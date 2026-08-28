using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using StreamFrame;

namespace SecsFrame;

internal sealed class StreamFrameHsmsTransport : IHsmsTransport
{
    private readonly IStreamConnection<HsmsTransportFrame> _connection;
    private readonly HsmsTransportSessionContext _sessionContext;
    private readonly HsmsIncompleteFrameMonitor _incompleteFrameMonitor;
    private readonly Channel<HsmsTransportEvent> _events;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
#if NET9_0_OR_GREATER
    private readonly Lock _pendingGate = new();
    private readonly Lock _closeReasonGate = new();
#else
    private readonly object _pendingGate = new();
    private readonly object _closeReasonGate = new();
#endif
    private PendingSend? _pendingSend;
    private Exception? _nextCloseReason;
    private Task? _messagePump;
    private int _started;
    private int _disposed;

    internal StreamFrameHsmsTransport(
        IStreamConnection<HsmsTransportFrame> connection,
        HsmsTransportSessionContext sessionContext,
        TimeSpan incompleteFrameTimeout,
        IHsmsTransportTimerFactory? timerFactory)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _events = Channel.CreateUnbounded<HsmsTransportEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _incompleteFrameMonitor = new HsmsIncompleteFrameMonitor(
            incompleteFrameTimeout,
            OnIncompleteFrameTimeout,
            timerFactory);

        _connection.ConnectionChanged += OnConnectionChanged;
        _connection.RawBytesReceived = OnRawBytesReceived;
        _connection.RawBytesSent = OnRawBytesSent;
    }

    public static StreamFrameHsmsTransport Create(
        IPAddress ipAddress,
        int port,
        bool isActive,
        TimeSpan incompleteFrameTimeout,
        StreamConnectionOptions? connectionOptions = null,
        HsmsFramer? framer = null)
    {
        var sessionContext = new HsmsTransportSessionContext();
        var connection = new StreamConnection<HsmsTransportFrame>(
            framer ?? new HsmsFramer(),
            new SessionBoundHsmsFrameCodec(sessionContext),
            ipAddress,
            port,
            isActive,
            connectionOptions);
        return new StreamFrameHsmsTransport(
            connection,
            sessionContext,
            incompleteFrameTimeout,
            timerFactory: null);
    }

    public void Start(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The HSMS transport has already been started.");

        _messagePump = PumpMessagesAsync();
        try
        {
            _connection.Start(cancellationToken);
        }
        catch (Exception ex)
        {
            _events.Writer.TryComplete(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<HsmsTransportEvent> GetEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _events.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var transportEvent))
                yield return transportEvent;
        }
    }

    public async ValueTask SendAsync(
        HsmsTransportSessionId sessionId,
        HsmsFrame frame,
        CancellationToken cancellationToken)
    {
        if (frame is null)
            throw new ArgumentNullException(nameof(frame));
        ThrowIfDisposed();
        EnsureCurrentSession(sessionId);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        PendingSend? pending = null;
        try
        {
            ThrowIfDisposed();
            EnsureCurrentSession(sessionId);

            pending = new PendingSend(GetWireLength(frame));
            lock (_pendingGate)
            {
                EnsureCurrentSession(sessionId);
                _pendingSend = pending;
            }

            await _connection.SendAsync(
                new HsmsTransportFrame(sessionId, frame),
                cancellationToken).ConfigureAwait(false);

            await pending.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            if (pending is not null)
            {
                lock (_pendingGate)
                {
                    if (ReferenceEquals(_pendingSend, pending))
                        _pendingSend = null;
                }
            }

            _sendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        FailPending(new ObjectDisposedException(nameof(StreamFrameHsmsTransport)));
        await _connection.DisposeAsync().ConfigureAwait(false);

        if (_messagePump is not null)
        {
            try
            {
                await _messagePump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _connection.ConnectionChanged -= OnConnectionChanged;
        _connection.RawBytesReceived = null;
        _connection.RawBytesSent = null;
        _incompleteFrameMonitor.Dispose();
        _events.Writer.TryComplete();
    }

    private async Task PumpMessagesAsync()
    {
        var messages = _connection.GetMessages(CancellationToken.None).GetAsyncEnumerator();
        try
        {
            while (await messages.MoveNextAsync().ConfigureAwait(false))
            {
                var message = messages.Current;
                if (!_events.Writer.TryWrite(
                    HsmsTransportEvent.FrameReceived(message.SessionId, message.Frame)))
                {
                    break;
                }
            }

            _events.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _events.Writer.TryComplete(ex);
        }
        finally
        {
            await messages.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnConnectionChanged(object? sender, ConnectionState state)
    {
        if (state == ConnectionState.Connected)
        {
            _incompleteFrameMonitor.Reset();
            var sessionId = _sessionContext.Open();
            _events.Writer.TryWrite(HsmsTransportEvent.SessionOpened(sessionId));
            return;
        }

        if (!_sessionContext.TryClose(out var closedSessionId))
            return;

        _incompleteFrameMonitor.Reset();
        var exception = TakeCloseReason();
        FailPending(
            exception ?? new HsmsTransportSessionExpiredException(closedSessionId));
        _events.Writer.TryWrite(HsmsTransportEvent.SessionClosed(closedSessionId, exception));
    }

    private void OnRawBytesReceived(ReadOnlyMemory<byte> bytes)
        => _incompleteFrameMonitor.Observe(bytes.Span);

    private void OnRawBytesSent(ReadOnlyMemory<byte> bytes)
    {
        PendingSend? completed = null;
        lock (_pendingGate)
        {
            var pending = _pendingSend;
            if (pending is null || pending.Completion.Task.IsCompleted)
                return;

            pending.RemainingBytes -= bytes.Length;
            if (pending.RemainingBytes == 0)
                completed = pending;
            else if (pending.RemainingBytes < 0)
            {
                pending.Completion.TrySetException(
                    new InvalidOperationException("StreamFrame reported more sent bytes than the pending HSMS frame contains."));
            }
        }

        completed?.Completion.TrySetResult(true);
    }

    private void OnIncompleteFrameTimeout()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        lock (_closeReasonGate)
        {
            _nextCloseReason = new TimeoutException(
                "The active HSMS transport session exceeded its incomplete-frame timeout.");
        }

        try
        {
            _connection.Reconnect();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            lock (_closeReasonGate)
                _nextCloseReason = null;
            FailPending(ex);
        }
    }

    private void FailPending(Exception exception)
    {
        PendingSend? pending;
        lock (_pendingGate)
            pending = _pendingSend;

        pending?.Completion.TrySetException(exception);
    }

    private Exception? TakeCloseReason()
    {
        lock (_closeReasonGate)
        {
            var exception = _nextCloseReason;
            _nextCloseReason = null;
            return exception;
        }
    }

    private void EnsureCurrentSession(HsmsTransportSessionId sessionId)
    {
        if (!_sessionContext.IsCurrent(sessionId))
            throw new HsmsTransportSessionExpiredException(sessionId);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(StreamFrameHsmsTransport));
    }

    private static long GetWireLength(HsmsFrame frame)
        => checked(
            (long)HsmsFramer.LengthPrefixSize +
            HsmsMessageHeader.EncodedSize +
            frame.Body.Length);

    private sealed class PendingSend
    {
        public PendingSend(long remainingBytes)
        {
            RemainingBytes = remainingBytes;
            Completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public long RemainingBytes { get; set; }

        public TaskCompletionSource<bool> Completion { get; }
    }
}
