namespace SecsFrame.Tests;

public sealed class HsmsT8MonitorTests
{
    private static readonly TimeSpan T8 = TimeSpan.FromSeconds(5);

    [Fact]
    public void Idle_connection_does_not_create_a_timer()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsT8Monitor(
            T8,
            () => timeoutCount++,
            timerFactory);

        Assert.Empty(timerFactory.Timers);
        Assert.Equal(0, timeoutCount);
    }

    [Fact]
    public void Partial_length_prefix_arms_T8()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsT8Monitor(
            T8,
            () => timeoutCount++,
            timerFactory);

        monitor.Observe(new byte[] { 0x00, 0x00 });
        var timer = Assert.Single(timerFactory.Timers);
        Assert.Equal(T8, timer.DueTime);
        timer.Fire();

        Assert.Equal(1, timeoutCount);
        Assert.True(timer.IsDisposed);
    }

    [Fact]
    public void Partial_payload_progress_replaces_T8_and_ignores_queued_callback()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsT8Monitor(
            T8,
            () => timeoutCount++,
            timerFactory);

        monitor.Observe(new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x01 });
        var first = Assert.Single(timerFactory.Timers);
        monitor.Observe(new byte[] { 0x02, 0x03 });
        var second = timerFactory.Timer!;

        Assert.NotSame(first, second);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsArmed);
        Assert.Equal(T8, second.DueTime);
        first.ForceFire();
        Assert.Equal(0, timeoutCount);
        second.Fire();
        Assert.Equal(1, timeoutCount);
    }

    [Fact]
    public void Complete_frame_stops_T8_during_later_idle_period()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsT8Monitor(
            T8,
            () => timeoutCount++,
            timerFactory);
        var wireFrame = CreateHeaderOnlyWireFrame();

        monitor.Observe(wireFrame.AsSpan(0, 5));
        var timer = Assert.Single(timerFactory.Timers);
        monitor.Observe(wireFrame.AsSpan(5));
        timer.ForceFire();

        Assert.True(timer.IsDisposed);
        Assert.Equal(0, timeoutCount);
    }

    [Fact]
    public void Complete_frame_followed_by_partial_next_frame_remains_armed()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsT8Monitor(
            T8,
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
    public void Reset_isolates_replacement_frame_from_queued_callback()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        var timeoutCount = 0;
        using var monitor = new HsmsT8Monitor(
            T8,
            () => timeoutCount++,
            timerFactory);

        monitor.Observe(new byte[] { 0x00 });
        var previousSessionTimer = timerFactory.Timer!;
        monitor.Reset();
        monitor.Observe(new byte[] { 0x00, 0x00 });
        var currentSessionTimer = timerFactory.Timer!;
        previousSessionTimer.ForceFire();

        Assert.NotSame(previousSessionTimer, currentSessionTimer);
        Assert.Equal(0, timeoutCount);
        Assert.True(currentSessionTimer.IsArmed);
        currentSessionTimer.Fire();
        Assert.Equal(1, timeoutCount);
    }

    [Fact]
    public void Multiple_complete_frames_do_not_create_a_timer()
    {
        var timerFactory = new ManualHsmsTransportTimerFactory();
        using var monitor = new HsmsT8Monitor(T8, () => { }, timerFactory);
        var frame = CreateHeaderOnlyWireFrame();
        var bytes = new byte[frame.Length * 2];
        frame.CopyTo(bytes, 0);
        frame.CopyTo(bytes, frame.Length);

        monitor.Observe(bytes);

        Assert.Empty(timerFactory.Timers);
    }

    [Fact]
    public void Nonpositive_T8_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsT8Monitor(TimeSpan.Zero, () => { }));
    }

    private static byte[] CreateHeaderOnlyWireFrame()
        => new byte[]
        {
            0x00, 0x00, 0x00, 0x0A,
            0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
        };
}
