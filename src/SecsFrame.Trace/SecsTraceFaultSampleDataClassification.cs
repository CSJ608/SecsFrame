namespace SecsFrame.Trace;

/// <summary>Identifies how much payload data a fault-sample record contains.</summary>
public enum SecsTraceFaultSampleDataClassification
{
    /// <summary>Only the HSMS header and original body length are retained.</summary>
    MetadataOnly,

    /// <summary>The body is retained after declared byte ranges are zeroed.</summary>
    RedactedPayload,

    /// <summary>The original body bytes are retained without redaction.</summary>
    RawPayload,
}
