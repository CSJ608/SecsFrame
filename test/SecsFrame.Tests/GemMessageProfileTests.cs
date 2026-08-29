using SecsFrame.Gem;

namespace SecsFrame.Tests;

public sealed class GemMessageProfileTests
{
    [Fact]
    public void Engineering_baseline_is_explicit_and_round_trips_utc_clock()
    {
        var profile = GemMessageProfile.CreateEngineeringBaseline();
        var value = new DateTimeOffset(
            2026,
            8,
            28,
            9,
            10,
            11,
            TimeSpan.FromHours(8)).AddMilliseconds(120);

        Assert.Equal(new GemMessagePair(1, 13, 14), profile.EstablishCommunication);
        Assert.Equal(new GemMessagePair(1, 1, 2), profile.AreYouOnline);
        Assert.Equal(new GemMessagePair(1, 17, 18), profile.RequestOnline);
        Assert.Equal(new GemMessagePair(1, 15, 16), profile.RequestOffline);
        Assert.Equal(new GemMessagePair(1, 3, 4), profile.ReadStatusVariables);
        Assert.Equal(new GemMessagePair(2, 13, 14), profile.ReadEquipmentConstants);
        Assert.Equal(new GemMessagePair(2, 17, 18), profile.GetClock);
        Assert.Equal(new GemMessagePair(2, 31, 32), profile.SetClock);
        Assert.Equal(new GemMessagePair(2, 33, 34), profile.DefineReports);
        Assert.Equal(new GemMessagePair(2, 35, 36), profile.LinkEventReports);
        Assert.Equal(new GemMessagePair(6, 11, 12), profile.CollectionEvent);
        Assert.Equal(new GemMessagePair(5, 1, 2), profile.AlarmNotification);
        Assert.Equal(new GemMessagePair(2, 41, 42), profile.RemoteCommand);
        Assert.Equal(new GemMessagePair(5, 5, 6), profile.ListAlarms);
        Assert.Equal(
            new GemMessagePair(5, 1, 2),
            CreateLegacyCompatibleProfile(profile).AlarmNotification);
        Assert.Equal(
            new GemMessagePair(2, 41, 42),
            CreateLegacyCompatibleProfile(profile).RemoteCommand);
        Assert.Equal(
            new GemMessagePair(2, 41, 42),
            CreateAlarmCompatibleProfile(profile).RemoteCommand);
        Assert.Equal(
            new GemMessagePair(5, 5, 6),
            CreateRemoteCompatibleProfile(profile).ListAlarms);
        Assert.Equal((byte)0, profile.AcceptedAcknowledgement);
        Assert.Equal((byte)1, profile.FailedAcknowledgement);
        Assert.Equal("2026082801101112", profile.ClockCodec.Encode(value));
        Assert.Equal(value.ToUniversalTime(), profile.ClockCodec.Decode(
            profile.ClockCodec.Encode(value)));
    }

    [Fact]
    public void Profile_rejects_ambiguous_routes_and_acknowledgements()
    {
        var baseline = GemMessageProfile.CreateEngineeringBaseline();

        Assert.Throws<ArgumentException>(() => CreateProfile(
            baseline,
            areYouOnline: baseline.EstablishCommunication));
        Assert.Throws<ArgumentException>(() => CreateProfile(
            baseline,
            collectionEvent: baseline.EstablishCommunication));
        Assert.Throws<ArgumentException>(() => CreateProfile(
            baseline,
            alarmNotification: baseline.EstablishCommunication));
        Assert.Throws<ArgumentException>(() => CreateProfile(
            baseline,
            remoteCommand: baseline.EstablishCommunication));
        Assert.Throws<ArgumentException>(() => CreateProfile(
            baseline,
            listAlarms: baseline.EstablishCommunication));
        Assert.Throws<ArgumentException>(() => CreateProfile(
            baseline,
            failedAcknowledgement: baseline.AcceptedAcknowledgement));
        Assert.Throws<ArgumentNullException>(() => CreateProfile(
            baseline,
            useNullClockCodec: true));
    }

