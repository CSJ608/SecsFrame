using System.Net;
using SecsFrame.Gem;

namespace SecsFrame.Tests;

public sealed class GemHostServicesTests
{
    [Fact]
    public async Task Collection_event_vectors_require_w_bit_and_unambiguous_report_data()
    {
        await using var endpoint = new SecsHost(CreateOptions());
        using var services = new GemHostServices(
            endpoint,
            new GemIdentity("HOST-01", "1.0"));

        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(6, 11, rootItem: SecsItem.List())))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(6, 11, true, SecsItem.List())))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.List(
                    SecsItem.U4(1),
                    SecsItem.U4(2),
                    SecsItem.List(
                        SecsItem.List(SecsItem.U4(3), SecsItem.List()),
                        SecsItem.List(SecsItem.U4(3), SecsItem.List()))))))
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Collection_event_handler_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsHost(CreateOptions());
        using var services = new GemHostServices(
            endpoint,
            new GemIdentity("HOST-01", "1.0"));
        GemCollectionEventHandler handler = static (_, _) =>
            new ValueTask<bool>(true);

        var first = services.RegisterCollectionEventHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterCollectionEventHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement = services.RegisterCollectionEventHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterCollectionEventHandler(handler));
    }

    [Fact]
    public async Task Communication_establishment_handler_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsHost(CreateOptions());
        using var services = new GemHostServices(
            endpoint,
            new GemIdentity("HOST-01", "1.0"));
        GemCommunicationEstablishmentHandler handler = static (_, _) =>
            new ValueTask<bool>(true);

        Assert.Throws<ArgumentNullException>(() =>
            services.RegisterCommunicationEstablishmentHandler(null!));
        var first =
            services.RegisterCommunicationEstablishmentHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterCommunicationEstablishmentHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement =
            services.RegisterCommunicationEstablishmentHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterCommunicationEstablishmentHandler(handler));
    }

    [Fact]
    public async Task Alarm_notification_vectors_require_w_bit_and_strict_fields()
    {
        await using var endpoint = new SecsHost(CreateOptions());
        using var services = new GemHostServices(
            endpoint,
            new GemIdentity("HOST-01", "1.0"));
        var validBody = SecsItem.List(
            SecsItem.Binary(0x81),
            SecsItem.U2(3001),
            SecsItem.Ascii("DOOR OPEN"));

        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(5, 1, rootItem: validBody))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(5, 1, true, SecsItem.List()))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                5,
                1,
                true,
                SecsItem.List(
                    SecsItem.U1(0x81),
                    SecsItem.U2(3001),
                    SecsItem.Ascii("DOOR OPEN"))))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                5,
                1,
                true,
                SecsItem.List(
                    SecsItem.Binary(0x81, 0x01),
                    SecsItem.U2(3001),
                    SecsItem.Ascii("DOOR OPEN"))))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                5,
                1,
                true,
                SecsItem.List(
                    SecsItem.Binary(0x81),
                    SecsItem.U2(3001),
                    SecsItem.U4(42))))).ConfigureAwait(true);
    }

    [Fact]
    public async Task Alarm_handler_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsHost(CreateOptions());
        using var services = new GemHostServices(
            endpoint,
            new GemIdentity("HOST-01", "1.0"));
        GemAlarmNotificationHandler handler = static (_, _) =>
            new ValueTask<bool>(true);

        var first = services.RegisterAlarmNotificationHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterAlarmNotificationHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement = services.RegisterAlarmNotificationHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterAlarmNotificationHandler(handler));
    }

    private static Task<bool> DispatchAsync(
        GemHostServices services,
        SecsMessage message)
        => services.TryDispatchAsync(
            HsmsConnectionEvent.DataMessageReceived(
                new HsmsIncomingDataMessage(
                    new object(),
                    new HsmsTransportSessionId(1),
                    new HsmsDataMessage(10, 42, message)))).AsTask();

    private static HsmsConnectionOptions CreateOptions()
        => new(
            IPAddress.Loopback,
            GetFreePort(),
            HsmsConnectionMode.Active,
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
}
