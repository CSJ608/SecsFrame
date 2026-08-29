namespace SecsFrame;

/// <summary>Identifies an explicitly observed HSMS transport fault.</summary>
public enum HsmsTransportFaultKind
{
    /// <summary>T8 expired while an incomplete HSMS frame was buffered.</summary>
    IncompleteFrameTimeout,
}
