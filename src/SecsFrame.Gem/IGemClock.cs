namespace SecsFrame.Gem;

/// <summary>Abstracts application-owned clock read and set behavior.</summary>
public interface IGemClock
{
    /// <summary>Reads the current equipment clock.</summary>
    ValueTask<DateTimeOffset> GetCurrentTimeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Attempts to set the equipment clock.</summary>
    ValueTask<bool> SetCurrentTimeAsync(
        DateTimeOffset value,
        CancellationToken cancellationToken = default);
}
