namespace SecsFrame;

/// <summary>Identifies a stable, machine-readable HSMS diagnostic.</summary>
public enum HsmsDiagnosticCode
{
    /// <summary>The transport reported an I/O failure.</summary>
    TransportFailure,

    /// <summary>An operation was bound to a transport session that ended.</summary>
    TransportSessionExpired,

    /// <summary>An incoming HSMS header or control message violated protocol rules.</summary>
    ProtocolViolation,

    /// <summary>A SECS-II message could not be encoded or decoded in the current operation.</summary>
    CodecFailure,

    /// <summary>T3 expired while waiting for a Secondary.</summary>
    T3Timeout,

    /// <summary>T6 expired while waiting for an HSMS control response.</summary>
    T6Timeout,

    /// <summary>T7 expired before the HSMS session became Selected.</summary>
    T7Timeout,

    /// <summary>T8 expired while an incomplete HSMS frame was being received.</summary>
    T8Timeout,

    /// <summary>The peer rejected HSMS selection.</summary>
    SelectionRejected,

    /// <summary>The peer rejected HSMS deselection.</summary>
    DeselectRejected,

    /// <summary>The peer sent Reject Request for an HSMS control message.</summary>
    ControlRejected,

    /// <summary>The peer sent Reject Request for an HSMS data message.</summary>
    DataMessageRejected,

    /// <summary>An open data transaction ended because its HSMS session changed.</summary>
    TransactionInterrupted,

    /// <summary>An incoming HSMS data message could not be decoded.</summary>
    DataMessageDecodeFailed,
}
