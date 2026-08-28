namespace SecsFrame.Tests;

public sealed class HsmsIncompleteFrameMonitorTests
{
    private static readonly TimeSpan TimeoutValue = TimeSpan.FromSeconds(5);

    [Fact]
    public void Idle_connection_does_not_arm_or_fire_timeout()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsIncompleteFrameMonitor(
            TimeoutValue,
            () => timeoutCount++,
            timerFactory);

        timerFactory.Timer!.Fire();

        Assert.False(timerFactory.Timer.IsArmed);
        Assert.Equal(0, timeoutCount);
    }

    [Fact]
    public void Partial_length_prefix_arms_timeout()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsIncompleteFrameMonitor(
            TimeoutValue,
            () => timeoutCount++,
            timerFactory);

        monitor.Observe(new byte[] { 0x00, 0x00 });
        timerFactory.Timer!.Fire();

        Assert.Equal(1, timeoutCount);
        Assert.False(timerFactory.Timer.IsArmed);
    }

    [Fact]
    public void Partial_payload_progress_restarts_timeout()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        using var monitor = new HsmsIncompleteFrameMonitor(
            TimeoutValue,
            () => { },
            timerFactory);

        monitor.Observe(new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x01 });
        var firstChangeCount = timerFactory.Timer!.ChangeCount;
        monitor.Observe(new byte[] { 0x02, 0x03 });

        Assert.True(timerFactory.Timer.IsArmed);
        Assert.Equal(TimeoutValue, timerFactory.Timer.DueTime);
        Assert.True(timerFactory.Timer.ChangeCount > firstChangeCount);
    }

    [Fact]
    public void Complete_frame_stops_timeout_during_later_idle_period()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsIncompleteFrameMonitor(
            TimeoutValue,
            () => timeoutCount++,
            timerFactory);
        var wireFrame = CreateHeaderOnlyWireFrame();

        monitor.Observe(wireFrame.AsSpan(0, 5));
        Assert.True(timerFactory.Timer!.IsArmed);
        monitor.Observe(wireFrame.AsSpan(5));
        timerFactory.Timer.Fire();

        Assert.False(timerFactory.Timer.IsArmed);
        Assert.Equal(0, timeoutCount);
    }

    [Fact]
    public void Complete_frame_followed_by_partial_next_frame_remains_armed()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsIncompleteFrameMonitor(
            TimeoutValue,
            () => timeoutCount++,
            timerFactory);
        var first = CreateHeaderOnlyWireFrame();
        var bytes = new byte[first.Length + 1];
        first.CopyTo(bytes, 0);
        bytes[^1] = 0;

        monitor.Observe(bytes);
        timerFactory.Timer!.Fire();

        Assert.Equal(1, timeoutCount);
    }

    [Fact]
    public void Reset_cancels_pending_timeout()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsIncompleteFrameMonitor(
            TimeoutValue,
            () => timeoutCount++,
            timerFactory);

        monitor.Observe(new byte[] { 0x00 });
        monitor.Reset();
        timerFactory.Timer!.Fire();

        Assert.False(timerFactory.Timer.IsArmed);
        Assert.Equal(0, timeoutCount);
    }

    [Fact]
    public void Nonpositive_timeout_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsIncompleteFrameMonitor(TimeSpan.Zero, () => { }));
    }

    private static byte[] CreateHeaderOnlyWireFrame()
        => new byte[]
        {
            0x00, 0x00, 0x00, 0x0A,
            0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
        };
}