    [Fact]
    public void Public_values_reject_invalid_protocol_boundaries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GemMessagePair(128, 1, 2));
        var parityIndependentPair = new GemMessagePair(1, 2, 2);
        Assert.Equal((byte)2, parityIndependentPair.PrimaryFunction);
        Assert.Equal((byte)2, parityIndependentPair.SecondaryFunction);
        Assert.Throws<ArgumentException>(() => new GemIdentity("设备", "1.0"));
        Assert.Throws<FormatException>(() =>
            GemMessageProfile.CreateEngineeringBaseline().ClockCodec.Decode("bad"));
        var valueIds = new[] { SecsItem.U4(1) };
        var definition = new GemReportDefinition(SecsItem.U4(2), valueIds);
        valueIds[0] = SecsItem.U4(3);
        Assert.Equal(SecsItem.U4(1), Assert.Single(definition.ValueIds));
        Assert.Throws<ArgumentNullException>(() =>
            new GemReportDefinition(null!, Array.Empty<SecsItem>()));
        Assert.Throws<ArgumentException>(() =>
            new GemEventReportLink(
                SecsItem.U4(1),
                new SecsItem[] { null! }));
        Assert.Throws<ArgumentException>(() =>
            new GemEventReportLink(
                SecsItem.U4(1),
                new[] { SecsItem.U4(2), SecsItem.U4(2) }));
        var alarm = new GemAlarmNotification(
            0x81,
            SecsItem.U2(3001),
            "DOOR OPEN");
        Assert.Equal((byte)0x81, alarm.Code);
        Assert.Equal(SecsItem.U2(3001), alarm.AlarmId);
        Assert.Equal("DOOR OPEN", alarm.Text);
        Assert.Throws<ArgumentNullException>(() =>
            new GemAlarmNotification(0, null!, string.Empty));
        Assert.Throws<ArgumentException>(() =>
            new GemAlarmNotification(0, SecsItem.U2(3001), "报警"));
        var alarmDefinition = new GemAlarmDefinition(
            0x81,
            SecsItem.Ascii("DOOR-01"),
            "DOOR OPEN");
        Assert.Equal((byte)0x81, alarmDefinition.Code);
        Assert.Equal(SecsItem.Ascii("DOOR-01"), alarmDefinition.AlarmId);
        Assert.Equal("DOOR OPEN", alarmDefinition.Text);
        Assert.Throws<ArgumentException>(() =>
            new GemAlarmDefinition(0, SecsItem.U2(3001), "报警"));
    }

    [Fact]
    public void Remote_command_values_are_immutable_and_unambiguous()
    {
        var parameters = new[]
        {
            new GemRemoteCommandParameter(
                SecsItem.Ascii("SPEED"),
                SecsItem.U2(10)),
        };
        var command = new GemRemoteCommand(SecsItem.U4(7), parameters);
        parameters[0] = new GemRemoteCommandParameter(
            SecsItem.Ascii("MODE"),
            SecsItem.Ascii("AUTO"));

        Assert.Equal(SecsItem.Ascii("SPEED"), Assert.Single(command.Parameters).Name);
        Assert.Throws<ArgumentException>(() => new GemRemoteCommand(
            SecsItem.Ascii("START"),
            new[]
            {
                new GemRemoteCommandParameter(
                    SecsItem.Ascii("SPEED"),
                    SecsItem.U2(10)),
                new GemRemoteCommandParameter(
                    SecsItem.Ascii("SPEED"),
                    SecsItem.U2(20)),
            }));
        Assert.Throws<ArgumentNullException>(() =>
            new GemRemoteCommandParameter(null!, SecsItem.U1(1)));
        Assert.Throws<ArgumentException>(() => new GemRemoteCommandResult(
            1,
            new[]
            {
                new GemRemoteCommandParameterResult(SecsItem.U1(1), 2),
                new GemRemoteCommandParameterResult(SecsItem.U1(1), 3),
            }));
    }

    private static GemMessageProfile CreateProfile(
        GemMessageProfile baseline,
        GemMessagePair? areYouOnline = null,
        GemMessagePair? collectionEvent = null,
        GemMessagePair? alarmNotification = null,
        GemMessagePair? remoteCommand = null,
        GemMessagePair? listAlarms = null,
        byte? failedAcknowledgement = null,
        GemClockCodec? clockCodec = default,
        bool useNullClockCodec = false)
        => new(
            baseline.EstablishCommunication,
            areYouOnline ?? baseline.AreYouOnline,
            baseline.RequestOnline,
            baseline.RequestOffline,
            baseline.ReadStatusVariables,
            baseline.ReadEquipmentConstants,
            baseline.GetClock,
            baseline.SetClock,
            baseline.DefineReports,
            baseline.LinkEventReports,
            collectionEvent ?? baseline.CollectionEvent,
            alarmNotification ?? baseline.AlarmNotification,
            remoteCommand ?? baseline.RemoteCommand,
            listAlarms ?? baseline.ListAlarms,
            baseline.AcceptedAcknowledgement,
            failedAcknowledgement ?? baseline.FailedAcknowledgement,
            useNullClockCodec ? null! : clockCodec ?? baseline.ClockCodec);

    private static GemMessageProfile CreateLegacyCompatibleProfile(
        GemMessageProfile baseline)
        => new(
            baseline.EstablishCommunication,
            baseline.AreYouOnline,
            baseline.RequestOnline,
            baseline.RequestOffline,
            baseline.ReadStatusVariables,
            baseline.ReadEquipmentConstants,
            baseline.GetClock,
            baseline.SetClock,
            baseline.DefineReports,
            baseline.LinkEventReports,
            baseline.CollectionEvent,
            baseline.AcceptedAcknowledgement,
            baseline.FailedAcknowledgement,
            baseline.ClockCodec);

    private static GemMessageProfile CreateAlarmCompatibleProfile(
        GemMessageProfile baseline)
        => new(
            baseline.EstablishCommunication,
            baseline.AreYouOnline,
            baseline.RequestOnline,
            baseline.RequestOffline,
            baseline.ReadStatusVariables,
            baseline.ReadEquipmentConstants,
            baseline.GetClock,
            baseline.SetClock,
            baseline.DefineReports,
            baseline.LinkEventReports,
            baseline.CollectionEvent,
            baseline.AlarmNotification,
            baseline.AcceptedAcknowledgement,
            baseline.FailedAcknowledgement,
            baseline.ClockCodec);

    private static GemMessageProfile CreateRemoteCompatibleProfile(
        GemMessageProfile baseline)
        => new(
            baseline.EstablishCommunication,
            baseline.AreYouOnline,
            baseline.RequestOnline,
            baseline.RequestOffline,
            baseline.ReadStatusVariables,
            baseline.ReadEquipmentConstants,
            baseline.GetClock,
            baseline.SetClock,
            baseline.DefineReports,
            baseline.LinkEventReports,
            baseline.CollectionEvent,
            baseline.AlarmNotification,
            baseline.RemoteCommand,
            baseline.AcceptedAcknowledgement,
            baseline.FailedAcknowledgement,
            baseline.ClockCodec);
}
