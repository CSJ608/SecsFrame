namespace SecsFrame;

/// <summary>Specifies which peer initiates the TCP connection.</summary>
/// <remarks>
/// The connection mode is independent of the Host or Equipment application
/// role.
/// </remarks>
public enum HsmsConnectionMode
{
    /// <summary>The local peer initiates the TCP connection.</summary>
    Active,

    /// <summary>The local peer listens for the TCP connection.</summary>
    Passive,
}
