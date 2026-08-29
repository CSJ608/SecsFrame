using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace SecsFrame;

internal sealed class HsmsDataTransactionManager : IAsyncDisposable
{
    private readonly HsmsSessionStateMachine _session;
    private readonly HsmsDataTransactionOptions _options;
    private readonly HsmsDataMessageCodec _codec;
    private readonly IHsmsTransportTimerFactory _timerFactory;
    private readonly IHsmsSystemBytesProvider _systemBytesProvider;
    private readonly Channel<TransactionInput> _inputs;
    private readonly Channel<HsmsDataTransactionEvent> _events;
    private readonly HashSet<OutgoingSend> _outgoingSends = new();
    private readonly Dictionary<TransactionKey, OutgoingSend> _pendingTransactions = new();
    private CancellationTokenSource? _lifetime;
    private Task? _sessionPump;
    private Task? _processor;
    private HsmsTransportSessionId _sessionId;
    private int _started;
    private int _disposed;

    public HsmsDataTransactionManager(
        HsmsSessionStateMachine session,
        HsmsDataTransactionOptions options,
        HsmsDataMessageCodec? codec = null,
        IHsmsTransportTimerFactory? timerFactory = null,
        IHsmsSystemBytesProvider? systemBytesProvider = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _codec = codec ?? new HsmsDataMessageCodec();
        _timerFactory = timerFactory ?? SystemHsmsTransportTimerFactory.Instance;
        _systemBytesProvider = systemBytesProvider ?? new IncrementingHsmsSystemBytesProvider();
        _inputs = Channel.CreateUnbounded<TransactionInput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _events = Channel.CreateUnbounded<HsmsDataTransactionEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    }

    public HsmsSessionState State => _session.State;

