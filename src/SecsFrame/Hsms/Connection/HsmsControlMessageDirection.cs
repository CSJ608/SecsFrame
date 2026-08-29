namespace SecsFrame;

/// <summary>Identifies the local direction of an observed HSMS control message.</summary>
public enum HsmsControlMessageDirection
{
    /// <summary>The control message was written to the peer.</summary>
    Sent,

    /// <summary>The control message was received from the peer.</summary>
    Received,
}
