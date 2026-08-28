namespace SecsFrame.Gem;

/// <summary>Contains one report and its collected runtime values.</summary>
public sealed class GemCollectedReport
{
    /// <summary>Creates collected report data.</summary>
    public GemCollectedReport(
        SecsItem reportId,
        IEnumerable<SecsItem> values)
    {
        ReportId = reportId ?? throw new ArgumentNullException(nameof(reportId));
        Values = GemCollection.Copy(values, nameof(values));
    }

    /// <summary>Gets the exact dynamic report identifier.</summary>
    public SecsItem ReportId { get; }

    /// <summary>Gets the collected values in report-definition order.</summary>
    public IReadOnlyList<SecsItem> Values { get; }
}
