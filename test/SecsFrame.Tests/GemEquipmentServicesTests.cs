using System.Net;
using SecsFrame.Gem;

namespace SecsFrame.Tests;

public sealed class GemEquipmentServicesTests
{
    private static readonly DateTimeOffset Epoch = new(
        1970,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task Invalid_bodies_unknown_identifiers_and_missing_w_bit_are_rejected()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));

        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(1, 17, true, SecsItem.List()))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(1, 3, true, SecsItem.U4(1001)))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(1, 3, true, SecsItem.List(SecsItem.U4(404)))))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(1, 17))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(2, 33, true, SecsItem.List()))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                2,
                33,
                true,
                SecsItem.List(
                    SecsItem.U4(1),
                    SecsItem.List(
                        SecsItem.List(SecsItem.U4(2), SecsItem.List()),
                        SecsItem.List(SecsItem.U4(2), SecsItem.List()))))))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                2,
                35,
                true,
                SecsItem.List(
                    SecsItem.U4(1),
                    SecsItem.List(
                        SecsItem.List(SecsItem.U4(3), SecsItem.List()),
                        SecsItem.List(SecsItem.U4(3), SecsItem.List()))))))
            .ConfigureAwait(true);

        Assert.Equal(GemOnlineState.Offline, services.OnlineState);
    }

    [Fact]
    public async Task Dynamic_registration_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        var identifier = SecsItem.U4(1001);
        GemValueProvider provider = static _ =>
            new ValueTask<SecsItem>(SecsItem.Ascii("READY"));

        var first = services.RegisterStatusVariable(identifier, provider);
        Assert.Equal(identifier, first.Id);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterStatusVariable(identifier, provider));

        first.Dispose();
        first.Dispose();
        using var replacement = services.RegisterStatusVariable(identifier, provider);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterStatusVariable(SecsItem.U4(1002), provider));
    }

    [Fact]
    public async Task Event_link_rejects_duplicate_report_identifiers()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));

        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                2,
                35,
                true,
                SecsItem.List(
                    SecsItem.U4(1),
                    SecsItem.List(
                        SecsItem.List(
                            SecsItem.U4(3),
                            SecsItem.List(
                                SecsItem.U4(2),
                                SecsItem.U4(2))))))))
            .ConfigureAwait(true);
    }

    private static Task<bool> DispatchAsync(
        GemEquipmentServices services,
        SecsMessage message)
        => services.TryDispatchAsync(
            HsmsConnectionEvent.DataMessageReceived(
                new HsmsIncomingDataMessage(
                    new object(),
                    new HsmsTransportSessionId(1),
                    new HsmsDataMessage(10, 42, message)))).AsTask();

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
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class TestGemClock : IGemClock
    {
        internal TestGemClock(DateTimeOffset value)
        {
            Value = value;
        }

        internal DateTimeOffset Value { get; private set; }

        public ValueTask<DateTimeOffset> GetCurrentTimeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<DateTimeOffset>(Value);
        }

        public ValueTask<bool> SetCurrentTimeAsync(
            DateTimeOffset value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Value = value;
            return new ValueTask<bool>(true);
        }
    }
}
