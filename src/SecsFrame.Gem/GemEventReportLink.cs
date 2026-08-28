namespace SecsFrame.Gem;

/// <summary>Links one runtime Collection Event to an ordered set of reports.</summary>
public sealed class GemEventReportLink
{
    /// <summary>Creates an event-to-report link.</summary>
    public GemEventReportLink(
        SecsItem eventId,
        IEnumerable<SecsItem> reportIds)
    {
        EventId = eventId ?? throw new ArgumentNullException(nameof(eventId));
        ReportIds = GemCollection.Copy(reportIds, nameof(reportIds));
        var identifiers = new HashSet<SecsItem>();
        for (var index = 0; index < ReportIds.Count; index++)
        {
            if (!identifiers.Add(ReportIds[index]))
            {
                throw new ArgumentException(
                    $"Report identifier at index {index} is duplicated.",
                    nameof(reportIds));
            }
        }
    }

    /// <summary>Gets the exact dynamic Collection Event identifier.</summary>
    public SecsItem EventId { get; }

    /// <summary>Gets the ordered report identifiers linked to the event.</summary>
    public IReadOnlyList<SecsItem> ReportIds { get; }
}
