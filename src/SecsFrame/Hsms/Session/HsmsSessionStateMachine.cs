using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace SecsFrame;

internal sealed class HsmsSessionStateMachine : IAsyncDisposable
{
    private readonly IHsmsTransport _transport;
    private readonly HsmsSessionOptions _options;
    private readonly IHsmsTransportTimerFactory _timerFactory;
    private readonly IHsmsSystemBytesProvider _systemBytesProvider;
    private readonly Channel<MachineInput> _inputs;
    private readonly Channel<HsmsSessionEvent> _events;
    private CancellationTokenSource? _lifetime;
    private Task? _transportPump;
    private Task? _processor;
    private IHsmsTransportTimer? _t6Timer;
    private IHsmsTransportTimer? _t7Timer;
    private HsmsTransportSessionId _sessionId;
    private uint? _pendingSelectSystemBytes;
    private TaskCompletionSource<bool>? _separateCompletion;
    private int _t6Generation;
    private int _t7Generation;
    private int _state;
    private int _started;
    private int _disposed;

    public HsmsSessionStateMachine(
        IHsmsTransport transport,
        HsmsSessionOptions options,
        IHsmsTransportTimerFactory? timerFactory = null,
        IHsmsSystemBytesProvider? systemBytesProvider = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timerFactory = timerFactory ?? SystemHsmsTransportTimerFactory.Instance;
        _systemBytesProvider = systemBytesProvider ?? new IncrementingHsmsSystemBytesProvider();
        _inputs = Channel.CreateUnbounded<MachineInput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _events = Channel.CreateUnbounded<HsmsSessionEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    }

    public HsmsSessionState State
        => (HsmsSessionState)Volatile.Read(ref _state);

