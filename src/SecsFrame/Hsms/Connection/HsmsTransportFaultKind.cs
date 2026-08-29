namespace SecsFrame;

/// <summary>Identifies an explicitly observed HSMS transport fault.</summary>
public enum HsmsTransportFaultKind
{
    /// <summary>T8 expired while an incomplete HSMS frame was buffered.</summary>
    IncompleteFrameTimeout,

    /// <summary>A complete framed payload could not be decoded.</summary>
    DecodeFailed,

    /// <summary>Bytes were discarded while the framer resynchronized.</summary>
    DiscardedByResync,

    /// <summary>An incomplete frame exceeded the configured buffer limit.</summary>
    IncompleteFrameOverflow,
}
