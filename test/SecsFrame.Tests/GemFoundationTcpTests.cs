using System.Net;
using SecsFrame.Gem;

namespace SecsFrame.Tests;

public sealed class GemFoundationTcpTests
{
    [Fact]
    public async Task Host_and_equipment_complete_foundational_dialogues_over_tcp()
    {
        await using var context = new GemTcpContext();
        await context.StartAsync().ConfigureAwait(true);

        await AssertCommunicationAndOnlineAsync(context).ConfigureAwait(true);
        await AssertOnlineTransitionPolicyAsync(context).ConfigureAwait(true);
        await AssertDynamicDataAsync(context).ConfigureAwait(true);
        await AssertCollectionEventsAsync(context).ConfigureAwait(true);
        await AssertAlarmCatalogAsync(context).ConfigureAwait(true);
        await AssertAlarmSendControlAsync(context).ConfigureAwait(true);
        await AssertAlarmNotificationsAsync(context).ConfigureAwait(true);
        await AssertRemoteCommandsAsync(context).ConfigureAwait(true);
        await AssertClockAsync(context).ConfigureAwait(true);
        await AssertCommunicationReestablishmentPolicyAsync(context)
            .ConfigureAwait(true);
        await context.HostServices.RequestOfflineAsync(context.Token)
            .ConfigureAwait(true);
        await WaitUntilAsync(
            () => context.EquipmentServices.OnlineState == GemOnlineState.Offline,
            context.Token).ConfigureAwait(true);
        Assert.Equal(GemOnlineState.Offline, context.HostServices.OnlineState);
        Assert.Equal(GemOnlineState.Offline, context.EquipmentServices.OnlineState);
        await context.Host.LinktestAsync(context.Token).ConfigureAwait(true);
    }

    private static async Task AssertCommunicationAndOnlineAsync(
        GemTcpContext context)
    {
        var equipmentIdentity =
            await AssertInitialCommunicationEstablishmentPolicyAsync(context)
                .ConfigureAwait(true);
        await WaitUntilAsync(
            () => context.EquipmentServices.CommunicationState ==
                GemCommunicationState.Communicating,
            context.Token).ConfigureAwait(true);
        Assert.Equal(new GemIdentity("EQ-01", "1.5"), equipmentIdentity);
        Assert.Equal(
            GemCommunicationState.Communicating,
            context.HostServices.CommunicationState);
        Assert.Equal(
            GemCommunicationState.Communicating,
            context.EquipmentServices.CommunicationState);
        Assert.Equal(
            context.HostServices.Identity,
            context.EquipmentServices.PeerIdentity);
        Assert.Equal(
            equipmentIdentity,
            await context.HostServices.AreYouOnlineAsync(context.Token)
                .ConfigureAwait(true));

        await context.HostServices.RequestOnlineAsync(context.Token)
            .ConfigureAwait(true);
        await WaitUntilAsync(
            () => context.EquipmentServices.OnlineState == GemOnlineState.Online,
            context.Token).ConfigureAwait(true);
        Assert.Equal(GemOnlineState.Online, context.HostServices.OnlineState);
        Assert.Equal(GemOnlineState.Online, context.EquipmentServices.OnlineState);
    }