    public void Start(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The HSMS session state machine has already been started.");

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processor = ProcessInputsAsync();
        _transportPump = PumpTransportEventsAsync(_lifetime.Token);
        try
        {
            _transport.Start(_lifetime.Token);
        }
        catch (Exception ex)
        {
            _lifetime.Cancel();
            _inputs.Writer.TryComplete(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<HsmsSessionEvent> GetEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _events.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var sessionEvent))
                yield return sessionEvent;
        }
    }

    public Task SeparateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The HSMS session state machine has not been started.");
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inputs.Writer.TryWrite(MachineInput.SeparateRequested(completion, cancellationToken)))
            throw new InvalidOperationException("The HSMS session state machine is no longer accepting commands.");

        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime?.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _inputs.Writer.TryComplete();

        if (_transportPump is not null)
        {
            try
            {
                await _transportPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_processor is not null)
            await _processor.ConfigureAwait(false);

        CancelT6();
        CancelT7();
        _separateCompletion?.TrySetCanceled();
        _events.Writer.TryComplete();
        _lifetime?.Dispose();
    }

    private async Task PumpTransportEventsAsync(CancellationToken cancellationToken)
    {
        var events = _transport.GetEventsAsync(cancellationToken).GetAsyncEnumerator();
        try
        {
            while (await events.MoveNextAsync().ConfigureAwait(false))
            {
                if (!_inputs.Writer.TryWrite(MachineInput.Transport(events.Current)))
                    break;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _inputs.Writer.TryWrite(
                    MachineInput.TransportFailed(
                        new IOException(
                            "The HSMS transport event stream ended before the state machine was stopped.")));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _inputs.Writer.TryWrite(MachineInput.TransportFailed(ex));
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
            _inputs.Writer.TryComplete();
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
            CancelT6();
            CancelT7();
            _events.Writer.TryComplete();
        }
    }

    private void ProcessInput(MachineInput input)
    {
        switch (input.Kind)
        {
            case MachineInputKind.Transport:
                ProcessTransportEvent(input.TransportEvent);
                break;
            case MachineInputKind.SendCompleted:
                ProcessSendCompleted(input.SendOperation);
                break;
            case MachineInputKind.SendFailed:
                ProcessSendFailed(input.SendOperation, input.Error!);
                break;
            case MachineInputKind.T6Expired:
                ProcessT6Expired(input.Generation);
                break;
            case MachineInputKind.T7Expired:
                ProcessT7Expired(input.Generation);
                break;
            case MachineInputKind.SeparateRequested:
                ProcessSeparateRequested(input.Completion!, input.CancellationToken);
                break;
            case MachineInputKind.TransportFailed:
                ProcessTransportFailure(input.Error!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Kind, "Unknown state-machine input.");
        }
    }

    private void ProcessTransportEvent(HsmsTransportEvent transportEvent)
    {
        switch (transportEvent.Kind)
        {
            case HsmsTransportEventKind.SessionOpened:
                ProcessSessionOpened(transportEvent.SessionId);
                break;
            case HsmsTransportEventKind.FrameReceived:
                if (transportEvent.SessionId == _sessionId)
                    ProcessFrame(transportEvent.Frame!);
                break;
            case HsmsTransportEventKind.SessionClosed:
                if (transportEvent.SessionId == _sessionId)
                    ProcessSessionClosed(transportEvent.Error);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(transportEvent),
                    transportEvent.Kind,
                    "Unknown transport event.");
        }
    }

    private void ProcessSessionOpened(HsmsTransportSessionId sessionId)
    {
        if (_sessionId.IsValid)
        {
            ProcessSessionClosed(
                new HsmsProtocolException(
                    $"Transport session {sessionId.Value} opened before session {_sessionId.Value} closed."));
        }

        _sessionId = sessionId;
        _pendingSelectSystemBytes = null;
        Transition(HsmsSessionState.Connected);
        ArmT7();

        if (_options.ConnectionMode != HsmsConnectionMode.Active)
            return;

        Transition(HsmsSessionState.Selecting);
        var systemBytes = _systemBytesProvider.Next();
        _pendingSelectSystemBytes = systemBytes;
        StartControlSend(
            new SendOperation(
                sessionId,
                SendPurpose.SelectRequest,
                systemBytes,
                HsmsSelectStatus.Success));
    }

    private void ProcessSessionClosed(Exception? error)
    {
        if (!_sessionId.IsValid)
            return;

        var closedSessionId = _sessionId;
        CancelT6();
        CancelT7();
        _pendingSelectSystemBytes = null;
        _sessionId = default;
        _separateCompletion?.TrySetException(
            error ?? new HsmsTransportSessionExpiredException(closedSessionId));
        _separateCompletion = null;
        Transition(closedSessionId, HsmsSessionState.Disconnected, error);
    }

    private void ProcessFrame(HsmsFrame frame)
    {
        var header = frame.Header;
        if (header.IsDataMessage)
        {
            if (State != HsmsSessionState.Selected)
            {
                AbortCurrentSession(
                    new HsmsProtocolException(
                        "An HSMS data message was received before the session was selected."));
                return;
            }

            _events.Writer.TryWrite(
                HsmsSessionEvent.DataMessageReceived(_sessionId, frame));
            return;
        }

        var validationError = ValidateControlHeader(header);
        if (validationError is not null)
        {
            AbortCurrentSession(validationError);
            return;
        }

        switch (header.MessageType)
        {
            case HsmsMessageType.SelectRequest:
                ProcessSelectRequest(header);
                break;
            case HsmsMessageType.SelectResponse:
                ProcessSelectResponse(header);
                break;
            case HsmsMessageType.SeparateRequest:
                ProcessSeparateRequest(header);
                break;
            default:
                AbortCurrentSession(
                    new HsmsProtocolException(
                        $"Control message {header.MessageType} is not supported by the Select/Separate state machine."));
                break;
        }
    }

    private void ProcessSelectRequest(HsmsMessageHeader header)
    {
        if (header.HeaderByte3 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException("Select Request must use status byte zero."));
            return;
        }

        var status = State == HsmsSessionState.Selected
            ? HsmsSelectStatus.AlreadySelected
            : HsmsSelectStatus.Success;
        StartControlSend(
            new SendOperation(
                _sessionId,
                SendPurpose.SelectResponse,
                header.SystemBytes,
                status));
    }

    private void ProcessSelectResponse(HsmsMessageHeader header)
    {
        if (_pendingSelectSystemBytes is not { } expectedSystemBytes ||
            header.SystemBytes != expectedSystemBytes)
        {
            AbortCurrentSession(
                new HsmsProtocolException(
                    $"Unexpected Select Response System Bytes 0x{header.SystemBytes:X8}."));
            return;
        }

        _pendingSelectSystemBytes = null;
        CancelT6();
        var status = DecodeSelectStatus(header.HeaderByte3);
        if (status == HsmsSelectStatus.Success)
        {
            EnterSelected();
            return;
        }

        Transition(
            HsmsSessionState.Connected,
            new HsmsSelectionRejectedException(status));
    }

    private void ProcessSeparateRequest(HsmsMessageHeader header)
    {
        if (header.HeaderByte3 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException("Separate Request must use status byte zero."));
            return;
        }

        _transport.TryCloseSession(_sessionId);
    }

    private void ProcessSendCompleted(SendOperation operation)
    {
        if (operation.SessionId != _sessionId)
            return;

        switch (operation.Purpose)
        {
            case SendPurpose.SelectRequest:
                if (_pendingSelectSystemBytes == operation.SystemBytes &&
                    State != HsmsSessionState.Selected)
                {
                    ArmT6();
                }
                break;
            case SendPurpose.SelectResponse:
                if (operation.SelectStatus == HsmsSelectStatus.Success)
                    EnterSelected();
                break;
            case SendPurpose.Separate:
                _separateCompletion?.TrySetResult(true);
                _separateCompletion = null;
                _transport.TryCloseSession(_sessionId);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(operation),
                    operation.Purpose,
                    "Unknown send purpose.");
        }
    }

    private void ProcessSendFailed(SendOperation operation, Exception error)
    {
        if (operation.SessionId != _sessionId)
            return;

        if (operation.Purpose == SendPurpose.Separate)
        {
            _separateCompletion?.TrySetException(error);
            _separateCompletion = null;
        }

        AbortCurrentSession(error);
    }

    private void ProcessT6Expired(int generation)
    {
        if (generation != _t6Generation ||
            !_sessionId.IsValid ||
            _pendingSelectSystemBytes is null ||
            State == HsmsSessionState.Selected)
        {
            return;
        }

        AbortCurrentSession(new HsmsSessionTimeoutException("T6"));
    }

    private void ProcessT7Expired(int generation)
    {
        if (generation != _t7Generation ||
            !_sessionId.IsValid ||
            State == HsmsSessionState.Selected)
        {
            return;
        }

        AbortCurrentSession(new HsmsSessionTimeoutException("T7"));
    }

    private void ProcessSeparateRequested(
        TaskCompletionSource<bool> completion,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

        if (State != HsmsSessionState.Selected || !_sessionId.IsValid)
        {
            completion.TrySetException(
                new InvalidOperationException("Separate Request requires a selected HSMS session."));
            return;
        }

        if (_separateCompletion is not null)
        {
            completion.TrySetException(
                new InvalidOperationException("A Separate Request is already in progress."));
            return;
        }

        _separateCompletion = completion;
        StartControlSend(
            new SendOperation(
                _sessionId,
                SendPurpose.Separate,
                _systemBytesProvider.Next(),
                HsmsSelectStatus.Success));
    }

    private void ProcessTransportFailure(Exception error)
    {
        if (_sessionId.IsValid)
            ProcessSessionClosed(error);
    }

    private void StartControlSend(SendOperation operation)
    {
        var messageType = operation.Purpose switch
        {
            SendPurpose.SelectRequest => HsmsMessageType.SelectRequest,
            SendPurpose.SelectResponse => HsmsMessageType.SelectResponse,
            SendPurpose.Separate => HsmsMessageType.SeparateRequest,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Purpose,
                "Unknown send purpose."),
        };
        var status = operation.Purpose == SendPurpose.SelectResponse
            ? (byte)operation.SelectStatus
            : (byte)0;
        var frame = new HsmsFrame(
            HsmsMessageHeader.CreateControl(
                messageType,
                operation.SystemBytes,
                status));

        _ = SendControlAsync(operation, frame);
    }

    private async Task SendControlAsync(SendOperation operation, HsmsFrame frame)
    {
        try
        {
            await _transport.SendAsync(
                operation.SessionId,
                frame,
                _lifetime?.Token ?? CancellationToken.None).ConfigureAwait(false);
            _inputs.Writer.TryWrite(MachineInput.SendCompleted(operation));
        }
        catch (OperationCanceledException) when (_lifetime?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            _inputs.Writer.TryWrite(MachineInput.SendFailed(operation, ex));
        }
    }

    private void EnterSelected()
    {
        CancelT6();
        CancelT7();
        Transition(HsmsSessionState.Selected);
    }

    private void AbortCurrentSession(Exception error)
    {
        if (_sessionId.IsValid)
            _transport.TryCloseSession(_sessionId, error);
    }

    private void ArmT6()
    {
        CancelT6();
        var generation = _t6Generation;
        _t6Timer = _timerFactory.Create(
            () => _inputs.Writer.TryWrite(MachineInput.T6Expired(generation)));
        _t6Timer.Change(_options.ControlReplyTimeout);
    }

    private void CancelT6()
    {
        _t6Generation++;
        _t6Timer?.Dispose();
        _t6Timer = null;
    }

    private void ArmT7()
    {
        CancelT7();
        var generation = _t7Generation;
        _t7Timer = _timerFactory.Create(
            () => _inputs.Writer.TryWrite(MachineInput.T7Expired(generation)));
        _t7Timer.Change(_options.SelectionTimeout);
    }

    private void CancelT7()
    {
        _t7Generation++;
        _t7Timer?.Dispose();
        _t7Timer = null;
    }

    private void Transition(HsmsSessionState state, Exception? error = null)
        => Transition(_sessionId, state, error);

    private void Transition(
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        Exception? error = null)
    {
        var previous = (HsmsSessionState)Interlocked.Exchange(ref _state, (int)state);
        if (previous == state && error is null)
            return;

        _events.Writer.TryWrite(
            HsmsSessionEvent.StateChanged(sessionId, state, error));
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(HsmsSessionStateMachine));
    }

    private static HsmsProtocolException? ValidateControlHeader(HsmsMessageHeader header)
    {
        if (header.SessionId != ushort.MaxValue)
            return new HsmsProtocolException("An HSMS control message must use Session ID 0xFFFF.");
        if (header.HeaderByte2 != 0)
            return new HsmsProtocolException("An HSMS control message must use header byte 2 value zero.");
        if (header.PresentationType != 0)
            return new HsmsProtocolException("An HSMS control message must use PType zero.");

        return null;
    }

    private static HsmsSelectStatus DecodeSelectStatus(byte value)
        => value switch
        {
            (byte)HsmsSelectStatus.Success => HsmsSelectStatus.Success,
            (byte)HsmsSelectStatus.AlreadySelected => HsmsSelectStatus.AlreadySelected,
            (byte)HsmsSelectStatus.NotReady => HsmsSelectStatus.NotReady,
            (byte)HsmsSelectStatus.Unavailable => HsmsSelectStatus.Unavailable,
            _ => (HsmsSelectStatus)value,
        };

    private enum MachineInputKind
    {
        Transport,
        SendCompleted,
        SendFailed,
        T6Expired,
        T7Expired,
        SeparateRequested,
        TransportFailed,
    }

    private enum SendPurpose
    {
        SelectRequest,
        SelectResponse,
        Separate,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SendOperation(
        HsmsTransportSessionId SessionId,
        SendPurpose Purpose,
        uint SystemBytes,
        HsmsSelectStatus SelectStatus);

    private sealed class MachineInput
    {
        private MachineInput(MachineInputKind kind)
        {
            Kind = kind;
        }

        public MachineInputKind Kind { get; }

        public HsmsTransportEvent TransportEvent { get; private init; }

        public SendOperation SendOperation { get; private init; }

        public Exception? Error { get; private init; }

        public int Generation { get; private init; }

        public TaskCompletionSource<bool>? Completion { get; private init; }

        public CancellationToken CancellationToken { get; private init; }

        public static MachineInput Transport(HsmsTransportEvent transportEvent)
            => new(MachineInputKind.Transport) { TransportEvent = transportEvent };

        public static MachineInput SendCompleted(SendOperation operation)
            => new(MachineInputKind.SendCompleted) { SendOperation = operation };

        public static MachineInput SendFailed(SendOperation operation, Exception error)
            => new(MachineInputKind.SendFailed) { SendOperation = operation, Error = error };

        public static MachineInput T6Expired(int generation)
            => new(MachineInputKind.T6Expired) { Generation = generation };

        public static MachineInput T7Expired(int generation)
            => new(MachineInputKind.T7Expired) { Generation = generation };

        public static MachineInput SeparateRequested(
            TaskCompletionSource<bool> completion,
            CancellationToken cancellationToken)
            => new(MachineInputKind.SeparateRequested)
            {
                Completion = completion,
                CancellationToken = cancellationToken,
            };

        public static MachineInput TransportFailed(Exception error)
            => new(MachineInputKind.TransportFailed) { Error = error };
    }
}
