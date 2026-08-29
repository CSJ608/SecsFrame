using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using StreamFrame;

namespace SecsFrame;

internal sealed class StreamFrameHsmsTransport : IHsmsTransport
{
    private readonly ISessionAwareStreamConnection<HsmsFrame> _connection;
    private readonly Channel<HsmsTransportEvent> _events;
    private readonly bool _enableTransportFaultObservation;
#if NET9_0_OR_GREATER
    private readonly Lock _lifecycleGate = new();
    private readonly Lock _sessionGate = new();
#else
    private readonly object _lifecycleGate = new();
    private readonly object _sessionGate = new();
#endif
    private readonly Dictionary<long, SessionContext> _sessions = new();
    private Task? _messagePump;
    private long _currentSessionId;
    private int _started;
    private int _disposed;

    internal StreamFrameHsmsTransport(
        ISessionAwareStreamConnection<HsmsFrame> connection,
        bool enableTransportFaultObservation = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _enableTransportFaultObservation = enableTransportFaultObservation;
        _events = Channel.CreateUnbounded<HsmsTransportEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        _connection.ConnectionChanged += OnConnectionChanged;
        _connection.FrameError += OnFrameError;
    }

    public static StreamFrameHsmsTransport Create(
        IPAddress ipAddress,
        int port,
        bool isActive,
        HsmsTransportOptions hsmsOptions,
        StreamConnectionOptions? connectionOptions = null,
        HsmsFramer? framer = null)
    {
        if (hsmsOptions is null)
            throw new ArgumentNullException(nameof(hsmsOptions));

        var adaptedOptions = HsmsStreamConnectionOptionsAdapter.Create(
            isActive,
            hsmsOptions,
            connectionOptions);
        var connection = new StreamConnection<HsmsFrame>(
            framer ?? new HsmsFramer(),
            new HsmsFrameCodec(),
            ipAddress,
            port,
            isActive,
            adaptedOptions);
        return new StreamFrameHsmsTransport(
            connection,
            hsmsOptions.EnableTransportFaultObservation);
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
        RegisterPendingSend(sessionId);

        try
        {
            await _connection.SendInSessionAsync(
                sessionId.Value,
                frame,
                cancellationToken).ConfigureAwait(false);
        }
        catch (SessionExpiredException ex)
        {
            var closeReason = GetCloseReason(sessionId);
            if (closeReason is not null)
            {
                ExceptionDispatchInfo.Capture(closeReason).Throw();
            }

            throw new HsmsTransportSessionExpiredException(
                sessionId,
                innerException: ex);
        }
        finally
        {
            CompletePendingSend(sessionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_lifecycleGate)
        {
            // Wait for an already-started Reconnect before disposing its lifetime state.
        }

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
        _connection.FrameError -= OnFrameError;
        _events.Writer.TryComplete();
    }

    public bool TryCloseSession(
        HsmsTransportSessionId sessionId,
        Exception? error = null)
    {
        ThrowIfDisposed();
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();

            Exception? previousReason;
            lock (_sessionGate)
            {
                if (_currentSessionId != sessionId.Value ||
                    _connection.CurrentSessionId != sessionId.Value ||
                    !_sessions.TryGetValue(sessionId.Value, out var context) ||
                    context.IsClosed)
                {
                    return false;
                }

                previousReason = context.CloseReason;
                if (error is not null && context.CloseReason is null)
                    context.CloseReason = error;
            }

            try
            {
                _connection.Reconnect();
                return true;
            }
            catch
            {
                if (error is not null)
                    RestoreCloseReason(sessionId, error, previousReason);
                throw;
            }
        }
    }

