using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using StreamFrame;

namespace SecsFrame.Tests;

public sealed class StreamFrameHsmsTransportTests
{
    private static readonly TimeSpan T5 = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan T8 = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Sessions_use_StreamFrame_monotonic_identifiers()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);

        connection.RaiseState(ConnectionState.Connected);
        var opened1 = await NextAsync(events);
        connection.RaiseState(ConnectionState.Retry);
        var closed1 = await NextAsync(events);
        connection.RaiseState(ConnectionState.Connecting);
        connection.RaiseState(ConnectionState.Connected);
        var opened2 = await NextAsync(events);

        Assert.Equal(HsmsTransportEventKind.SessionOpened, opened1.Kind);
        Assert.Equal(HsmsTransportEventKind.SessionClosed, closed1.Kind);
        Assert.Equal(opened1.SessionId, closed1.SessionId);
        Assert.Equal(HsmsTransportEventKind.SessionOpened, opened2.Kind);
        Assert.True(opened2.SessionId.Value > opened1.SessionId.Value);
        Assert.Equal(connection.CurrentSessionId, opened2.SessionId.Value);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Late_received_frame_keeps_its_native_session_identifier()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var originalSession = (await NextAsync(events)).SessionId;
        var frame = CreateFrame();

        connection.Emit(originalSession.Value, frame);
        connection.RaiseState(ConnectionState.Retry);
        connection.RaiseState(ConnectionState.Connected);

        HsmsTransportEvent received = default;
        for (var index = 0; index < 3; index++)
        {
            var transportEvent = await NextAsync(events);
            if (transportEvent.Kind == HsmsTransportEventKind.FrameReceived)
                received = transportEvent;
        }

        Assert.Equal(HsmsTransportEventKind.FrameReceived, received.Kind);
        Assert.Equal(originalSession, received.SessionId);
        Assert.Same(frame, received.Frame);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Send_completes_only_after_native_socket_write_confirmation()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var sessionId = (await NextAsync(events)).SessionId;
        var frame = CreateFrame(new byte[] { 0x21, 0x01, 0xFF });

        var send = transport.SendAsync(sessionId, frame, cancellation.Token).AsTask();

        Assert.Equal(1, connection.SentCount);
        Assert.Equal(sessionId.Value, connection.GetSentSessionId(0));
        Assert.Equal(0, connection.OrdinarySendCount);
        Assert.False(send.IsCompleted);
        connection.CompleteSend(0);
        await send;
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Concurrent_sends_delegate_to_native_session_queue()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var sessionId = (await NextAsync(events)).SessionId;
        var frame = CreateFrame();

        var first = transport.SendAsync(sessionId, frame, cancellation.Token).AsTask();
        var second = transport.SendAsync(sessionId, frame, cancellation.Token).AsTask();

        Assert.Equal(2, connection.SentCount);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        connection.CompleteSend(0);
        await first;
        Assert.False(second.IsCompleted);
        connection.CompleteSend(1);
        await second;
        Assert.Equal(0, connection.OrdinarySendCount);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Closing_session_fails_pending_send_and_prevents_reuse()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var sessionId = (await NextAsync(events)).SessionId;

        var pending = transport.SendAsync(sessionId, CreateFrame(), cancellation.Token).AsTask();
        connection.RaiseState(ConnectionState.Retry);

        var exception = await Assert.ThrowsAsync<HsmsTransportSessionExpiredException>(
            async () => await pending.ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(sessionId, exception.SessionId);
        Assert.IsType<SessionExpiredException>(exception.InnerException);
        await Assert.ThrowsAsync<HsmsTransportSessionExpiredException>(
            async () => await transport.SendAsync(
                sessionId,
                CreateFrame(),
                cancellation.Token).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(1, connection.SentCount);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Native_incomplete_frame_timeout_reports_HSMS_T8_reason()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var sessionId = (await NextAsync(events)).SessionId;
        var pendingSend = transport.SendAsync(
            sessionId,
            CreateFrame(),
            cancellation.Token).AsTask();

        connection.RaiseFrameError(FrameErrorKind.IncompleteFrameTimeout);
        Assert.Equal(0, connection.ReconnectCount);
        connection.Reconnect();
        var closed = await NextAsync(events);
        var sendException = await Assert.ThrowsAsync<HsmsT8TimeoutException>(
            async () => await pendingSend.ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(1, connection.ReconnectCount);
        Assert.Equal(HsmsTransportEventKind.SessionClosed, closed.Kind);
        Assert.Equal(sessionId, closed.SessionId);
        var timeout = Assert.IsType<HsmsT8TimeoutException>(closed.Error);
        Assert.Equal(sessionId, timeout.SessionId);
        Assert.Same(closed.Error, sendException);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Other_native_frame_errors_do_not_become_T8_close_reasons()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var opened = await NextAsync(events);

        connection.RaiseFrameError(FrameErrorKind.DecodeFailed);
        connection.RaiseState(ConnectionState.Retry);
        var closed = await NextAsync(events);

        Assert.Equal(opened.SessionId, closed.SessionId);
        Assert.Null(closed.Error);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Explicit_close_reconnects_current_session_and_preserves_reason()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var opened = await NextAsync(events);
        var pendingSend = transport.SendAsync(
            opened.SessionId,
            CreateFrame(),
            cancellation.Token).AsTask();
        var reason = new HsmsProtocolException("Invalid control message.");

        var closedCurrent = transport.TryCloseSession(opened.SessionId, reason);
        var closed = await NextAsync(events);
        var sendException = await Assert.ThrowsAsync<HsmsProtocolException>(
            async () => await pendingSend.ConfigureAwait(true)).ConfigureAwait(true);

        Assert.True(closedCurrent);
        Assert.Equal(1, connection.ReconnectCount);
        Assert.Equal(HsmsTransportEventKind.SessionClosed, closed.Kind);
        Assert.Equal(opened.SessionId, closed.SessionId);
        Assert.Same(reason, closed.Error);
        Assert.Same(reason, sendException);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Explicit_close_for_expired_session_does_not_reconnect_current_session()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var expiredSessionId = (await NextAsync(events)).SessionId;
        connection.RaiseState(ConnectionState.Retry);
        await NextAsync(events);
        connection.RaiseState(ConnectionState.Connecting);
        connection.RaiseState(ConnectionState.Connected);
        var currentSessionId = (await NextAsync(events)).SessionId;

        var closedExpired = transport.TryCloseSession(
            expiredSessionId,
            new HsmsProtocolException("Late close request."));

        Assert.False(closedExpired);
        Assert.Equal(0, connection.ReconnectCount);
        var currentSend = transport.SendAsync(
            currentSessionId,
            CreateFrame(),
            cancellation.Token).AsTask();
        connection.CompleteSend(0);
        await currentSend.ConfigureAwait(true);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Real_streamframe_connection_sends_and_receives_fragmented_frame()
    {
        var port = GetFreePort();
        await using var transport = StreamFrameHsmsTransport.Create(
            IPAddress.Loopback,
            port,
            isActive: false,
            new HsmsTransportOptions(
                T5,
                TimeSpan.FromMilliseconds(1000)),
            new StreamConnectionOptions
            {
                AcceptRetryDelayMs = 10,
                ConnectRetryDelayMs = 10,
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var opened = await NextAsync(events);
        var frame = CreateFrame();

        await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellation.Token);

        var send = transport.SendAsync(opened.SessionId, frame, cancellation.Token).AsTask();
        var sentBytes = await ReadExactlyAsync(
            client.GetStream(),
            HsmsFramer.LengthPrefixSize + HsmsMessageHeader.EncodedSize,
            cancellation.Token);
        await send;

        Assert.Equal(
            new byte[]
            {
                0x00, 0x00, 0x00, 0x0A,
                0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
            },
            sentBytes);

        var stream = client.GetStream();
        await WriteFragmentedWithProgressAsync(
            stream,
            sentBytes,
            cancellation.Token);
        var received = await NextAsync(events);

        Assert.Equal(HsmsTransportEventKind.FrameReceived, received.Kind);
        Assert.Equal(opened.SessionId, received.SessionId);
        Assert.Equal(frame.Header, received.Frame!.Header);

        await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellation.Token);
        await stream.WriteAsync(
            sentBytes,
            0,
            sentBytes.Length,
            cancellation.Token);
        var receivedAfterIdle = await NextAsync(events);
        Assert.Equal(HsmsTransportEventKind.FrameReceived, receivedAfterIdle.Kind);
        Assert.Equal(opened.SessionId, receivedAfterIdle.SessionId);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Real_streamframe_partial_frame_expires_T8_and_closes_session()
    {
        var port = GetFreePort();
        await using var transport = StreamFrameHsmsTransport.Create(
            IPAddress.Loopback,
            port,
            isActive: false,
            new HsmsTransportOptions(
                T5,
                TimeSpan.FromMilliseconds(250)),
            new StreamConnectionOptions
            {
                AcceptRetryDelayMs = 10,
                ConnectRetryDelayMs = 10,
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var opened = await NextAsync(events);

        await client.GetStream().WriteAsync(
            new byte[] { 0x00 },
            0,
            1,
            cancellation.Token);
        var closed = await NextAsync(events);

        Assert.Equal(HsmsTransportEventKind.SessionClosed, closed.Kind);
        Assert.Equal(opened.SessionId, closed.SessionId);
        var timeout = Assert.IsType<HsmsT8TimeoutException>(closed.Error);
        Assert.Equal(opened.SessionId, timeout.SessionId);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    private static StreamFrameHsmsTransport CreateTransport(
        FakeStreamConnection connection)
        => new(connection);

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<byte[]> ReadExactlyAsync(
        Stream stream,
        int count,
        CancellationToken cancellationToken)
    {
        var bytes = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(
                bytes,
                offset,
                count - offset,
                cancellationToken).ConfigureAwait(true);
            if (read == 0)
                throw new EndOfStreamException();

            offset += read;
        }

        return bytes;
    }

    private static async Task WriteFragmentedWithProgressAsync(
        Stream stream,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, 0, 2, cancellationToken).ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken).ConfigureAwait(true);
        await stream.WriteAsync(bytes, 2, 3, cancellationToken).ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken).ConfigureAwait(true);
        await stream.WriteAsync(
            bytes,
            5,
            bytes.Length - 5,
            cancellationToken).ConfigureAwait(true);
    }

    private static HsmsFrame CreateFrame(ReadOnlyMemory<byte> body = default)
        => new(
            HsmsMessageHeader.CreateData(1, 1, 1, false, 1),
            body);

    private static async Task<HsmsTransportEvent> NextAsync(
        IAsyncEnumerator<HsmsTransportEvent> events)
    {
        Assert.True(await events.MoveNextAsync().ConfigureAwait(false));
        return events.Current;
    }

    private sealed class FakeStreamConnection : ISessionAwareStreamConnection<HsmsFrame>
    {
        private readonly Channel<SessionMessage<HsmsFrame>> _messages =
            Channel.CreateUnbounded<SessionMessage<HsmsFrame>>();
#if NET9_0_OR_GREATER
        private readonly Lock _gate = new();
#else
        private readonly object _gate = new();
#endif
        private readonly List<PendingSessionSend> _sent = new();
        private long _sessionCounter;

        public ConnectionState State { get; private set; } = ConnectionState.Connecting;

        public bool IsActive => false;

        public IPAddress IpAddress => IPAddress.Loopback;

        public int Port => 5000;

        public string? RemoteIpAddress => IPAddress.Loopback.ToString();

        public long CurrentSessionId { get; private set; }

        public int ReconnectCount { get; private set; }

        public int OrdinarySendCount { get; private set; }

        public int SentCount
        {
            get
            {
                lock (_gate)
                    return _sent.Count;
            }
        }

        public event EventHandler<ConnectionState>? ConnectionChanged;

        public event EventHandler<FrameErrorEventArgs>? FrameError;

        public Action<ReadOnlyMemory<byte>>? RawBytesReceived { get; set; }

        public Action<ReadOnlyMemory<byte>>? RawBytesSent { get; set; }

        public void Start(CancellationToken ct)
        {
        }

        public void Reconnect()
        {
            ReconnectCount++;
            RaiseState(ConnectionState.Retry);
        }

        public Task WaitForConnectedAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendAsync(HsmsFrame message, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            OrdinarySendCount++;
            return Task.CompletedTask;
        }

        public Task SendInSessionAsync(
            long sessionId,
            HsmsFrame message,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return Task.FromCanceled(ct);

            lock (_gate)
            {
                if (CurrentSessionId != sessionId)
                {
                    return Task.FromException(
                        new SessionExpiredException(
                            sessionId,
                            $"Session {sessionId} expired."));
                }

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _sent.Add(new PendingSessionSend(sessionId, message, completion));
                return completion.Task;
            }
        }

        public async IAsyncEnumerable<HsmsFrame> GetMessages(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            while (await _messages.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_messages.Reader.TryRead(out var message))
                    yield return message.Message;
            }
        }

        public async IAsyncEnumerable<SessionMessage<HsmsFrame>> GetSessionMessages(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            while (await _messages.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_messages.Reader.TryRead(out var message))
                    yield return message;
            }
        }

        public ValueTask DisposeAsync()
        {
            RaiseState(ConnectionState.Disconnected);
            _messages.Writer.TryComplete();
            return default;
        }

        public void RaiseState(ConnectionState state)
        {
            long expiredSessionId = 0;
            lock (_gate)
            {
                if (state == ConnectionState.Connected &&
                    State != ConnectionState.Connected)
                {
                    CurrentSessionId = checked(++_sessionCounter);
                }
                else if (state != ConnectionState.Connected &&
                    State == ConnectionState.Connected)
                {
                    expiredSessionId = CurrentSessionId;
                    CurrentSessionId = 0;
                }

                State = state;
                if (expiredSessionId > 0)
                {
                    foreach (var pending in _sent)
                    {
                        if (pending.SessionId == expiredSessionId)
                        {
                            pending.Completion.TrySetException(
                                new SessionExpiredException(
                                    expiredSessionId,
                                    $"Session {expiredSessionId} expired."));
                        }
                    }
                }
            }

            ConnectionChanged?.Invoke(this, state);
        }

        public void RaiseFrameError(FrameErrorKind kind)
            => FrameError?.Invoke(
                this,
                new FrameErrorEventArgs(kind, ReadOnlyMemory<byte>.Empty));

        public void Emit(long sessionId, HsmsFrame frame)
            => _messages.Writer.TryWrite(new SessionMessage<HsmsFrame>(sessionId, frame));

        public long GetSentSessionId(int index)
        {
            lock (_gate)
                return _sent[index].SessionId;
        }

        public void CompleteSend(int index)
        {
            lock (_gate)
                _sent[index].Completion.TrySetResult(true);
        }

        private sealed record PendingSessionSend(
            long SessionId,
            HsmsFrame Message,
            TaskCompletionSource<bool> Completion);
    }
}
