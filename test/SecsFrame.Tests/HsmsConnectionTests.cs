using System.Net;

namespace SecsFrame.Tests;

public sealed class HsmsConnectionTests
{
    [Fact]
    public async Task Public_connections_exchange_dynamic_messages_over_real_tcp()
    {
        var port = GetFreePort();
        await using var passive = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Passive));
        await using var active = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Active));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(15));

        passive.Start();
        active.Start();
        await using var passiveEvents = passive
            .GetEventsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        await Task.WhenAll(
            passive.WaitUntilSelectedAsync(cancellation.Token),
            active.WaitUntilSelectedAsync(cancellation.Token))
            .ConfigureAwait(true);

        await AssertSelectedEventAsync(
            passiveEvents,
            passive,
            active).ConfigureAwait(true);
        await active.LinktestAsync(cancellation.Token).ConfigureAwait(true);
        await AssertRoundTripAsync(
            passive,
            active,
            passiveEvents,
            cancellation.Token).ConfigureAwait(true);
    }

    [Fact]
    public async Task Enabled_control_observation_captures_select_and_linktest_over_real_tcp()
    {
        var port = GetFreePort();
        await using var passive = new HsmsConnection(
            CreateOptions(
                port,
                HsmsConnectionMode.Passive,
                enableControlMessageObservation: true));
        await using var active = new HsmsConnection(
            CreateOptions(
                port,
                HsmsConnectionMode.Active,
                enableControlMessageObservation: true));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(15));

        passive.Start();
        active.Start();
        await using var passiveObservations = passive
            .GetControlMessageObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        await using var activeObservations = active
            .GetControlMessageObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        await Task.WhenAll(
            passive.WaitUntilSelectedAsync(cancellation.Token),
            active.WaitUntilSelectedAsync(cancellation.Token))
            .ConfigureAwait(true);

        await AssertSelectObservationsAsync(
            passiveObservations,
            activeObservations).ConfigureAwait(true);

        await active.LinktestAsync(cancellation.Token).ConfigureAwait(true);
        await AssertLinktestObservationsAsync(
            passiveObservations,
            activeObservations).ConfigureAwait(true);
    }

    [Fact]
    public async Task Enabled_transport_fault_observation_captures_real_T8_prefix()
    {
        var port = GetFreePort();
        await using var connection = new HsmsConnection(
            CreateOptions(
                port,
                HsmsConnectionMode.Passive,
                enableTransportFaultObservation: true,
                t8: TimeSpan.FromMilliseconds(250)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        connection.Start();
        await using var observations = connection
            .GetTransportFaultObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(true);
        var prefix = new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x00, 0x01 };

        await client.GetStream().WriteAsync(
            prefix,
            0,
            prefix.Length,
            cancellation.Token).ConfigureAwait(true);
        Assert.True(await observations.MoveNextAsync().ConfigureAwait(true));
        var observation = observations.Current;

        Assert.Equal(
            HsmsTransportFaultKind.IncompleteFrameTimeout,
            observation.Kind);
        Assert.True(observation.TransportSessionId > 0);
        Assert.Equal(HsmsSessionState.Connected, observation.State);
        Assert.Equal(prefix, observation.Snapshot.ToArray());
    }

    private static async Task AssertSelectObservationsAsync(
        IAsyncEnumerator<HsmsControlMessageObservation> passiveObservations,
        IAsyncEnumerator<HsmsControlMessageObservation> activeObservations)
    {
        var active = await ReadControlExchangeAsync(
            activeObservations,
            HsmsControlMessageDirection.Sent,
            HsmsMessageType.SelectRequest,
            HsmsMessageType.SelectResponse).ConfigureAwait(true);
        var passive = await ReadControlExchangeAsync(
            passiveObservations,
            HsmsControlMessageDirection.Received,
            HsmsMessageType.SelectRequest,
            HsmsMessageType.SelectResponse).ConfigureAwait(true);

        Assert.True(
            active.Request.State is
                HsmsSessionState.Selecting or HsmsSessionState.Selected);
        Assert.Equal(HsmsSessionState.Connected, passive.Request.State);
        Assert.Equal(active.Request.Header, passive.Request.Header);
        Assert.Equal(HsmsSessionState.Connected, passive.Response.State);
        Assert.Equal(HsmsSessionState.Selecting, active.Response.State);
        Assert.Equal(passive.Response.Header, active.Response.Header);
    }

    private static async Task AssertLinktestObservationsAsync(
        IAsyncEnumerator<HsmsControlMessageObservation> passiveObservations,
        IAsyncEnumerator<HsmsControlMessageObservation> activeObservations)
    {
        var active = await ReadControlExchangeAsync(
            activeObservations,
            HsmsControlMessageDirection.Sent,
            HsmsMessageType.LinktestRequest,
            HsmsMessageType.LinktestResponse).ConfigureAwait(true);
        var passive = await ReadControlExchangeAsync(
            passiveObservations,
            HsmsControlMessageDirection.Received,
            HsmsMessageType.LinktestRequest,
            HsmsMessageType.LinktestResponse).ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Selected, active.Request.State);
        Assert.Equal(HsmsSessionState.Selected, passive.Request.State);
        Assert.Equal(active.Request.Header, passive.Request.Header);
        Assert.Equal(HsmsSessionState.Selected, passive.Response.State);
        Assert.Equal(HsmsSessionState.Selected, active.Response.State);
        Assert.Equal(passive.Response.Header, active.Response.Header);
    }

    [Fact]
    public async Task Canceling_open_transaction_does_not_close_public_connection()
    {
        var port = GetFreePort();
        await using var passive = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Passive));
        await using var active = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Active));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var sendCancellation = new CancellationTokenSource();

        passive.Start();
        active.Start();
        await using var passiveEvents = passive
            .GetEventsAsync(lifetime.Token)
            .GetAsyncEnumerator();
        await Task.WhenAll(
            passive.WaitUntilSelectedAsync(lifetime.Token),
            active.WaitUntilSelectedAsync(lifetime.Token))
            .ConfigureAwait(true);
        var send = active.SendAsync(
            new SecsMessage(1, 1, true),
            sendCancellation.Token);
        _ = await NextMatchingAsync(
            passiveEvents,
            item => item.Kind ==
                HsmsConnectionEventKind.DataMessageReceived)
            .ConfigureAwait(true);
        sendCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => send).ConfigureAwait(true);
        Assert.Equal(HsmsSessionState.Selected, passive.State);
        Assert.Equal(HsmsSessionState.Selected, active.State);
    }

    [Fact]
    public async Task Readiness_wait_can_be_canceled_without_stopping_connection()
    {
        var port = GetFreePort();
        await using var connection = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Active));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));
        connection.Start();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.WaitUntilSelectedAsync(cancellation.Token))
            .ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Disconnected, connection.State);
    }

    [Fact]
    public async Task Disposing_connection_ends_uncanceled_readiness_wait()
    {
        var port = GetFreePort();
        var connection = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Active));
        connection.Start();
        var selected = connection.WaitUntilSelectedAsync();

        await connection.DisposeAsync().ConfigureAwait(true);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => selected).ConfigureAwait(true);
    }

    [Fact]
    public async Task Canceled_event_read_allows_replacement_consumer()
    {
        var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        await using var owned = connection;
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));
        connection.Start();
        await using var first = connection
            .GetEventsAsync(cancellation.Token)
            .GetAsyncEnumerator();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => first.MoveNextAsync().AsTask()).ConfigureAwait(true);

        var replacement = connection.GetEventsAsync();
        await using var second = replacement
            .GetAsyncEnumerator()
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Concurrent_event_readers_are_rejected()
    {
        var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        await using var owned = connection;
        using var cancellation = new CancellationTokenSource();
        connection.Start();
        await using var first = connection
            .GetEventsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        var firstRead = first.MoveNextAsync().AsTask();
        await using var second = connection
            .GetEventsAsync()
            .GetAsyncEnumerator();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => second.MoveNextAsync().AsTask()).ConfigureAwait(true);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => firstRead).ConfigureAwait(true);
    }

    [Fact]
    public async Task Control_observation_is_disabled_by_default()
    {
        await using var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        connection.Start();

        Assert.Throws<InvalidOperationException>(
            () => connection.GetControlMessageObservationsAsync());
    }

    [Fact]
    public async Task Transport_fault_observation_is_disabled_by_default()
    {
        await using var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        connection.Start();

        Assert.Throws<InvalidOperationException>(
            () => connection.GetTransportFaultObservationsAsync());
    }

    [Fact]
    public async Task Control_observation_enforces_one_reader_and_allows_replacement()
    {
        await using var connection = new HsmsConnection(
            CreateOptions(
                GetFreePort(),
                HsmsConnectionMode.Active,
                enableControlMessageObservation: true));
        using var cancellation = new CancellationTokenSource();
        connection.Start();
        await using var first = connection
            .GetControlMessageObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        var firstRead = first.MoveNextAsync().AsTask();
        var concurrent = connection
            .GetControlMessageObservationsAsync()
            .GetAsyncEnumerator();
        await using var concurrentScope = concurrent.ConfigureAwait(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => concurrent.MoveNextAsync().AsTask()).ConfigureAwait(true);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => firstRead).ConfigureAwait(true);

        using var replacementCancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));
        var replacement = connection
            .GetControlMessageObservationsAsync(replacementCancellation.Token)
            .GetAsyncEnumerator();
        await using var replacementScope = replacement.ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => replacement.MoveNextAsync().AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task Transport_fault_observation_enforces_one_reader_and_allows_replacement()
    {
        await using var connection = new HsmsConnection(
            CreateOptions(
                GetFreePort(),
                HsmsConnectionMode.Active,
                enableTransportFaultObservation: true));
        using var cancellation = new CancellationTokenSource();
        connection.Start();
        await using var first = connection
            .GetTransportFaultObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        var firstRead = first.MoveNextAsync().AsTask();
        var concurrent = connection
            .GetTransportFaultObservationsAsync()
            .GetAsyncEnumerator();
        await using var concurrentScope = concurrent.ConfigureAwait(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => concurrent.MoveNextAsync().AsTask()).ConfigureAwait(true);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => firstRead).ConfigureAwait(true);

        using var replacementCancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));
        var replacement = connection
            .GetTransportFaultObservationsAsync(replacementCancellation.Token)
            .GetAsyncEnumerator();
        await using var replacementScope = replacement.ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => replacement.MoveNextAsync().AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task Connection_rejects_commands_before_start_and_duplicate_start()
    {
        var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        await using var owned = connection;

        Assert.Throws<InvalidOperationException>(
            () => connection.GetEventsAsync());
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = connection.WaitUntilSelectedAsync();
            });
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = connection.SendAsync(new SecsMessage(1, 1));
            });

        connection.Start();
        Assert.Throws<InvalidOperationException>(() => connection.Start());
    }

    private static HsmsConnectionOptions CreateOptions(
        int port,
        HsmsConnectionMode connectionMode,
        bool enableControlMessageObservation = false,
        bool enableTransportFaultObservation = false,
        TimeSpan? t8 = null)
        => new(
            IPAddress.Loopback,
            port,
            connectionMode,
            sessionId: 10,
            t3: TimeSpan.FromSeconds(5),
            t5: TimeSpan.FromMilliseconds(10),
            t6: TimeSpan.FromSeconds(5),
            t7: TimeSpan.FromSeconds(10),
            t8: t8 ?? TimeSpan.FromSeconds(5),
            enableControlMessageObservation: enableControlMessageObservation,
            enableTransportFaultObservation: enableTransportFaultObservation);

    private static async Task AssertSelectedEventAsync(
        IAsyncEnumerator<HsmsConnectionEvent> passiveEvents,
        HsmsConnection passive,
        HsmsConnection active)
    {
        var selected = await NextMatchingAsync(
            passiveEvents,
            item => item.Kind == HsmsConnectionEventKind.StateChanged &&
                item.State == HsmsSessionState.Selected).ConfigureAwait(true);

        Assert.Null(selected.IncomingMessage);
        Assert.Null(selected.Frame);
        Assert.Null(selected.Error);
        Assert.Null(selected.Diagnostic);
        Assert.Equal(HsmsSessionState.Selected, passive.State);
        Assert.Equal(HsmsSessionState.Selected, active.State);
    }

    private static async Task AssertRoundTripAsync(
        HsmsConnection passive,
        HsmsConnection active,
        IAsyncEnumerator<HsmsConnectionEvent> passiveEvents,
        CancellationToken cancellationToken)
    {
        var send = active.SendAsync(
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.List(
                    SecsItem.Ascii("LOT-42"),
                    SecsItem.List(SecsItem.U1(1), SecsItem.U1(2)))),
            cancellationToken);
        var received = await NextMatchingAsync(
            passiveEvents,
            item => item.Kind ==
                HsmsConnectionEventKind.DataMessageReceived).ConfigureAwait(true);
        var incoming = received.IncomingMessage!;

        Assert.True(incoming.ReplyExpected);
        Assert.Equal(10, incoming.DataMessage.SessionId);
        Assert.Equal(6, incoming.DataMessage.Message.Stream);
        Assert.Equal(11, incoming.DataMessage.Message.Function);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => active.ReplyAsync(
                incoming,
                new SecsMessage(6, 12),
                cancellationToken)).ConfigureAwait(true);
        await passive.ReplyAsync(
            incoming,
            new SecsMessage(
                6,
                12,
                rootItem: SecsItem.Boolean(true)),
            cancellationToken).ConfigureAwait(true);
        var secondary = await send.ConfigureAwait(true);

        Assert.NotNull(secondary);
        Assert.Equal(10, secondary.SessionId);
        Assert.Equal(6, secondary.Message.Stream);
        Assert.Equal(12, secondary.Message.Function);
        Assert.Equal(SecsItem.Boolean(true), secondary.Message.RootItem);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => passive.ReplyAsync(
                incoming,
                new SecsMessage(6, 12),
                cancellationToken)).ConfigureAwait(true);
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(
            IPAddress.Loopback,
            0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<HsmsConnectionEvent> NextMatchingAsync(
        IAsyncEnumerator<HsmsConnectionEvent> events,
        Func<HsmsConnectionEvent, bool> predicate)
    {
        while (await events.MoveNextAsync().ConfigureAwait(true))
        {
            if (predicate(events.Current))
                return events.Current;
        }

        Assert.Fail("The expected public HSMS connection event was not received.");
        return null!;
    }

    private static async Task<(
        HsmsControlMessageObservation Request,
        HsmsControlMessageObservation Response)> ReadControlExchangeAsync(
            IAsyncEnumerator<HsmsControlMessageObservation> observations,
            HsmsControlMessageDirection requestDirection,
            HsmsMessageType requestType,
            HsmsMessageType responseType)
    {
        Assert.True(await observations.MoveNextAsync().ConfigureAwait(true));
        var first = observations.Current;
        Assert.True(await observations.MoveNextAsync().ConfigureAwait(true));
        var second = observations.Current;
        var values = new[] { first, second };
        var request = Assert.Single(
            values,
            item => item.Direction == requestDirection &&
                item.Header.MessageType == requestType);
        var response = Assert.Single(
            values,
            item => item.Direction != requestDirection &&
                item.Header.MessageType == responseType);

        return (request, response);
    }
}