    private async Task PumpMessagesAsync()
    {
        var messages = _connection
            .GetSessionMessages(CancellationToken.None)
            .GetAsyncEnumerator();
        try
        {
            while (await messages.MoveNextAsync().ConfigureAwait(false))
            {
                var message = messages.Current;
                if (!_events.Writer.TryWrite(
                    HsmsTransportEvent.FrameReceived(
                        new HsmsTransportSessionId(message.SessionId),
                        message.Message)))
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
            var openedSessionId = _connection.CurrentSessionId;
            if (openedSessionId <= 0)
            {
                _events.Writer.TryComplete(
                    new InvalidOperationException(
                        "StreamFrame published Connected without a valid session identifier."));
                return;
            }

            lock (_sessionGate)
            {
                _sessions.Add(openedSessionId, new SessionContext());
                Volatile.Write(ref _currentSessionId, openedSessionId);
            }

            _events.Writer.TryWrite(
                HsmsTransportEvent.SessionOpened(
                    new HsmsTransportSessionId(openedSessionId)));
            return;
        }

        var closedSessionValue = Interlocked.Exchange(ref _currentSessionId, 0);
        if (closedSessionValue <= 0)
            return;

        Exception? closeReason = null;
        lock (_sessionGate)
        {
            if (_sessions.TryGetValue(closedSessionValue, out var context))
            {
                context.IsClosed = true;
                closeReason = context.CloseReason;
                RemoveSessionIfComplete(closedSessionValue, context);
            }
        }

        _events.Writer.TryWrite(
            HsmsTransportEvent.SessionClosed(
                new HsmsTransportSessionId(closedSessionValue),
                closeReason));
    }

    private void OnFrameError(object? sender, FrameErrorEventArgs args)
    {
        if (!TryMapFrameErrorKind(args.Kind, out var faultKind) ||
            args.SessionId <= 0)
        {
            return;
        }

        if (faultKind == HsmsTransportFaultKind.IncompleteFrameTimeout)
        {
            lock (_sessionGate)
            {
                if (_sessions.TryGetValue(args.SessionId, out var context) &&
                    !context.IsClosed)
                {
                    context.CloseReason ??= new HsmsT8TimeoutException(
                        new HsmsTransportSessionId(args.SessionId));
                }
            }
        }

        if (!_enableTransportFaultObservation)
            return;

        var snapshotLength = Math.Min(
            args.Bytes.Length,
            HsmsTransportFaultObservation.MaxSnapshotBytes);
        var isTruncated =
            args.IsTruncated ||
            snapshotLength < args.ObservedByteCount;
        _events.Writer.TryWrite(
            HsmsTransportEvent.TransportFaultObserved(
                new HsmsTransportSessionId(args.SessionId),
                faultKind,
                args.Bytes.Span[..snapshotLength],
                args.ObservedByteCount,
                isTruncated));
    }

    private static bool TryMapFrameErrorKind(
        FrameErrorKind kind,
        out HsmsTransportFaultKind faultKind)
    {
        switch (kind)
        {
            case FrameErrorKind.DecodeFailed:
                faultKind = HsmsTransportFaultKind.DecodeFailed;
                return true;
            case FrameErrorKind.DiscardedByResync:
                faultKind = HsmsTransportFaultKind.DiscardedByResync;
                return true;
            case FrameErrorKind.IncompleteFrameOverflow:
                faultKind = HsmsTransportFaultKind.IncompleteFrameOverflow;
                return true;
            case FrameErrorKind.IncompleteFrameTimeout:
                faultKind = HsmsTransportFaultKind.IncompleteFrameTimeout;
                return true;
            default:
                faultKind = default;
                return false;
        }
    }

    private void RegisterPendingSend(HsmsTransportSessionId sessionId)
    {
        lock (_sessionGate)
        {
            if (_currentSessionId != sessionId.Value ||
                _connection.CurrentSessionId != sessionId.Value ||
                !_sessions.TryGetValue(sessionId.Value, out var context) ||
                context.IsClosed)
            {
                throw new HsmsTransportSessionExpiredException(sessionId);
            }

            context.PendingSends++;
        }
    }

    private void CompletePendingSend(HsmsTransportSessionId sessionId)
    {
        lock (_sessionGate)
        {
            if (!_sessions.TryGetValue(sessionId.Value, out var context))
                return;

            context.PendingSends--;
            RemoveSessionIfComplete(sessionId.Value, context);
        }
    }

    private Exception? GetCloseReason(HsmsTransportSessionId sessionId)
    {
        lock (_sessionGate)
        {
            return _sessions.TryGetValue(sessionId.Value, out var context)
                ? context.CloseReason
                : null;
        }
    }

    private void RestoreCloseReason(
        HsmsTransportSessionId sessionId,
        Exception attemptedReason,
        Exception? previousReason)
    {
        lock (_sessionGate)
        {
            if (_sessions.TryGetValue(sessionId.Value, out var context) &&
                ReferenceEquals(context.CloseReason, attemptedReason))
            {
                context.CloseReason = previousReason;
            }
        }
    }

    private void RemoveSessionIfComplete(long sessionId, SessionContext context)
    {
        if (context.IsClosed && context.PendingSends == 0)
            _sessions.Remove(sessionId);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(StreamFrameHsmsTransport));
    }

    private sealed class SessionContext
    {
        public int PendingSends { get; set; }

        public bool IsClosed { get; set; }

        public Exception? CloseReason { get; set; }
    }
}
