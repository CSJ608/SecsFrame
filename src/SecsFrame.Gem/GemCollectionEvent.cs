namespace SecsFrame.Gem;

/// <summary>Contains one decoded GEM Collection Event and its report data.</summary>
public sealed class GemCollectionEvent
{
    /// <summary>Creates a Collection Event value.</summary>
    public GemCollectionEvent(
        SecsItem dataId,
        SecsItem eventId,
        IEnumerable<GemCollectedReport> reports)
    {
        DataId = dataId ?? throw new ArgumentNullException(nameof(dataId));
        EventId = eventId ?? throw new ArgumentNullException(nameof(eventId));
        Reports = GemCollection.Copy(reports, nameof(reports));
    }

    /// <summary>Gets the transaction data identifier supplied by the Equipment.</summary>
    public SecsItem DataId { get; }

    /// <summary>Gets the exact dynamic Collection Event identifier.</summary>
    public SecsItem EventId { get; }

    /// <summary>Gets collected reports in linked order.</summary>
    public IReadOnlyList<GemCollectedReport> Reports { get; }
}
