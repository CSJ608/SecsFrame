namespace SecsFrame.Trace;

/// <summary>Identifies how much byte content a fault-sample record contains.</summary>
public enum SecsTraceFaultSampleDataClassification
{
    /// <summary>Only stable metadata and the observed byte length are retained.</summary>
    MetadataOnly,

    /// <summary>The bytes are retained after declared ranges are zeroed.</summary>
    RedactedPayload,

    /// <summary>The original observed bytes are retained without redaction.</summary>
    RawPayload,
}
