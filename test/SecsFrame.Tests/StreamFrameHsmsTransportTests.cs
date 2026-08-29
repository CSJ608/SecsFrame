using System.Buffers;
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

    [Theory]
    [InlineData(
        FrameErrorKind.DecodeFailed,
        HsmsTransportFaultKind.DecodeFailed)]
    [InlineData(
        FrameErrorKind.DiscardedByResync,
        HsmsTransportFaultKind.DiscardedByResync)]
    [InlineData(
        FrameErrorKind.IncompleteFrameOverflow,
        HsmsTransportFaultKind.IncompleteFrameOverflow)]
    [InlineData(
        FrameErrorKind.IncompleteFrameTimeout,
        HsmsTransportFaultKind.IncompleteFrameTimeout)]
    public async Task Enabled_native_transport_fault_observation_preserves_metadata(
        FrameErrorKind nativeKind,
        HsmsTransportFaultKind expectedKind)
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(
            connection,
            enableTransportFaultObservation: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var opened = await NextAsync(events).ConfigureAwait(true);
        var snapshot = new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x00 };

        connection.RaiseFrameError(
            nativeKind,
            snapshot,
            observedByteCount: 9);
        snapshot[0] = 0xFF;
        var fault = await NextAsync(events).ConfigureAwait(true);

        Assert.Equal(HsmsTransportEventKind.TransportFaultObserved, fault.Kind);
        Assert.Equal(opened.SessionId, fault.SessionId);
        Assert.Equal(expectedKind, fault.FaultKind);
        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x00 },
            fault.Snapshot.ToArray());
        Assert.Equal(9, fault.ObservedByteCount);
        Assert.True(fault.IsTruncated);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Late_native_transport_fault_keeps_its_original_session()
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(
            connection,
            enableTransportFaultObservation: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var originalSession = (await NextAsync(events)).SessionId;
        connection.RaiseState(ConnectionState.Retry);
        await NextAsync(events);
        connection.RaiseState(ConnectionState.Connecting);
        connection.RaiseState(ConnectionState.Connected);
        var currentSession = (await NextAsync(events)).SessionId;

        connection.RaiseFrameError(
            FrameErrorKind.DiscardedByResync,
            new byte[] { 0xAA },
            sessionId: originalSession.Value);
        var fault = await NextAsync(events);

        Assert.True(currentSession.Value > originalSession.Value);
        Assert.Equal(HsmsTransportEventKind.TransportFaultObserved, fault.Kind);
        Assert.Equal(originalSession, fault.SessionId);
        Assert.Equal(HsmsTransportFaultKind.DiscardedByResync, fault.FaultKind);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Theory]
    [InlineData(8191, 8191, 8191, false)]
    [InlineData(8192, 8192, 8192, false)]
    [InlineData(8193, 8193, 8192, true)]
    [InlineData(8192, 9004, 8192, true)]
    public async Task Native_transport_fault_snapshot_is_bounded(
        int nativeSnapshotLength,
        long observedByteCount,
        int retainedSnapshotLength,
        bool isTruncated)
    {
        var connection = new FakeStreamConnection();
        await using var transport = CreateTransport(
            connection,
            enableTransportFaultObservation: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        await NextAsync(events);

        connection.RaiseFrameError(
            FrameErrorKind.DiscardedByResync,
            new byte[nativeSnapshotLength],
            observedByteCount: observedByteCount);
        var fault = await NextAsync(events);

        Assert.Equal(retainedSnapshotLength, fault.Snapshot.Length);
        Assert.Equal(observedByteCount, fault.ObservedByteCount);
        Assert.Equal(isTruncated, fault.IsTruncated);
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
    public async Task Dispose_waits_for_inflight_session_close()
    {
        var connection = new FakeStreamConnection { BlockReconnect = true };
        var transport = CreateTransport(connection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        connection.RaiseState(ConnectionState.Connected);
        var opened = await NextAsync(events).ConfigureAwait(true);

        var close = Task.Factory.StartNew(
            () => transport.TryCloseSession(opened.SessionId),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var started = await Task.WhenAny(
            connection.ReconnectStarted,
            Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token)).ConfigureAwait(true);
        if (!ReferenceEquals(connection.ReconnectStarted, started))
        {
            connection.ReleaseReconnect();
            Assert.Same(connection.ReconnectStarted, started);
        }

        var dispose = Task.Factory.StartNew(
            () => transport.DisposeAsync().AsTask(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellation.Token).ConfigureAwait(true);
        var disposeWaitedForClose = !dispose.IsCompleted;
        connection.ReleaseReconnect();

        Assert.True(await close.ConfigureAwait(true));
        await dispose.ConfigureAwait(true);
        Assert.True(disposeWaitedForClose);
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

    [Fact]
    public async Task Real_streamframe_replacement_expires_queued_send_without_replay()
    {
        var port = GetFreePort();
        var codec = new ControlledEncodeCodec();
        await using var transport = CreateControlledTransport(port, codec);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);

        using var firstClient = new System.Net.Sockets.TcpClient();
        await firstClient.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(true);
        var firstOpened = await NextAsync(events).ConfigureAwait(true);
        await SendAndReadAsync(
            transport,
            firstOpened.SessionId,
            firstClient,
            0x01,
            cancellation.Token).ConfigureAwait(true);
        codec.BlockNextEncode();
        var inFlight = transport.SendAsync(
            firstOpened.SessionId,
            CreateFrame(new byte[] { 0x11 }),
            cancellation.Token).AsTask();
        var encodeStarted = await Task.WhenAny(
            codec.BlockedEncodeStarted,
            Task.Delay(TimeSpan.FromSeconds(5), cancellation.Token)).ConfigureAwait(true);
        Assert.Same(codec.BlockedEncodeStarted, encodeStarted);

        var queued = transport.SendAsync(
            firstOpened.SessionId,
            CreateFrame(new byte[] { 0x12 }),
            cancellation.Token).AsTask();

        Assert.True(transport.TryCloseSession(firstOpened.SessionId));
        var firstClosed = await NextAsync(events).ConfigureAwait(true);
        var queuedException = await Assert.ThrowsAsync<HsmsTransportSessionExpiredException>(
            async () => await queued.ConfigureAwait(true)).ConfigureAwait(true);
        Assert.IsType<SessionExpiredException>(queuedException.InnerException);
        Assert.NotNull(await Record.ExceptionAsync(
            async () => await inFlight.ConfigureAwait(true)).ConfigureAwait(true));
        Assert.Equal(HsmsTransportEventKind.SessionClosed, firstClosed.Kind);
        Assert.Equal(firstOpened.SessionId, firstClosed.SessionId);

        firstClient.Dispose();
        using var secondClient = new System.Net.Sockets.TcpClient();
        await secondClient.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(true);
        var secondOpened = await NextAsync(events).ConfigureAwait(true);
        Assert.True(secondOpened.SessionId.Value > firstOpened.SessionId.Value);

        var currentBytes = await SendAndReadAsync(
            transport,
            secondOpened.SessionId,
            secondClient,
            0x22,
            cancellation.Token).ConfigureAwait(true);

        Assert.Equal(0x22, currentBytes[currentBytes.Length - 1]);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Real_passive_reconnect_race_keeps_listener_available()
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
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = transport.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        transport.Start(cancellation.Token);
        var previousSessionId = new HsmsTransportSessionId(0);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var client = await ConnectWithRetryAsync(
                port,
                cancellation.Token).ConfigureAwait(true);
            var opened = await NextAsync(events).ConfigureAwait(true);
            Assert.Equal(HsmsTransportEventKind.SessionOpened, opened.Kind);
            Assert.True(opened.SessionId.Value > previousSessionId.Value);

            var explicitClose = Task.Run(
                () => transport.TryCloseSession(opened.SessionId));
            client.Dispose();
            _ = await explicitClose.ConfigureAwait(true);
            var closed = await NextAsync(events).ConfigureAwait(true);
            Assert.Equal(HsmsTransportEventKind.SessionClosed, closed.Kind);
            Assert.Equal(opened.SessionId, closed.SessionId);
            previousSessionId = opened.SessionId;
        }

        await events.DisposeAsync().ConfigureAwait(true);
    }

    private static StreamFrameHsmsTransport CreateTransport(
        FakeStreamConnection connection,
        bool enableTransportFaultObservation = false)
        => new(connection, enableTransportFaultObservation);

    private static StreamFrameHsmsTransport CreateControlledTransport(
        int port,
        ICodec<HsmsFrame> codec)
        => new(
            new StreamConnection<HsmsFrame>(
                new HsmsFramer(),
                codec,
                IPAddress.Loopback,
                port,
                isActive: false,
                new StreamConnectionOptions
                {
                    AcceptRetryDelayMs = 10,
                    ConnectRetryDelayMs = 10,
                }));

    private static async Task<byte[]> SendAndReadAsync(
        StreamFrameHsmsTransport transport,
        HsmsTransportSessionId sessionId,
        System.Net.Sockets.TcpClient client,
        byte body,
        CancellationToken cancellationToken)
    {
        var send = transport.SendAsync(
            sessionId,
            CreateFrame(new[] { body }),
            cancellationToken).AsTask();
        var bytes = await ReadExactlyAsync(
            client.GetStream(),
            HsmsFramer.LengthPrefixSize + HsmsMessageHeader.EncodedSize + 1,
            cancellationToken).ConfigureAwait(true);
        await send.ConfigureAwait(true);
        return bytes;
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<System.Net.Sockets.TcpClient> ConnectWithRetryAsync(
        int port,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var client = new System.Net.Sockets.TcpClient();
            try
            {
                var connect = client.ConnectAsync(IPAddress.Loopback, port);
                var completed = await Task.WhenAny(
                    connect,
                    Task.Delay(
                        TimeSpan.FromSeconds(2),
                        cancellationToken)).ConfigureAwait(false);
                if (!ReferenceEquals(connect, completed))
                {
                    client.Dispose();
                    throw new TimeoutException(
                        "The passive listener accepted no connection.");
                }

                await connect.ConfigureAwait(false);
                return client;
            }
            catch (System.Net.Sockets.SocketException)
            {
                client.Dispose();
                await Task.Delay(
                    TimeSpan.FromMilliseconds(10),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("The passive listener did not become available.");
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

    private sealed class ControlledEncodeCodec : ICodec<HsmsFrame>
    {
        private readonly HsmsFrameCodec _inner = new();
        private readonly TaskCompletionSource<bool> _blockedEncodeStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blockNextEncode;

        public Task BlockedEncodeStarted => _blockedEncodeStarted.Task;

        public void BlockNextEncode()
            => Interlocked.Exchange(ref _blockNextEncode, 1);

        public HsmsFrame Decode(
            in ReadOnlySequence<byte> frame,
            CancellationToken ct = default)
            => _inner.Decode(in frame, ct);

        public void Encode(
            HsmsFrame message,
            IBufferWriter<byte> writer,
            CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _blockNextEncode, 0) != 0)
            {
                _blockedEncodeStarted.TrySetResult(true);
                ct.WaitHandle.WaitOne();
                ct.ThrowIfCancellationRequested();
            }

            _inner.Encode(message, writer, ct);
        }
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
        private readonly TaskCompletionSource<bool> _continueReconnect = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _reconnectStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private long _sessionCounter;
        private int _disposed;

        public ConnectionState State { get; private set; } = ConnectionState.Connecting;

        public bool IsActive => false;

        public IPAddress IpAddress => IPAddress.Loopback;

        public int Port => 5000;

        public string? RemoteIpAddress => IPAddress.Loopback.ToString();

        public long CurrentSessionId { get; private set; }

        public int ReconnectCount { get; private set; }

        public int OrdinarySendCount { get; private set; }

        public bool BlockReconnect { get; init; }

        public Task ReconnectStarted => _reconnectStarted.Task;

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
            if (BlockReconnect)
            {
                _reconnectStarted.TrySetResult(true);
                _continueReconnect.Task.GetAwaiter().GetResult();
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new InvalidOperationException(
                        "The connection was disposed during Reconnect.");
                }
            }

            RaiseState(ConnectionState.Retry);
        }

        public void ReleaseReconnect()
            => _continueReconnect.TrySetResult(true);

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
            Interlocked.Exchange(ref _disposed, 1);
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

        public void RaiseFrameError(
            FrameErrorKind kind,
            ReadOnlyMemory<byte> bytes = default,
            long? sessionId = null,
            long? observedByteCount = null)
            => FrameError?.Invoke(
                this,
                new FrameErrorEventArgs(
                    kind,
                    bytes,
                    null,
                    sessionId ?? CurrentSessionId,
                    observedByteCount ?? bytes.Length));

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
