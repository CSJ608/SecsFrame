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
    public async Task Remote_command_vectors_require_w_bit_and_unique_parameters()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        var body = SecsItem.List(
            SecsItem.Ascii("START"),
            SecsItem.List(
                SecsItem.List(
                    SecsItem.Ascii("SPEED"),
                    SecsItem.U2(10))));

        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(2, 41, rootItem: body))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(2, 41, true, SecsItem.List())))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                2,
                41,
                true,
                SecsItem.List(
                    SecsItem.Ascii("START"),
                    SecsItem.List(
                        SecsItem.List(SecsItem.Ascii("SPEED")))))))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                2,
                41,
                true,
                SecsItem.List(
                    SecsItem.Ascii("START"),
                    SecsItem.List(
                        SecsItem.List(
                            SecsItem.Ascii("SPEED"),
                            SecsItem.U2(10)),
                        SecsItem.List(
                            SecsItem.Ascii("SPEED"),
                            SecsItem.U2(20)))))))
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Alarm_catalog_vectors_require_w_bit_list_and_unique_ids()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        var body = SecsItem.List(SecsItem.U2(3001));

        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(5, 5, rootItem: body))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(5, 5, true, SecsItem.U2(3001))))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                5,
                5,
                true,
                SecsItem.List(SecsItem.U2(3001), SecsItem.U2(3001)))))
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Alarm_send_control_vectors_require_exact_configured_shape()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        var alarmId = SecsItem.U2(3001);
        var body = SecsItem.List(SecsItem.Binary(0x80), alarmId);

        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(5, 3, rootItem: body))).ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(5, 3, true, SecsItem.List())))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                5,
                3,
                true,
                SecsItem.List(SecsItem.U1(0x80), alarmId))))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                5,
                3,
                true,
                SecsItem.List(SecsItem.Binary(0x80, 0x00), alarmId))))
            .ConfigureAwait(true);
        await Assert.ThrowsAsync<GemProtocolException>(() => DispatchAsync(
            services,
            new SecsMessage(
                5,
                3,
                true,
                SecsItem.List(SecsItem.Binary(0x7F), alarmId))))
            .ConfigureAwait(true);
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
    public async Task Online_state_transition_handler_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        GemOnlineStateTransitionHandler handler = static (_, _, _) =>
            new ValueTask<bool>(true);

        Assert.Throws<ArgumentNullException>(() =>
            services.RegisterOnlineStateTransitionHandler(null!));
        var first = services.RegisterOnlineStateTransitionHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterOnlineStateTransitionHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement =
            services.RegisterOnlineStateTransitionHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterOnlineStateTransitionHandler(handler));
    }

    [Fact]
    public async Task Communication_establishment_handler_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
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

    [Fact]
    public async Task Remote_command_handler_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        GemRemoteCommandHandler handler = static (_, _) =>
            new ValueTask<GemRemoteCommandResult>(
                new GemRemoteCommandResult(
                    0,
                    Array.Empty<GemRemoteCommandParameterResult>()));

        var first = services.RegisterRemoteCommandHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterRemoteCommandHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement = services.RegisterRemoteCommandHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterRemoteCommandHandler(handler));
    }

    [Fact]
    public async Task Remote_command_acceptance_handler_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        GemRemoteCommandAcceptanceHandler handler = static (_, _, _, _) =>
            new ValueTask<bool>(true);

        Assert.Throws<ArgumentNullException>(() =>
            services.RegisterRemoteCommandAcceptanceHandler(null!));
        var first =
            services.RegisterRemoteCommandAcceptanceHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterRemoteCommandAcceptanceHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement =
            services.RegisterRemoteCommandAcceptanceHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterRemoteCommandAcceptanceHandler(handler));
    }

    [Fact]
    public async Task Collection_event_send_policy_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        GemCollectionEventSendPolicyHandler handler = static (_, _, _, _, _) =>
            new ValueTask<bool>(true);

        Assert.Throws<ArgumentNullException>(() =>
            services.RegisterCollectionEventSendPolicyHandler(null!));
        var first = services.RegisterCollectionEventSendPolicyHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterCollectionEventSendPolicyHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement =
            services.RegisterCollectionEventSendPolicyHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterCollectionEventSendPolicyHandler(handler));
    }

    [Fact]
    public async Task Alarm_notification_send_policy_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        GemAlarmNotificationSendPolicyHandler handler = static (_, _, _, _) =>
            new ValueTask<bool>(true);

        Assert.Throws<ArgumentNullException>(() =>
            services.RegisterAlarmNotificationSendPolicyHandler(null!));
        var first =
            services.RegisterAlarmNotificationSendPolicyHandler(handler);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterAlarmNotificationSendPolicyHandler(handler));
        first.Dispose();
        first.Dispose();
        using var replacement =
            services.RegisterAlarmNotificationSendPolicyHandler(handler);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterAlarmNotificationSendPolicyHandler(handler));
    }

    [Fact]
    public async Task Alarm_registration_is_exact_disposable_and_replaceable()
    {
        await using var endpoint = new SecsEquipment(
            CreateOptions(GetFreePort(), HsmsConnectionMode.Active));
        using var services = new GemEquipmentServices(
            endpoint,
            new GemIdentity("EQ-01", "1.0"),
            new TestGemClock(Epoch));
        var definition = new GemAlarmDefinition(
            0x81,
            SecsItem.U2(3001),
            "DOOR OPEN");

        var first = services.RegisterAlarm(definition);
        Assert.Equal(definition.AlarmId, first.AlarmId);
        Assert.True(first.IsSendEnabled);
        Assert.Throws<InvalidOperationException>(() =>
            services.RegisterAlarm(definition));
        first.Dispose();
        first.Dispose();
        using var replacement = services.RegisterAlarm(definition);
        Assert.True(replacement.IsSendEnabled);
        services.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            services.RegisterAlarm(new GemAlarmDefinition(
                0x01,
                SecsItem.U2(3002),
                "PRESSURE HIGH")));
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
