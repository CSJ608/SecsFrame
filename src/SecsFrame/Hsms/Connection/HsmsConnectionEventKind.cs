namespace SecsFrame;

/// <summary>Identifies an event emitted by <see cref="HsmsConnection"/>.</summary>
public enum HsmsConnectionEventKind
{
    /// <summary>The HSMS session state changed.</summary>
    StateChanged,

    /// <summary>An unmatched or primary data message was received.</summary>
    DataMessageReceived,

    /// <summary>An unconsumed HSMS control message was received.</summary>
    ControlMessageReceived,

    /// <summary>An HSMS data message could not be decoded.</summary>
    DataMessageDecodeFailed,
}
