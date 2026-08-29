namespace SecsFrame;

internal sealed class HsmsTransportOptions
{
    public HsmsTransportOptions(
        TimeSpan t5,
        TimeSpan t8,
        bool enableTransportFaultObservation = false)
    {
        if (t5 <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(t5),
                t5,
                "T5 must be positive.");
        }

        if (t5.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(t5),
                t5,
                "T5 must be representable as a whole number of milliseconds.");
        }

        var t5Milliseconds = t5.Ticks / TimeSpan.TicksPerMillisecond;
        if (t5Milliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(t5),
                t5,
                "T5 exceeds the StreamFrame retry-delay range.");
        }

        if (t8 <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(t8),
                t8,
                "T8 must be positive.");
        }

        if (t8.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(t8),
                t8,
                "T8 must be representable as a whole number of milliseconds.");
        }

        var t8Milliseconds = t8.Ticks / TimeSpan.TicksPerMillisecond;
        if (t8Milliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(t8),
                t8,
                "T8 exceeds the StreamFrame incomplete-frame timeout range.");
        }

        T5 = t5;
        T8 = t8;
        T5Milliseconds = (int)t5Milliseconds;
        T8Milliseconds = (int)t8Milliseconds;
        EnableTransportFaultObservation = enableTransportFaultObservation;
    }

    public TimeSpan T5 { get; }

    public TimeSpan T8 { get; }

    public bool EnableTransportFaultObservation { get; }

    internal int T5Milliseconds { get; }

    internal int T8Milliseconds { get; }
}
