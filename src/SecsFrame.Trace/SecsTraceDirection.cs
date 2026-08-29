namespace SecsFrame.Trace;

/// <summary>Identifies the local direction of a decoded data message.</summary>
public enum SecsTraceDirection
{
    /// <summary>The local endpoint sent the message.</summary>
    Sent,

    /// <summary>The local endpoint received the message.</summary>
    Received,
}