    private static async Task<GemIdentity>
        AssertInitialCommunicationEstablishmentPolicyAsync(GemTcpContext context)
    {
        var acceptEstablishment = false;
        var observed = new List<GemIdentity>();
        GemIdentity equipmentIdentity;
        using (context.EquipmentServices.RegisterCommunicationEstablishmentHandler(
            (peerIdentity, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                observed.Add(peerIdentity);
                return new ValueTask<bool>(acceptEstablishment);
            }))
        {
            var rejected = await Assert.ThrowsAsync<GemRequestRejectedException>(
                () => context.HostServices.EstablishCommunicationAsync(
                    context.Token)).ConfigureAwait(true);
            Assert.Equal(GemOperation.EstablishCommunication, rejected.Operation);
            Assert.Equal((byte)1, rejected.Acknowledgement);
            Assert.Equal(
                GemCommunicationState.NotCommunicating,
                context.HostServices.CommunicationState);
            Assert.Equal(
                GemCommunicationState.NotCommunicating,
                context.EquipmentServices.CommunicationState);
            Assert.Null(context.HostServices.PeerIdentity);
            Assert.Null(context.EquipmentServices.PeerIdentity);

            acceptEstablishment = true;
            equipmentIdentity = await context.HostServices
                .EstablishCommunicationAsync(context.Token).ConfigureAwait(true);
        }

        Assert.Equal(
            new[]
            {
                context.HostServices.Identity,
                context.HostServices.Identity,
            },
            observed);
        return equipmentIdentity;
    }

    private static async Task AssertCommunicationReestablishmentPolicyAsync(
        GemTcpContext context)
    {
        var hostPeerIdentity = context.HostServices.PeerIdentity;
        var equipmentPeerIdentity = context.EquipmentServices.PeerIdentity;
        Assert.NotNull(hostPeerIdentity);
        Assert.NotNull(equipmentPeerIdentity);

        var acceptEstablishment = false;
        var observed = new List<GemIdentity>();
        using (context.HostServices.RegisterCommunicationEstablishmentHandler(
            (peerIdentity, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                observed.Add(peerIdentity);
                return new ValueTask<bool>(acceptEstablishment);
            }))
        {
            var rejected = await Assert.ThrowsAsync<GemRequestRejectedException>(
                () => context.EquipmentServices.EstablishCommunicationAsync(
                    context.Token)).ConfigureAwait(true);
            Assert.Equal(GemOperation.EstablishCommunication, rejected.Operation);
            Assert.Equal((byte)1, rejected.Acknowledgement);
            Assert.Equal(
                GemCommunicationState.Communicating,
                context.HostServices.CommunicationState);
            Assert.Equal(
                GemCommunicationState.Communicating,
                context.EquipmentServices.CommunicationState);
            Assert.Equal(hostPeerIdentity, context.HostServices.PeerIdentity);
            Assert.Equal(
                equipmentPeerIdentity,
                context.EquipmentServices.PeerIdentity);

            acceptEstablishment = true;
            Assert.Equal(
                context.HostServices.Identity,
                await context.EquipmentServices.EstablishCommunicationAsync(
                    context.Token).ConfigureAwait(true));
        }

        await WaitUntilAsync(
            () => context.EquipmentServices.Identity.Equals(
                context.HostServices.PeerIdentity),
            context.Token).ConfigureAwait(true);
        Assert.Equal(
            new[]
            {
                context.EquipmentServices.Identity,
                context.EquipmentServices.Identity,
            },
            observed);
        Assert.Equal(
            context.HostServices.Identity,
            await context.EquipmentServices.EstablishCommunicationAsync(
                context.Token).ConfigureAwait(true));
    }