    public void Start(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The HSMS data transaction manager has already been started.");

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processor = ProcessInputsAsync();
        _sessionPump = PumpSessionEventsAsync(_lifetime.Token);
        try
        {
            _session.Start(_lifetime.Token);
        }
        catch (Exception ex)
        {
            _lifetime.Cancel();
            _inputs.Writer.TryComplete(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<HsmsDataTransactionEvent> GetEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _events.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var transactionEvent))
                yield return transactionEvent;
        }
    }

    public IAsyncEnumerable<HsmsControlMessageObservation>
        GetControlMessageObservationsAsync(CancellationToken cancellationToken)
        => _session.GetControlMessageObservationsAsync(cancellationToken);

    public Task<HsmsDataMessage?> SendAsync(
        ushort sessionId,
        SecsMessage primary,
        CancellationToken cancellationToken = default)
    {
        if (primary is null)
            throw new ArgumentNullException(nameof(primary));

        ThrowIfNotRunning();
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<HsmsDataMessage?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inputs.Writer.TryWrite(
            TransactionInput.SendPrimaryRequested(
                sessionId,
                primary,
                completion,
                cancellationToken)))
        {
            throw new InvalidOperationException("The HSMS data transaction manager is no longer accepting commands.");
        }

        return completion.Task;
    }

    public Task ReplyAsync(
        HsmsIncomingDataMessage incoming,
        SecsMessage secondary,
        CancellationToken cancellationToken = default)
    {
        if (incoming is null)
            throw new ArgumentNullException(nameof(incoming));
        if (secondary is null)
            throw new ArgumentNullException(nameof(secondary));
        if (!incoming.IsOwnedBy(this))
        {
            throw new InvalidOperationException(
                "The incoming data message belongs to another HSMS connection.");
        }

        if (!incoming.DataMessage.Message.ReplyExpected)
        {
            throw new InvalidOperationException(
                "The incoming data message does not request a secondary reply.");
        }

        if (secondary.ReplyExpected)
        {
            throw new ArgumentException(
                "A secondary message cannot request another reply.",
                nameof(secondary));
        }

        ThrowIfNotRunning();
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<HsmsDataMessage?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inputs.Writer.TryWrite(
            TransactionInput.ReplyRequested(
                incoming,
                secondary,
                completion,
                cancellationToken)))
        {
            throw new InvalidOperationException("The HSMS data transaction manager is no longer accepting commands.");
        }

        return AwaitReplyAsync(completion.Task);
    }

    public Task LinktestAsync(CancellationToken cancellationToken = default)
        => _session.LinktestAsync(cancellationToken);

    public Task DeselectAsync(CancellationToken cancellationToken = default)
        => _session.DeselectAsync(cancellationToken);

    public Task SeparateAsync(CancellationToken cancellationToken = default)
        => _session.SeparateAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime?.Cancel();
        await _session.DisposeAsync().ConfigureAwait(false);
        _inputs.Writer.TryComplete();

        if (_sessionPump is not null)
        {
            try
            {
                await _sessionPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_processor is not null)
            await _processor.ConfigureAwait(false);

        _events.Writer.TryComplete();
        _lifetime?.Dispose();
    }

    private static async Task AwaitReplyAsync(Task<HsmsDataMessage?> completion)
        => await completion.ConfigureAwait(false);

    private async Task PumpSessionEventsAsync(CancellationToken cancellationToken)
    {
        var events = _session.GetEventsAsync(cancellationToken).GetAsyncEnumerator();
        try
        {
            while (await events.MoveNextAsync().ConfigureAwait(false))
            {
                if (!_inputs.Writer.TryWrite(
                    TransactionInput.FromSessionEvent(events.Current)))
                {
                    break;
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _inputs.Writer.TryWrite(
                    TransactionInput.SessionPumpFailed(
                        new IOException(
                            "The HSMS session event stream ended before the transaction manager was stopped.")));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _inputs.Writer.TryWrite(TransactionInput.SessionPumpFailed(ex));
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessInputsAsync()
    {
        try
        {
            var reader = _inputs.Reader;
            while (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
            {
                while (reader.TryRead(out var input))
                    ProcessInput(input);
            }
        }
        finally
        {
            FailAll(new ObjectDisposedException(nameof(HsmsDataTransactionManager)));
            _events.Writer.TryComplete();
        }
    }

    private void ProcessInput(TransactionInput input)
    {
        switch (input.Kind)
        {
            case TransactionInputKind.SessionEvent:
                ProcessSessionEvent(input.SessionEvent);
                break;
            case TransactionInputKind.SendPrimaryRequested:
                ProcessSendPrimaryRequested(input);
                break;
            case TransactionInputKind.ReplyRequested:
                ProcessReplyRequested(input);
                break;
            case TransactionInputKind.SendCompleted:
                ProcessSendCompleted(input.Send!);
                break;
            case TransactionInputKind.SendFailed:
                ProcessSendFailed(input.Send!, input.Error!);
                break;
            case TransactionInputKind.SendCanceled:
                ProcessSendCanceled(input.Send!);
                break;
            case TransactionInputKind.T3Expired:
                ProcessT3Expired(input.Send!);
                break;
            case TransactionInputKind.SessionPumpFailed:
                FailAll(input.Error!);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(input),
                    input.Kind,
                    "Unknown transaction-manager input.");
        }
    }

    private void ProcessSessionEvent(HsmsSessionEvent sessionEvent)
    {
        switch (sessionEvent.Kind)
        {
            case HsmsSessionEventKind.StateChanged:
                ProcessStateChanged(sessionEvent);
                break;
            case HsmsSessionEventKind.DataMessageReceived:
                ProcessDataMessageReceived(sessionEvent);
                break;
            case HsmsSessionEventKind.ControlMessageReceived:
                ProcessControlMessageReceived(sessionEvent);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(sessionEvent),
                    sessionEvent.Kind,
                    "Unknown HSMS session event.");
        }
    }

    private void ProcessStateChanged(HsmsSessionEvent sessionEvent)
    {
        if (sessionEvent.State != HsmsSessionState.Selected)
        {
            var error =
                sessionEvent.Error ??
                new HsmsDataTransactionInterruptedException(sessionEvent.State);
            FailSession(sessionEvent.SessionId, error);
        }

        _sessionId = sessionEvent.State == HsmsSessionState.Disconnected
            ? default
            : sessionEvent.SessionId;
        _events.Writer.TryWrite(
            HsmsDataTransactionEvent.StateChanged(
                sessionEvent.SessionId,
                sessionEvent.State,
                sessionEvent.Error));
    }

    private void ProcessDataMessageReceived(HsmsSessionEvent sessionEvent)
    {
        HsmsDataMessage dataMessage;
        try
        {
            dataMessage = _codec.Decode(sessionEvent.Frame!);
        }
        catch (Exception ex) when (
            ex is HsmsProtocolException ||
            ex is SecsProtocolException ||
            ex is ArgumentException)
        {
            FailMatchingMalformedSecondary(sessionEvent, ex);
            _events.Writer.TryWrite(
                HsmsDataTransactionEvent.DataMessageDecodeFailed(
                    sessionEvent.SessionId,
                    sessionEvent.Frame!,
                    ex));
            return;
        }

        var key = new TransactionKey(
            sessionEvent.SessionId,
            dataMessage.SessionId,
            dataMessage.SystemBytes);
        if (!dataMessage.Message.ReplyExpected &&
            _pendingTransactions.TryGetValue(key, out var pending))
        {
            CompleteSend(pending, dataMessage);
            return;
        }

        var incoming = new HsmsIncomingDataMessage(
            this,
            sessionEvent.SessionId,
            dataMessage);
        _events.Writer.TryWrite(
            HsmsDataTransactionEvent.DataMessageReceived(
                sessionEvent.SessionId,
                incoming));
    }

    private void FailMatchingMalformedSecondary(
        HsmsSessionEvent sessionEvent,
        Exception error)
    {
        var header = sessionEvent.Frame!.Header;
        if (header.ReplyExpected)
            return;

        var key = new TransactionKey(
            sessionEvent.SessionId,
            header.SessionId,
            header.SystemBytes);
        if (_pendingTransactions.TryGetValue(key, out var pending))
            FailSend(pending, error);
    }

    private void ProcessControlMessageReceived(HsmsSessionEvent sessionEvent)
    {
        var frame = sessionEvent.Frame!;
        var header = frame.Header;
        if (header.MessageType == HsmsMessageType.RejectRequest &&
            header.HeaderByte2 == (byte)HsmsMessageType.DataMessage)
        {
            var pending = FindPending(
                sessionEvent.SessionId,
                header.SystemBytes);
            if (pending is not null)
            {
                FailSend(
                    pending,
                    new HsmsDataMessageRejectedException(
                        (HsmsRejectReason)header.HeaderByte3));
            }
        }

        _events.Writer.TryWrite(
            HsmsDataTransactionEvent.ControlMessageReceived(
                sessionEvent.SessionId,
                sessionEvent.State,
                frame));
    }

    private void ProcessSendPrimaryRequested(TransactionInput input)
    {
        var completion = input.Completion!;
        if (IsStopping)
        {
            completion.TrySetException(
                new ObjectDisposedException(nameof(HsmsDataTransactionManager)));
            return;
        }

        if (input.CancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(input.CancellationToken);
            return;
        }

        if (State != HsmsSessionState.Selected || !_sessionId.IsValid)
        {
            completion.TrySetException(
                new InvalidOperationException(
                    "A primary message requires a selected HSMS session."));
            return;
        }

        uint systemBytes;
        try
        {
            systemBytes = AllocateSystemBytes(_sessionId);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return;
        }

        var dataMessage = new HsmsDataMessage(
            input.ProtocolSessionId,
            systemBytes,
            input.Message!);
        StartSend(
            new OutgoingSend(
                this,
                _sessionId,
                dataMessage,
                dataMessage.Message.ReplyExpected,
                completion,
                input.CancellationToken));
    }

    private void ProcessReplyRequested(TransactionInput input)
    {
        var completion = input.Completion!;
        var incoming = input.Incoming!;
        if (IsStopping)
        {
            completion.TrySetException(
                new ObjectDisposedException(nameof(HsmsDataTransactionManager)));
            return;
        }

        if (input.CancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(input.CancellationToken);
            return;
        }

        if (State != HsmsSessionState.Selected ||
            !_sessionId.IsValid ||
            incoming.TransportSessionId != _sessionId)
        {
            completion.TrySetException(
                new HsmsDataTransactionInterruptedException(State));
            return;
        }

        var dataMessage = new HsmsDataMessage(
            incoming.DataMessage.SessionId,
            incoming.DataMessage.SystemBytes,
            input.Message!);
        try
        {
            _ = _codec.EncodeFrame(dataMessage);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return;
        }

        if (!incoming.TryBeginReply())
        {
            completion.TrySetException(
                new InvalidOperationException(
                    "A reply has already been started for this incoming data message."));
            return;
        }

        StartSend(
            new OutgoingSend(
                this,
                _sessionId,
                dataMessage,
                waitForReply: false,
                completion,
                input.CancellationToken));
    }

    private void StartSend(OutgoingSend send)
    {
        HsmsFrame frame;
        try
        {
            frame = _codec.EncodeFrame(send.DataMessage);
        }
        catch (Exception ex)
        {
            send.Completion.TrySetException(ex);
            return;
        }

        _outgoingSends.Add(send);
        if (send.WaitForReply)
            _pendingTransactions.Add(send.Key, send);
        send.RegisterCancellation();
        _ = SendFrameAsync(send, frame);
    }

    private async Task SendFrameAsync(
        OutgoingSend send,
        HsmsFrame frame)
    {
        try
        {
            await _session.SendDataAsync(frame, CancellationToken.None)
                .ConfigureAwait(false);
            _inputs.Writer.TryWrite(TransactionInput.SendCompleted(send));
        }
        catch (Exception ex)
        {
            _inputs.Writer.TryWrite(TransactionInput.SendFailed(send, ex));
        }
    }

    private void ProcessSendCompleted(OutgoingSend send)
    {
        if (!_outgoingSends.Contains(send))
            return;

        if (!send.WaitForReply)
        {
            CompleteSend(send, null);
            return;
        }

        try
        {
            send.Timer = _timerFactory.Create(
                () => _inputs.Writer.TryWrite(
                    TransactionInput.T3Expired(send)));
            send.Timer.Change(_options.ReplyTimeout);
        }
        catch (Exception ex)
        {
            FailSend(send, ex);
        }
    }

    private void ProcessSendFailed(
        OutgoingSend send,
        Exception error)
    {
        if (_outgoingSends.Contains(send))
            FailSend(send, error);
    }

    private void ProcessSendCanceled(OutgoingSend send)
    {
        if (!_outgoingSends.Remove(send))
            return;

        RemovePending(send);
        send.DisposeResources();
        send.Completion.TrySetCanceled(send.CancellationToken);
    }

    private void ProcessT3Expired(OutgoingSend send)
    {
        if (!_pendingTransactions.TryGetValue(send.Key, out var pending) ||
            !ReferenceEquals(pending, send))
        {
            return;
        }

        FailSend(
            send,
            new HsmsDataTransactionTimeoutException(send.DataMessage));
    }

    private void CompleteSend(
        OutgoingSend send,
        HsmsDataMessage? secondary)
    {
        if (!_outgoingSends.Remove(send))
            return;

        RemovePending(send);
        send.DisposeResources();
        send.Completion.TrySetResult(secondary);
    }

    private void FailSend(
        OutgoingSend send,
        Exception error)
    {
        if (!_outgoingSends.Remove(send))
            return;

        RemovePending(send);
        send.DisposeResources();
        send.Completion.TrySetException(error);
    }

    private void RemovePending(OutgoingSend send)
    {
        if (send.WaitForReply &&
            _pendingTransactions.TryGetValue(send.Key, out var pending) &&
            ReferenceEquals(pending, send))
        {
            _pendingTransactions.Remove(send.Key);
        }
    }

    private uint AllocateSystemBytes(HsmsTransportSessionId sessionId)
    {
        var attempts = _pendingTransactions.Count + 1;
        for (var index = 0; index < attempts; index++)
        {
            var candidate = _systemBytesProvider.Next();
            if (FindPending(sessionId, candidate) is null)
                return candidate;
        }

        throw new InvalidOperationException(
            "The System Bytes provider did not produce a value that is unique among open data transactions.");
    }

    private OutgoingSend? FindPending(
        HsmsTransportSessionId sessionId,
        uint systemBytes)
    {
        foreach (var pair in _pendingTransactions)
        {
            if (pair.Key.TransportSessionId == sessionId &&
                pair.Key.SystemBytes == systemBytes)
            {
                return pair.Value;
            }
        }

        return null;
    }

    private void FailSession(
        HsmsTransportSessionId sessionId,
        Exception error)
    {
        var sends = _outgoingSends
            .Where(send => send.TransportSessionId == sessionId)
            .ToArray();
        foreach (var send in sends)
            FailSend(send, error);
    }

    private void FailAll(Exception error)
    {
        var sends = _outgoingSends.ToArray();
        foreach (var send in sends)
            FailSend(send, error);
    }

    private bool IsStopping
        => Volatile.Read(ref _disposed) != 0 ||
            _lifetime?.IsCancellationRequested == true;

    private void ThrowIfNotRunning()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The HSMS data transaction manager has not been started.");
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(HsmsDataTransactionManager));
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TransactionKey(
        HsmsTransportSessionId TransportSessionId,
        ushort ProtocolSessionId,
        uint SystemBytes);

    private sealed class OutgoingSend
    {
        private readonly HsmsDataTransactionManager _owner;
        private CancellationTokenRegistration _cancellationRegistration;

        public OutgoingSend(
            HsmsDataTransactionManager owner,
            HsmsTransportSessionId transportSessionId,
            HsmsDataMessage dataMessage,
            bool waitForReply,
            TaskCompletionSource<HsmsDataMessage?> completion,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            TransportSessionId = transportSessionId;
            DataMessage = dataMessage;
            WaitForReply = waitForReply;
            Completion = completion;
            CancellationToken = cancellationToken;
            Key = new TransactionKey(
                transportSessionId,
                dataMessage.SessionId,
                dataMessage.SystemBytes);
        }

        public HsmsTransportSessionId TransportSessionId { get; }

        public HsmsDataMessage DataMessage { get; }

        public bool WaitForReply { get; }

        public TaskCompletionSource<HsmsDataMessage?> Completion { get; }

        public CancellationToken CancellationToken { get; }

        public TransactionKey Key { get; }

        public IHsmsTransportTimer? Timer { get; set; }

        public void RegisterCancellation()
        {
            if (!CancellationToken.CanBeCanceled)
                return;

            _cancellationRegistration = CancellationToken.Register(
                static state => ((OutgoingSend)state!).Cancel(),
                this);
        }

        public void DisposeResources()
        {
            Timer?.Dispose();
            Timer = null;
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
            => _owner._inputs.Writer.TryWrite(
                TransactionInput.SendCanceled(this));
    }

    private enum TransactionInputKind
    {
        SessionEvent,
        SendPrimaryRequested,
        ReplyRequested,
        SendCompleted,
        SendFailed,
        SendCanceled,
        T3Expired,
        SessionPumpFailed,
    }

    private sealed class TransactionInput
    {
        private TransactionInput(TransactionInputKind kind)
        {
            Kind = kind;
        }

        public TransactionInputKind Kind { get; }

        public HsmsSessionEvent SessionEvent { get; private init; }

        public ushort ProtocolSessionId { get; private init; }

        public SecsMessage? Message { get; private init; }

        public HsmsIncomingDataMessage? Incoming { get; private init; }

        public OutgoingSend? Send { get; private init; }

        public TaskCompletionSource<HsmsDataMessage?>? Completion { get; private init; }

        public CancellationToken CancellationToken { get; private init; }

        public Exception? Error { get; private init; }

        public static TransactionInput FromSessionEvent(HsmsSessionEvent sessionEvent)
            => new(TransactionInputKind.SessionEvent)
            {
                SessionEvent = sessionEvent,
            };

        public static TransactionInput SendPrimaryRequested(
            ushort sessionId,
            SecsMessage message,
            TaskCompletionSource<HsmsDataMessage?> completion,
            CancellationToken cancellationToken)
            => new(TransactionInputKind.SendPrimaryRequested)
            {
                ProtocolSessionId = sessionId,
                Message = message,
                Completion = completion,
                CancellationToken = cancellationToken,
            };

        public static TransactionInput ReplyRequested(
            HsmsIncomingDataMessage incoming,
            SecsMessage message,
            TaskCompletionSource<HsmsDataMessage?> completion,
            CancellationToken cancellationToken)
            => new(TransactionInputKind.ReplyRequested)
            {
                Incoming = incoming,
                Message = message,
                Completion = completion,
                CancellationToken = cancellationToken,
            };

        public static TransactionInput SendCompleted(OutgoingSend send)
            => new(TransactionInputKind.SendCompleted) { Send = send };

        public static TransactionInput SendFailed(
            OutgoingSend send,
            Exception error)
            => new(TransactionInputKind.SendFailed)
            {
                Send = send,
                Error = error,
            };

        public static TransactionInput SendCanceled(OutgoingSend send)
            => new(TransactionInputKind.SendCanceled) { Send = send };

        public static TransactionInput T3Expired(OutgoingSend send)
            => new(TransactionInputKind.T3Expired) { Send = send };

        public static TransactionInput SessionPumpFailed(Exception error)
            => new(TransactionInputKind.SessionPumpFailed) { Error = error };
    }
}
