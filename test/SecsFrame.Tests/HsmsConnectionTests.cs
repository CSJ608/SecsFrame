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
        HsmsConnectionMode connectionMode)
        => new(
            IPAddress.Loopback,
            port,
            connectionMode,
            sessionId: 10,
            t3: TimeSpan.FromSeconds(5),
            t5: TimeSpan.FromMilliseconds(10),
            t6: TimeSpan.FromSeconds(5),
            t7: TimeSpan.FromSeconds(10),
            t8: TimeSpan.FromSeconds(5));

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
}
