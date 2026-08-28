using System.Net;

namespace SecsFrame.Tests;

public sealed class HsmsConnectionOptionsTests
{
    [Fact]
    public void Explicit_values_are_preserved()
    {
        var address = IPAddress.Loopback;
        var options = new HsmsConnectionOptions(
            address,
            5000,
            HsmsConnectionMode.Active,
            10,
            TimeSpan.FromSeconds(45),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5));

        Assert.Same(address, options.IpAddress);
        Assert.Equal(5000, options.Port);
        Assert.Equal(HsmsConnectionMode.Active, options.ConnectionMode);
        Assert.Equal(10, options.SessionId);
        Assert.Equal(TimeSpan.FromSeconds(45), options.T3);
        Assert.Equal(TimeSpan.FromSeconds(10), options.T5);
        Assert.Equal(TimeSpan.FromSeconds(5), options.T6);
        Assert.Equal(TimeSpan.FromSeconds(10), options.T7);
        Assert.Equal(TimeSpan.FromSeconds(5), options.T8);
    }

    [Fact]
    public void Null_address_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new HsmsConnectionOptions(
                null!,
                5000,
                HsmsConnectionMode.Active,
                10,
                TimeSpan.FromSeconds(45),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Invalid_port_is_rejected(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(port: port));
    }

    [Fact]
    public void Undefined_connection_mode_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(connectionMode: (HsmsConnectionMode)99));
    }

    [Fact]
    public void Control_message_session_identifier_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(sessionId: ushort.MaxValue));
    }

    [Theory]
    [InlineData("T3")]
    [InlineData("T5")]
    [InlineData("T6")]
    [InlineData("T7")]
    [InlineData("T8")]
    public void Nonpositive_timer_is_rejected(string timerName)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateWithTimer(timerName, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateWithTimer(timerName, TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void Submillisecond_T5_is_rejected_instead_of_rounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(
                t5: TimeSpan.FromTicks(
                    TimeSpan.TicksPerMillisecond + 1)));
    }

    [Fact]
    public void T5_beyond_connection_retry_range_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(
                t5: TimeSpan.FromMilliseconds(
                    (long)int.MaxValue + 1)));
    }

    [Fact]
    public void Submillisecond_T8_is_rejected_instead_of_rounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(
                t8: TimeSpan.FromTicks(
                    TimeSpan.TicksPerMillisecond + 1)));
    }

    [Fact]
    public void T8_beyond_StreamFrame_range_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(
                t8: TimeSpan.FromMilliseconds(
                    (long)int.MaxValue + 1)));
    }

    private static HsmsConnectionOptions Create(
        IPAddress? ipAddress = null,
        int port = 5000,
        HsmsConnectionMode connectionMode = HsmsConnectionMode.Active,
        ushort sessionId = 10,
        TimeSpan? t3 = null,
        TimeSpan? t5 = null,
        TimeSpan? t6 = null,
        TimeSpan? t7 = null,
        TimeSpan? t8 = null)
        => new(
            ipAddress ?? IPAddress.Loopback,
            port,
            connectionMode,
            sessionId,
            t3 ?? TimeSpan.FromSeconds(45),
            t5 ?? TimeSpan.FromSeconds(10),
            t6 ?? TimeSpan.FromSeconds(5),
            t7 ?? TimeSpan.FromSeconds(10),
            t8 ?? TimeSpan.FromSeconds(5));

    private static HsmsConnectionOptions CreateWithTimer(
        string timerName,
        TimeSpan value)
        => timerName switch
        {
            "T3" => Create(t3: value),
            "T5" => Create(t5: value),
            "T6" => Create(t6: value),
            "T7" => Create(t7: value),
            "T8" => Create(t8: value),
            _ => throw new ArgumentOutOfRangeException(
                nameof(timerName),
                timerName,
                "Unknown HSMS timer."),
        };
}
