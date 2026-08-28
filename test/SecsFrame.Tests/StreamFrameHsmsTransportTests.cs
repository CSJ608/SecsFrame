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
    public async Task Sessions_open_and_close_with_monotonic_identifiers()
    {
        var connection = new FakeStreamConnection();
        var timerFactory = new ManualHsmsTransportTimerFactory();
        await using var transport = CreateTransport(connection, timerFactory);
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
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Late_received_frame_keeps_its_original_session_identifier()
    {
        var connection = new FakeStreamConnection();
        var timerFactory = new ManualHsmsTransportTimerFactory();
        await using var transport = CreateTransport(connection, timerFactory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var originalSession = (await NextAsync(events)).SessionId;
        var frame = CreateFrame();

        connection.Emit(new HsmsTransportFrame(originalSession, frame));
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
    public async Task Send_completes_only_after_the_entire_wire_frame_is_reported()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection, new ManualHsmsTransportTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var sessionId = (await NextAsync(events)).SessionId;
        var frame = CreateFrame(new byte[] { 0x21, 0x01, 0xFF });

        var send = transport.SendAsync(sessionId, frame, cancellation.Token).AsTask();

        Assert.Single(connection.Sent);
        Assert.False(send.IsCompleted);
        connection.ReportSent(new byte[16]);
        Assert.False(send.IsCompleted);
        connection.ReportSent(new byte[1]);
        await send;
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Concurrent_sends_are_serialized_until_each_write_confirmation()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection, new ManualHsmsTransportTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var sessionId = (await NextAsync(events)).SessionId;
        var frame = CreateFrame();

        var first = transport.SendAsync(sessionId, frame, cancellation.Token).AsTask();
        var second = transport.SendAsync(sessionId, frame, cancellation.Token).AsTask();

        Assert.Single(connection.Sent);
        connection.ReportSent(new byte[14]);
        await first;
        await WaitUntilAsync(() => connection.Sent.Count == 2);
        Assert.False(second.IsCompleted);
        connection.ReportSent(new byte[14]);
        await second;
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Closing_session_fails_pending_send_and_prevents_reuse()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(connection, new ManualHsmsTransportTimerFactory());
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
        await Assert.ThrowsAsync<HsmsTransportSessionExpiredException>(
            async () => await transport.SendAsync(
                sessionId,
                CreateFrame(),
                cancellation.Token).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Single(connection.Sent);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task T8_timeout_reconnects_and_reports_close_reason()
    {
        var connection = new FakeStreamConnection();
        var timerFactory = new ManualHsmsTransportTimerFactory();
        await using var transport = CreateTransport(connection, timerFactory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var sessionId = (await NextAsync(events)).SessionId;
        var pendingSend = transport.SendAsync(
            sessionId,
            CreateFrame(),
            cancellation.Token).AsTask();

        connection.ReportReceived(new byte[] { 0x00, 0x00 });
        timerFactory.Timer!.Fire();
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
    public async Task Queued_T8_callback_from_previous_session_does_not_close_current_session()
    {
        var connection = new FakeStreamConnection();
        var timerFactory = new ManualHsmsTransportTimerFactory();
        await using var transport = CreateTransport(connection, timerFactory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        await NextAsync(events);

        connection.ReportReceived(new byte[] { 0x00 });
        var previousSessionTimer = timerFactory.Timer!;
        connection.RaiseState(ConnectionState.Retry);
        await NextAsync(events);
        connection.RaiseState(ConnectionState.Connecting);
        connection.RaiseState(ConnectionState.Connected);
        var currentSessionId = (await NextAsync(events)).SessionId;
        connection.ReportReceived(new byte[] { 0x00, 0x00 });
        var currentSessionTimer = timerFactory.Timer!;

        previousSessionTimer.ForceFire();
        Assert.Equal(0, connection.ReconnectCount);
        Assert.True(currentSessionTimer.IsArmed);

        currentSessionTimer.Fire();
        var closed = await NextAsync(events);
        Assert.Equal(1, connection.ReconnectCount);
        Assert.Equal(currentSessionId, closed.SessionId);
        var timeout = Assert.IsType<HsmsT8TimeoutException>(closed.Error);
        Assert.Equal(currentSessionId, timeout.SessionId);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Explicit_close_reconnects_current_session_and_reports_reason()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(
            connection,
            new ManualHsmsTransportTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var opened = await NextAsync(events);
        var reason = new HsmsProtocolException("Invalid control message.");

        var closedCurrent = transport.TryCloseSession(opened.SessionId, reason);
        var closed = await NextAsync(events);

        Assert.True(closedCurrent);
        Assert.Equal(1, connection.ReconnectCount);
        Assert.Equal(HsmsTransportEventKind.SessionClosed, closed.Kind);
        Assert.Equal(opened.SessionId, closed.SessionId);
        Assert.Same(reason, closed.Error);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Explicit_close_for_expired_session_does_not_reconnect_current_session()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(
            connection,
            new ManualHsmsTransportTimerFactory());
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
        connection.ReportSent(new byte[14]);
        await currentSend.ConfigureAwait(true);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Idle_or_complete_frames_do_not_arm_T8_or_reconnect()
    {
        var connection = new FakeStreamConnection();
        var timerFactory = new ManualHsmsTransportTimerFactory();
        await using var transport = CreateTransport(connection, timerFactory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        await NextAsync(events);

        Assert.Empty(timerFactory.Timers);
        connection.ReportReceived(
            new byte[]
            {
                0x00, 0x00, 0x00, 0x0A,
                0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
            });

        Assert.Empty(timerFactory.Timers);
        Assert.Equal(0, connection.ReconnectCount);
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
            new HsmsTransportOptions(T5, T8),
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
        var frame = CreateFrame();

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
        await stream.WriteAsync(
            sentBytes,
            0,
            2,
            cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellation.Token);
        await stream.WriteAsync(
            sentBytes,
            2,
            3,
            cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellation.Token);
        await stream.WriteAsync(
            sentBytes,
            5,
            sentBytes.Length - 5,
            cancellation.Token);
        var received = await NextAsync(events);

        Assert.Equal(HsmsTransportEventKind.FrameReceived, received.Kind);
        Assert.Equal(opened.SessionId, received.SessionId);
        Assert.Equal(frame.Header, received.Frame!.Header);
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
        FakeStreamConnection connection,
        ManualHsmsTransportTimerFactory timerFactory)
        => new(
            connection,
            new HsmsTransportSessionContext(),
            new HsmsTransportOptions(T5, T8),
            timerFactory);

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

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var index = 0; index < 100; index++)
        {
            if (predicate())
                return;

            await Task.Yield();
        }

        Assert.True(predicate());
    }

    private sealed class FakeStreamConnection : IStreamConnection<HsmsTransportFrame>
    {
        private readonly Channel<HsmsTransportFrame> _messages =
            Channel.CreateUnbounded<HsmsTransportFrame>();

        public ConnectionState State { get; private set; } = ConnectionState.Connecting;

        public bool IsActive => false;

        public IPAddress IpAddress => IPAddress.Loopback;

        public int Port => 5000;

        public string? RemoteIpAddress => IPAddress.Loopback.ToString();

        public event EventHandler<ConnectionState>? ConnectionChanged;

        public event EventHandler<FrameErrorEventArgs>? FrameError
        {
            add { }
            remove { }
        }

        public Action<ReadOnlyMemory<byte>>? RawBytesReceived { get; set; }

        public Action<ReadOnlyMemory<byte>>? RawBytesSent { get; set; }

        public List<HsmsTransportFrame> Sent { get; } = new();

        public int ReconnectCount { get; private set; }

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

        public Task SendAsync(HsmsTransportFrame message, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<HsmsTransportFrame> GetMessages(
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
            State = state;
            ConnectionChanged?.Invoke(this, state);
        }

        public void Emit(HsmsTransportFrame frame)
            => _messages.Writer.TryWrite(frame);

        public void ReportReceived(ReadOnlyMemory<byte> bytes)
            => RawBytesReceived?.Invoke(bytes);

        public void ReportSent(ReadOnlyMemory<byte> bytes)
            => RawBytesSent?.Invoke(bytes);
    }
}