    private static async Task AssertOnlineTransitionPolicyAsync(
        GemTcpContext context)
    {
        var acceptTransitions = false;
        var observed = new List<(
            GemOnlineState Current,
            GemOnlineState Requested)>();
        using (context.EquipmentServices.RegisterOnlineStateTransitionHandler(
            (currentState, requestedState, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                observed.Add((currentState, requestedState));
                return new ValueTask<bool>(acceptTransitions);
            }))
        {
            var rejected = await Assert.ThrowsAsync<GemRequestRejectedException>(
                () => context.HostServices.RequestOfflineAsync(context.Token))
                .ConfigureAwait(true);
            Assert.Equal(GemOperation.RequestOffline, rejected.Operation);
            Assert.Equal((byte)1, rejected.Acknowledgement);
            Assert.Equal(GemOnlineState.Online, context.HostServices.OnlineState);
            Assert.Equal(
                GemOnlineState.Online,
                context.EquipmentServices.OnlineState);

            acceptTransitions = true;
            await context.HostServices.RequestOfflineAsync(context.Token)
                .ConfigureAwait(true);
            await WaitUntilAsync(
                () => context.EquipmentServices.OnlineState ==
                    GemOnlineState.Offline,
                context.Token).ConfigureAwait(true);
            Assert.Equal(
                GemOnlineState.Offline,
                context.HostServices.OnlineState);

            await context.HostServices.RequestOnlineAsync(context.Token)
                .ConfigureAwait(true);
            await WaitUntilAsync(
                () => context.EquipmentServices.OnlineState ==
                    GemOnlineState.Online,
                context.Token).ConfigureAwait(true);
            Assert.Equal(GemOnlineState.Online, context.HostServices.OnlineState);
        }

        Assert.Equal(
            new[]
            {
                (GemOnlineState.Online, GemOnlineState.Offline),
                (GemOnlineState.Online, GemOnlineState.Offline),
                (GemOnlineState.Offline, GemOnlineState.Online),
            },
            observed);
    }

    private static async Task AssertDynamicDataAsync(GemTcpContext context)
    {
        var variables = await context.HostServices.ReadStatusVariablesAsync(
            new[] { SecsItem.Ascii("TEMP"), SecsItem.U4(1001) },
            context.Token).ConfigureAwait(true);
        Assert.Equal(
            new[] { SecsItem.F8(23.5), SecsItem.Ascii("READY") },
            variables);

        var constants = await context.HostServices.ReadEquipmentConstantsAsync(
            new[] { SecsItem.U2(2001) },
            context.Token).ConfigureAwait(true);
        Assert.Equal(
            SecsItem.List(SecsItem.U4(10), SecsItem.Boolean(true)),
            Assert.Single(constants));
    }

    private static async Task AssertCollectionEventsAsync(GemTcpContext context)
    {
        var dataId = SecsItem.U4(9001);
        var eventId = SecsItem.U4(7001);
        var reportId = SecsItem.U4(5001);
        var emptyReportId = SecsItem.Ascii("EMPTY");
        await AssertRejectedReportDefinitionAsync(context, dataId, reportId)
            .ConfigureAwait(true);
        await ConfigureCollectionEventAsync(
            context,
            dataId,
            eventId,
            reportId,
            emptyReportId).ConfigureAwait(true);
        var received = new TaskCompletionSource<GemCollectionEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (context.HostServices.RegisterCollectionEventHandler(
            (collectionEvent, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                received.TrySetResult(collectionEvent);
                return new ValueTask<bool>(true);
            }))
        {
            await context.EquipmentServices.SendCollectionEventAsync(
                SecsItem.U4(9002),
                eventId,
                context.Token).ConfigureAwait(true);
        }

        AssertCollectionEvent(
            await received.Task.ConfigureAwait(true),
            eventId,
            reportId,
            emptyReportId);
        var rejectedEvent = await Assert.ThrowsAsync<GemRequestRejectedException>(
            () => context.EquipmentServices.SendCollectionEventAsync(
                SecsItem.U4(9003),
                eventId,
                context.Token)).ConfigureAwait(true);
        Assert.Equal(GemOperation.CollectionEvent, rejectedEvent.Operation);
        Assert.Equal((byte)1, rejectedEvent.Acknowledgement);
    }

    private static async Task AssertRejectedReportDefinitionAsync(
        GemTcpContext context,
        SecsItem dataId,
        SecsItem reportId)
    {
        var rejectedDefinition = await Assert.ThrowsAsync<GemRequestRejectedException>(
            () => context.HostServices.DefineReportsAsync(
                dataId,
                new[]
                {
                    new GemReportDefinition(
                        reportId,
                        new[] { SecsItem.U4(404) }),
                },
                context.Token)).ConfigureAwait(true);
        Assert.Equal(GemOperation.DefineReports, rejectedDefinition.Operation);
    }

