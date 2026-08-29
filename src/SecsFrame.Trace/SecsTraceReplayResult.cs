namespace SecsFrame.Trace;

/// <summary>Describes one sent trace record and its optional new Secondary.</summary>
public sealed class SecsTraceReplayResult
{
    internal SecsTraceReplayResult(SecsTraceRecord record, HsmsDataMessage? secondary)
    {
        Record = record;
        Secondary = secondary;
    }

    /// <summary>Gets the source trace record.</summary>
    public SecsTraceRecord Record { get; }

    /// <summary>Gets the Secondary produced by the new transaction, when requested.</summary>
    public HsmsDataMessage? Secondary { get; }
}
