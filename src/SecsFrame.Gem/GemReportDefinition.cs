namespace SecsFrame.Gem;

/// <summary>Defines one runtime GEM report and its ordered value identifiers.</summary>
public sealed class GemReportDefinition
{
    /// <summary>Creates a report definition.</summary>
    public GemReportDefinition(
        SecsItem reportId,
        IEnumerable<SecsItem> valueIds)
    {
        ReportId = reportId ?? throw new ArgumentNullException(nameof(reportId));
        ValueIds = GemCollection.Copy(valueIds, nameof(valueIds));
    }

    /// <summary>Gets the exact dynamic report identifier.</summary>
    public SecsItem ReportId { get; }

    /// <summary>Gets the ordered status-variable identifiers in this report.</summary>
    public IReadOnlyList<SecsItem> ValueIds { get; }
}
