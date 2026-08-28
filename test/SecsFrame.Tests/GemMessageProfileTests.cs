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
    }

    private static GemMessageProfile CreateProfile(
        GemMessageProfile baseline,
        GemMessagePair? areYouOnline = null,
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
            baseline.AcceptedAcknowledgement,
            failedAcknowledgement ?? baseline.FailedAcknowledgement,
            useNullClockCodec ? null! : clockCodec ?? baseline.ClockCodec);
}
