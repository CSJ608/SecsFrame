using System.Net;

namespace SecsFrame.Tests;

public sealed class HsmsPrimaryRouterTests
{
    [Fact]
    public async Task Runtime_handler_sends_secondary_on_original_transaction()
    {
        var port = GetFreePort();
        await using var passive = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Passive));
        await using var active = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Active));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var router = new HsmsPrimaryRouter(passive);
        var calls = 0;
        using var route = router.Register(
            6,
            11,
            (context, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                calls++;
                Assert.Equal((ushort)10, context.ProtocolSessionId);
                Assert.True(context.ReplyExpected);
                Assert.Equal(6, context.Message.Stream);
                Assert.Equal(11, context.Message.Function);
                Assert.Equal(SecsItem.Ascii("LOT-42"), context.Message.RootItem);
                return new ValueTask<SecsMessage?>(
                    new SecsMessage(
                        6,
                        12,
                        rootItem: SecsItem.Boolean(true)));
            });

        passive.Start();
        active.Start();
        await using var events = passive
            .GetEventsAsync(lifetime.Token)
            .GetAsyncEnumerator();
        await Task.WhenAll(
            passive.WaitUntilSelectedAsync(lifetime.Token),
            active.WaitUntilSelectedAsync(lifetime.Token)).ConfigureAwait(true);
        var send = active.SendAsync(
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.Ascii("LOT-42")),
            lifetime.Token);
        var incoming = await NextDataMessageAsync(events).ConfigureAwait(true);

        Assert.True(await router.TryDispatchAsync(
            incoming,
            lifetime.Token).ConfigureAwait(true));
        var secondary = await send.ConfigureAwait(true);

        Assert.Equal(1, calls);
        Assert.NotNull(secondary);
        Assert.Equal(incoming.IncomingMessage!.DataMessage.SystemBytes, secondary.SystemBytes);
        Assert.Equal(6, secondary.Message.Stream);
        Assert.Equal(12, secondary.Message.Function);
        Assert.Equal(SecsItem.Boolean(true), secondary.Message.RootItem);
    }

    [Fact]
    public async Task Unmatched_data_event_remains_available_to_application()
    {
        var port = GetFreePort();
        await using var passive = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Passive));
        await using var active = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Active));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var router = new HsmsPrimaryRouter(passive);
        using var route = router.Register(
            6,
            11,
            static (_, _) => new ValueTask<SecsMessage?>((SecsMessage?)null));

        passive.Start();
        active.Start();
        await using var events = passive
            .GetEventsAsync(lifetime.Token)
            .GetAsyncEnumerator();
        await Task.WhenAll(
            passive.WaitUntilSelectedAsync(lifetime.Token),
            active.WaitUntilSelectedAsync(lifetime.Token)).ConfigureAwait(true);
        Assert.Null(await active.SendAsync(
            new SecsMessage(1, 1),
            lifetime.Token).ConfigureAwait(true));
        var connectionEvent = await NextDataMessageAsync(events).ConfigureAwait(true);

        Assert.False(await router.TryDispatchAsync(
            connectionEvent,
            lifetime.Token).ConfigureAwait(true));
        Assert.Equal(1, connectionEvent.IncomingMessage!.DataMessage.Message.Stream);
        Assert.Equal(1, connectionEvent.IncomingMessage.DataMessage.Message.Function);
    }

    [Fact]
    public async Task Registration_is_exact_disposable_and_replaceable()
    {
        var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        await using var owned = connection;
        var router = new HsmsPrimaryRouter(connection);
        HsmsPrimaryHandler handler = static (_, _) =>
            new ValueTask<SecsMessage?>((SecsMessage?)null);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => router.Register(128, 1, handler));
        var first = router.Register(1, 1, handler);
        Assert.Equal(1, first.Stream);
        Assert.Equal(1, first.Function);
        Assert.Throws<InvalidOperationException>(
            () => router.Register(1, 1, handler));

        first.Dispose();
        first.Dispose();
        using var replacement = router.Register(1, 1, handler);
    }

    [Fact]
    public async Task Handler_failure_and_cancellation_propagate_to_event_loop()
    {
        var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        await using var owned = connection;
        var router = new HsmsPrimaryRouter(connection);
        var incoming = CreateIncoming(new SecsMessage(1, 3));
        using var failed = router.Register(
            1,
            3,
            static (_, _) => throw new FormatException("Bad dynamic body."));

        var error = await Assert.ThrowsAsync<FormatException>(
            () => router.TryDispatchAsync(incoming).AsTask()).ConfigureAwait(true);
        Assert.Equal("Bad dynamic body.", error.Message);

        failed.Dispose();
        var called = false;
        using var canceled = router.Register(
            1,
            3,
            (_, _) =>
            {
                called = true;
                return new ValueTask<SecsMessage?>((SecsMessage?)null);
            });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.TryDispatchAsync(
                incoming,
                cancellation.Token).AsTask()).ConfigureAwait(true);
        Assert.False(called);
    }

    [Fact]
    public async Task Returning_secondary_for_message_without_w_bit_is_rejected()
    {
        var connection = new HsmsConnection(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        await using var owned = connection;
        var router = new HsmsPrimaryRouter(connection);
        var incoming = CreateIncoming(new SecsMessage(6, 11));
        using var route = router.Register(
            6,
            11,
            static (_, _) => new ValueTask<SecsMessage?>(
                new SecsMessage(6, 12)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.TryDispatchAsync(incoming).AsTask()).ConfigureAwait(true);

        Assert.Contains("does not request", error.Message, StringComparison.Ordinal);
    }

    private static HsmsIncomingDataMessage CreateIncoming(SecsMessage message)
        => new(
            new object(),
            new HsmsTransportSessionId(1),
            new HsmsDataMessage(10, 42, message));

    private static HsmsConnectionOptions CreateOptions(
        int port,
        HsmsConnectionMode mode)
        => new(
            IPAddress.Loopback,
            port,
            mode,
            sessionId: 10,
            t3: TimeSpan.FromSeconds(5),
            t5: TimeSpan.FromMilliseconds(10),
            t6: TimeSpan.FromSeconds(5),
            t7: TimeSpan.FromSeconds(10),
            t8: TimeSpan.FromSeconds(5));

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

    private static async Task<HsmsConnectionEvent> NextDataMessageAsync(
        IAsyncEnumerator<HsmsConnectionEvent> events)
    {
        while (await events.MoveNextAsync().ConfigureAwait(true))
        {
            if (events.Current.Kind == HsmsConnectionEventKind.DataMessageReceived)
                return events.Current;
        }

        Assert.Fail("The expected data-message event was not received.");
        return null!;
    }
}
