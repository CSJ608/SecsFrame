namespace SecsFrame.Trace;

/// <summary>Configures explicit source-interval pacing for trace replay.</summary>
public sealed class SecsTraceReplayTimingOptions
{
    /// <summary>The default upper bound for one scaled replay delay.</summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(1);

    /// <summary>Creates explicit replay timing options.</summary>
    /// <param name="speedMultiplier">
    /// Replay speed relative to source intervals. Two means half-length delays.
    /// </param>
    /// <param name="maxDelay">Upper bound applied after interval scaling.</param>
    public SecsTraceReplayTimingOptions(
        double speedMultiplier = 1d,
        TimeSpan? maxDelay = null)
    {
        if (double.IsNaN(speedMultiplier) || double.IsInfinity(speedMultiplier) || speedMultiplier <= 0d)
            throw new ArgumentOutOfRangeException(nameof(speedMultiplier), speedMultiplier, "The speed multiplier must be finite and positive.");

        var effectiveMaxDelay = maxDelay ?? DefaultMaxDelay;
        if (effectiveMaxDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxDelay), effectiveMaxDelay, "The maximum delay must be positive.");

        SpeedMultiplier = speedMultiplier;
        MaxDelay = effectiveMaxDelay;
    }

    /// <summary>Gets the replay speed relative to source intervals.</summary>
    public double SpeedMultiplier { get; }

    /// <summary>Gets the upper bound for one scaled replay delay.</summary>
    public TimeSpan MaxDelay { get; }

    internal TimeSpan Scale(TimeSpan sourceInterval)
    {
        if (sourceInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sourceInterval), sourceInterval, "The source interval cannot be negative.");

        var scaledTicks = sourceInterval.Ticks / SpeedMultiplier;
        if (scaledTicks >= MaxDelay.Ticks)
            return MaxDelay;

        var roundedTicks = (long)Math.Round(scaledTicks, MidpointRounding.AwayFromZero);
        return TimeSpan.FromTicks(roundedTicks);
    }
}