    private static async Task ConfigureCollectionEventAsync(
        GemTcpContext context,
        SecsItem dataId,
        SecsItem eventId,
        SecsItem reportId,
        SecsItem emptyReportId)
    {
        await context.HostServices.DefineReportsAsync(
            dataId,
            new[]
            {
                new GemReportDefinition(
                    reportId,
                    new[]
                    {
                        SecsItem.Ascii("TEMP"),
                        SecsItem.U4(1001),
                        SecsItem.U4(1002),
                    }),
                new GemReportDefinition(
                    emptyReportId,
                    Array.Empty<SecsItem>()),
            },
            context.Token).ConfigureAwait(true);
        var rejectedLink = await Assert.ThrowsAsync<GemRequestRejectedException>(
            () => context.HostServices.LinkEventReportsAsync(
                dataId,
                new[]
                {
                    new GemEventReportLink(
                        eventId,
                        new[] { SecsItem.U4(404) }),
                },
                context.Token)).ConfigureAwait(true);
        Assert.Equal(GemOperation.LinkEventReports, rejectedLink.Operation);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.EquipmentServices.SendCollectionEventAsync(
                dataId,
                eventId,
                context.Token)).ConfigureAwait(true);

        await context.HostServices.LinkEventReportsAsync(
            dataId,
            new[]
            {
                new GemEventReportLink(
                    eventId,
                    new[] { reportId, emptyReportId }),
            },
            context.Token).ConfigureAwait(true);
    }

    private static void AssertCollectionEvent(
        GemCollectionEvent collectionEvent,
        SecsItem eventId,
        SecsItem reportId,
        SecsItem emptyReportId)
    {
        Assert.Equal(SecsItem.U4(9002), collectionEvent.DataId);
        Assert.Equal(eventId, collectionEvent.EventId);
        Assert.Equal(2, collectionEvent.Reports.Count);
        Assert.Equal(reportId, collectionEvent.Reports[0].ReportId);
        Assert.Equal(
            new[]
            {
                SecsItem.F8(23.5),
                SecsItem.Ascii("READY"),
                SecsItem.List(
                    SecsItem.U4(10),
                    SecsItem.Boolean(true)),
            },
            collectionEvent.Reports[0].Values);
        Assert.Equal(emptyReportId, collectionEvent.Reports[1].ReportId);
        Assert.Empty(collectionEvent.Reports[1].Values);
    }

    private static async Task AssertClockAsync(GemTcpContext context)
    {
        Assert.Equal(
            context.InitialTime.ToUniversalTime(),
            await context.HostServices.GetClockAsync(context.Token)
                .ConfigureAwait(true));
        context.Clock.AcceptSet = false;
        var rejected = await Assert.ThrowsAsync<GemRequestRejectedException>(
            () => context.HostServices.SetClockAsync(
                context.InitialTime.AddHours(1),
                context.Token)).ConfigureAwait(true);
        Assert.Equal(GemOperation.SetClock, rejected.Operation);
        Assert.Equal((byte)1, rejected.Acknowledgement);

        context.Clock.AcceptSet = true;
        var replacementTime = context.InitialTime.AddHours(2);
        await context.HostServices.SetClockAsync(replacementTime, context.Token)
            .ConfigureAwait(true);
        Assert.Equal(replacementTime.ToUniversalTime(), context.Clock.Value);
    }

    private static async Task AssertAlarmNotificationsAsync(GemTcpContext context)
    {
        var received = new TaskCompletionSource<GemAlarmNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (context.HostServices.RegisterAlarmNotificationHandler(
            (notification, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                received.TrySetResult(notification);
                return new ValueTask<bool>(true);
            }))
        {
            await context.EquipmentServices.SendAlarmNotificationAsync(
                new GemAlarmNotification(
                    0x81,
                    SecsItem.Ascii("DOOR-01"),
                    "DOOR OPEN"),
                context.Token).ConfigureAwait(true);
        }

        var notification = await received.Task.ConfigureAwait(true);
        Assert.Equal((byte)0x81, notification.Code);
        Assert.Equal(SecsItem.Ascii("DOOR-01"), notification.AlarmId);
        Assert.Equal("DOOR OPEN", notification.Text);

        var rejected = await Assert.ThrowsAsync<GemRequestRejectedException>(
            () => context.EquipmentServices.SendAlarmNotificationAsync(
                new GemAlarmNotification(
                    0x00,
                    SecsItem.U2(3001),
                    "DOOR CLOSED"),
                context.Token)).ConfigureAwait(true);
        Assert.Equal(GemOperation.AlarmNotification, rejected.Operation);
        Assert.Equal((byte)1, rejected.Acknowledgement);
    }

    private static async Task AssertAlarmCatalogAsync(GemTcpContext context)
    {
        using var first = context.EquipmentServices.RegisterAlarm(
            new GemAlarmDefinition(
                0x81,
                SecsItem.Ascii("DOOR-01"),
                "DOOR OPEN"));
        using var second = context.EquipmentServices.RegisterAlarm(
            new GemAlarmDefinition(
                0x02,
                SecsItem.U2(3002),
                "PRESSURE HIGH"));

        var all = await context.HostServices.ListAlarmsAsync(
            cancellationToken: context.Token).ConfigureAwait(true);
        Assert.Equal(2, all.Count);
        Assert.Equal(SecsItem.Ascii("DOOR-01"), all[0].AlarmId);
        Assert.Equal((byte)0x81, all[0].Code);
        Assert.Equal("DOOR OPEN", all[0].Text);
        Assert.Equal(SecsItem.U2(3002), all[1].AlarmId);

        var selected = await context.HostServices.ListAlarmsAsync(
            new[] { SecsItem.U2(404), SecsItem.U2(3002) },
            context.Token).ConfigureAwait(true);
        Assert.Equal(SecsItem.U2(3002), Assert.Single(selected).AlarmId);

        first.Dispose();
        var remaining = await context.HostServices.ListAlarmsAsync(
            cancellationToken: context.Token).ConfigureAwait(true);
        Assert.Equal(SecsItem.U2(3002), Assert.Single(remaining).AlarmId);
    }

    private static async Task AssertAlarmSendControlAsync(GemTcpContext context)
    {
        var alarmId = SecsItem.Ascii("CONTROLLED-01");
        using var alarm = context.EquipmentServices.RegisterAlarm(
            new GemAlarmDefinition(0x81, alarmId, "CONTROLLED ALARM"));

        Assert.True(alarm.IsSendEnabled);
        await context.HostServices.SetAlarmSendEnabledAsync(
            alarmId,
            enabled: false,
            context.Token).ConfigureAwait(true);
        Assert.False(alarm.IsSendEnabled);
        Assert.Equal(
            alarmId,
            Assert.Single(await context.HostServices.ListAlarmsAsync(
                cancellationToken: context.Token).ConfigureAwait(true)).AlarmId);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.EquipmentServices.SendAlarmNotificationAsync(
                new GemAlarmNotification(0x81, alarmId, "CONTROLLED ALARM"),
                context.Token)).ConfigureAwait(true);

        await context.HostServices.SetAlarmSendEnabledAsync(
            alarmId,
            enabled: true,
            context.Token).ConfigureAwait(true);
        Assert.True(alarm.IsSendEnabled);
        var received = new TaskCompletionSource<GemAlarmNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (context.HostServices.RegisterAlarmNotificationHandler(
            (notification, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                received.TrySetResult(notification);
                return new ValueTask<bool>(true);
            }))
        {
            await context.EquipmentServices.SendAlarmNotificationAsync(
                new GemAlarmNotification(0x81, alarmId, "CONTROLLED ALARM"),
                context.Token).ConfigureAwait(true);
        }

        Assert.Equal(alarmId, (await received.Task.ConfigureAwait(true)).AlarmId);
        var rejected = await Assert.ThrowsAsync<GemRequestRejectedException>(
            () => context.HostServices.SetAlarmSendEnabledAsync(
                SecsItem.U2(404),
                enabled: false,
                context.Token)).ConfigureAwait(true);
        Assert.Equal(GemOperation.SetAlarmSendEnabled, rejected.Operation);
        Assert.Equal((byte)1, rejected.Acknowledgement);

        await context.HostServices.SetAlarmSendEnabledAsync(
            alarmId,
            enabled: false,
            context.Token).ConfigureAwait(true);
        alarm.Dispose();
        using var replacement = context.EquipmentServices.RegisterAlarm(
            new GemAlarmDefinition(0x81, alarmId, "CONTROLLED ALARM"));
        Assert.True(replacement.IsSendEnabled);
    }

    private static async Task AssertRemoteCommandsAsync(GemTcpContext context)
    {
        var received = new TaskCompletionSource<GemRemoteCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (context.EquipmentServices.RegisterRemoteCommandHandler(
            (command, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                received.TrySetResult(command);
                return new ValueTask<GemRemoteCommandResult>(
                    new GemRemoteCommandResult(
                        0,
                        new[]
                        {
                            new GemRemoteCommandParameterResult(
                                SecsItem.Ascii("SPEED"),
                                0),
                            new GemRemoteCommandParameterResult(
                                SecsItem.U1(2),
                                7),
                        }));
            }))
        {
            var result = await context.HostServices.ExecuteRemoteCommandAsync(
                CreateRemoteCommand(),
                context.Token).ConfigureAwait(true);
            Assert.Equal((byte)0, result.Acknowledgement);
            Assert.Equal(2, result.ParameterResults.Count);
            Assert.Equal(
                SecsItem.Ascii("SPEED"),
                result.ParameterResults[0].Name);
            Assert.Equal((byte)0, result.ParameterResults[0].Acknowledgement);
            Assert.Equal(SecsItem.U1(2), result.ParameterResults[1].Name);
            Assert.Equal((byte)7, result.ParameterResults[1].Acknowledgement);
        }

        var command = await received.Task.ConfigureAwait(true);
        Assert.Equal(SecsItem.U4(7001), command.Command);
        Assert.Equal(2, command.Parameters.Count);
        Assert.Equal(SecsItem.Ascii("SPEED"), command.Parameters[0].Name);
        Assert.Equal(SecsItem.U2(25), command.Parameters[0].Value);
        Assert.Equal(SecsItem.U1(2), command.Parameters[1].Name);
        Assert.Equal(
            SecsItem.List(SecsItem.Boolean(true), SecsItem.F8(1.5)),
            command.Parameters[1].Value);

        var unavailable = await context.HostServices.ExecuteRemoteCommandAsync(
            CreateRemoteCommand(),
            context.Token).ConfigureAwait(true);
        Assert.Equal((byte)1, unavailable.Acknowledgement);
        Assert.Empty(unavailable.ParameterResults);
    }

    private static GemRemoteCommand CreateRemoteCommand()
        => new(
            SecsItem.U4(7001),
            new[]
            {
                new GemRemoteCommandParameter(
                    SecsItem.Ascii("SPEED"),
                    SecsItem.U2(25)),
                new GemRemoteCommandParameter(
                    SecsItem.U1(2),
                    SecsItem.List(
                        SecsItem.Boolean(true),
                        SecsItem.F8(1.5))),
            });

    private static async Task PumpAsync(
        SecsEndpoint endpoint,
        Func<HsmsConnectionEvent, CancellationToken, ValueTask<bool>> dispatch,
        CancellationToken cancellationToken)
    {
        var events = endpoint
            .GetEventsAsync(cancellationToken)
            .GetAsyncEnumerator();
        try
        {
            while (await events.MoveNextAsync().ConfigureAwait(false))
            {
                _ = await dispatch(events.Current, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken)
                .ConfigureAwait(false);
        }

        Assert.True(condition(), "The expected peer state was not observed.");
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
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class GemTcpContext : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime =
            new(TimeSpan.FromSeconds(20));
        private readonly GemValueRegistration _variable1;
        private readonly GemValueRegistration _variable2;
        private readonly GemValueRegistration _variable3;
        private readonly GemValueRegistration _constant;
        private Task _hostPump = Task.CompletedTask;
        private Task _equipmentPump = Task.CompletedTask;

        internal GemTcpContext()
        {
            var port = GetFreePort();
            Host = new SecsHost(CreateOptions(port, HsmsConnectionMode.Active));
            Equipment = new SecsEquipment(
                CreateOptions(port, HsmsConnectionMode.Passive));
            HostServices = new GemHostServices(
                Host,
                new GemIdentity("HOST-01", "2.0"));
            InitialTime = new DateTimeOffset(
                2026,
                8,
                28,
                9,
                10,
                11,
                TimeSpan.FromHours(8)).AddMilliseconds(120);
            Clock = new TestGemClock(InitialTime);
            EquipmentServices = new GemEquipmentServices(
                Equipment,
                new GemIdentity("EQ-01", "1.5"),
                Clock);
            _variable1 = EquipmentServices.RegisterStatusVariable(
                SecsItem.U4(1001),
                static _ => new ValueTask<SecsItem>(SecsItem.Ascii("READY")));
            _variable2 = EquipmentServices.RegisterStatusVariable(
                SecsItem.Ascii("TEMP"),
                static _ => new ValueTask<SecsItem>(SecsItem.F8(23.5)));
            _variable3 = EquipmentServices.RegisterStatusVariable(
                SecsItem.U4(1002),
                static _ => new ValueTask<SecsItem>(
                    SecsItem.List(
                        SecsItem.U4(10),
                        SecsItem.Boolean(true))));
            _constant = EquipmentServices.RegisterEquipmentConstant(
                SecsItem.U2(2001),
                static _ => new ValueTask<SecsItem>(
                    SecsItem.List(SecsItem.U4(10), SecsItem.Boolean(true))));
        }

        internal SecsHost Host { get; }

        internal SecsEquipment Equipment { get; }

        internal GemHostServices HostServices { get; }

        internal GemEquipmentServices EquipmentServices { get; }

        internal TestGemClock Clock { get; }

        internal DateTimeOffset InitialTime { get; }

        internal CancellationToken Token => _lifetime.Token;

        internal async Task StartAsync()
        {
            Equipment.Start();
            Host.Start();
            _hostPump = PumpAsync(Host, HostServices.TryDispatchAsync, Token);
            _equipmentPump = PumpAsync(
                Equipment,
                EquipmentServices.TryDispatchAsync,
                Token);
            await Task.WhenAll(
                Host.WaitUntilSelectedAsync(Token),
                Equipment.WaitUntilSelectedAsync(Token)).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            await Task.WhenAll(_hostPump, _equipmentPump).ConfigureAwait(false);
            _constant.Dispose();
            _variable3.Dispose();
            _variable2.Dispose();
            _variable1.Dispose();
            EquipmentServices.Dispose();
            HostServices.Dispose();
            await Equipment.DisposeAsync().ConfigureAwait(false);
            await Host.DisposeAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }

    private sealed class TestGemClock : IGemClock
    {
        internal TestGemClock(DateTimeOffset value)
        {
            Value = value;
        }

        internal bool AcceptSet { get; set; } = true;

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
            if (AcceptSet)
                Value = value;
            return new ValueTask<bool>(AcceptSet);
        }
    }
}
