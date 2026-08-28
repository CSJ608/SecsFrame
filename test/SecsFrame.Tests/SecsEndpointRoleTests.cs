using System.Net;

namespace SecsFrame.Tests;

public sealed class SecsEndpointRoleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_and_equipment_exchange_primaries_in_both_connection_topologies(
        bool hostIsActive)
    {
        var port = GetFreePort();
        await using var host = new SecsHost(
            CreateOptions(
                port,
                hostIsActive
                    ? HsmsConnectionMode.Active
                    : HsmsConnectionMode.Passive));
        await using var equipment = new SecsEquipment(
            CreateOptions(
                port,
                hostIsActive
                    ? HsmsConnectionMode.Passive
                    : HsmsConnectionMode.Active));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var equipmentRequest = equipment.RegisterPrimaryHandler(
            1,
            1,
            HandleEquipmentRequest);
        using var hostEvent = host.RegisterPrimaryHandler(
            6,
            11,
            HandleHostEvent);

        if (hostIsActive)
        {
            equipment.Start();
            host.Start();
        }
        else
        {
            host.Start();
            equipment.Start();
        }

        await using var hostEvents = host
            .GetEventsAsync(lifetime.Token)
            .GetAsyncEnumerator();
        await using var equipmentEvents = equipment
            .GetEventsAsync(lifetime.Token)
            .GetAsyncEnumerator();
        await Task.WhenAll(
            host.WaitUntilSelectedAsync(lifetime.Token),
            equipment.WaitUntilSelectedAsync(lifetime.Token)).ConfigureAwait(true);

        AssertTopology(host, equipment, hostIsActive);
        await AssertHostRequestAsync(
            host,
            equipment,
            equipmentEvents,
            lifetime.Token).ConfigureAwait(true);
        await AssertEquipmentEventAsync(
            host,
            equipment,
            hostEvents,
            lifetime.Token).ConfigureAwait(true);

        await host.LinktestAsync(lifetime.Token).ConfigureAwait(true);
        Assert.Equal(HsmsSessionState.Selected, host.State);
        Assert.Equal(HsmsSessionState.Selected, equipment.State);
    }

    [Fact]
    public void Role_constructors_require_explicit_connection_options()
    {
        Assert.Throws<ArgumentNullException>(() => new SecsHost(null!));
        Assert.Throws<ArgumentNullException>(() => new SecsEquipment(null!));
    }

    private static ValueTask<SecsMessage?> HandleEquipmentRequest(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.True(context.ReplyExpected);
        Assert.Null(context.Message.RootItem);
        return new ValueTask<SecsMessage?>(
            new SecsMessage(
                1,
                2,
                rootItem: SecsItem.Ascii("EQUIPMENT-01")));
    }

    private static ValueTask<SecsMessage?> HandleHostEvent(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(SecsItem.U4(1001), context.Message.RootItem);
        return new ValueTask<SecsMessage?>(
            new SecsMessage(
                6,
                12,
                rootItem: SecsItem.Boolean(true)));
    }

    private static void AssertTopology(
        SecsHost host,
        SecsEquipment equipment,
        bool hostIsActive)
    {
        Assert.Equal(SecsEndpointRole.Host, host.Role);
        Assert.Equal(SecsEndpointRole.Equipment, equipment.Role);
        Assert.Equal(
            hostIsActive
                ? HsmsConnectionMode.Active
                : HsmsConnectionMode.Passive,
            host.Options.ConnectionMode);
        Assert.Equal(
            hostIsActive
                ? HsmsConnectionMode.Passive
                : HsmsConnectionMode.Active,
            equipment.Options.ConnectionMode);
    }

    private static async Task AssertHostRequestAsync(
        SecsHost host,
        SecsEquipment equipment,
        IAsyncEnumerator<HsmsConnectionEvent> equipmentEvents,
        CancellationToken cancellationToken)
    {
        var send = host.SendAsync(
            new SecsMessage(1, 1, true),
            cancellationToken);
        var incoming = await NextDataMessageAsync(equipmentEvents)
            .ConfigureAwait(true);
        Assert.True(await equipment.TryDispatchAsync(
            incoming,
            cancellationToken).ConfigureAwait(true));
        var reply = await send.ConfigureAwait(true);
        Assert.NotNull(reply);
        Assert.Equal(SecsItem.Ascii("EQUIPMENT-01"), reply.Message.RootItem);
    }

    private static async Task AssertEquipmentEventAsync(
        SecsHost host,
        SecsEquipment equipment,
        IAsyncEnumerator<HsmsConnectionEvent> hostEvents,
        CancellationToken cancellationToken)
    {
        var send = equipment.SendAsync(
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.U4(1001)),
            cancellationToken);
        var incoming = await NextDataMessageAsync(hostEvents)
            .ConfigureAwait(true);
        Assert.True(await host.TryDispatchAsync(
            incoming,
            cancellationToken).ConfigureAwait(true));
        var reply = await send.ConfigureAwait(true);
        Assert.NotNull(reply);
        Assert.Equal(SecsItem.Boolean(true), reply.Message.RootItem);
    }

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

        Assert.Fail("The expected role endpoint data message was not received.");
        return null!;
    }
}
