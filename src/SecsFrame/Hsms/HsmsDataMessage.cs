namespace SecsFrame;

/// <summary>
/// A SECS-II message together with the identifiers carried by an HSMS data
/// message header.
/// </summary>
public sealed class HsmsDataMessage
{
    /// <summary>Creates an HSMS data message.</summary>
    /// <param name="sessionId">The HSMS session identifier.</param>
    /// <param name="systemBytes">The transaction identifier.</param>
    /// <param name="message">The dynamic SECS-II message.</param>
    public HsmsDataMessage(ushort sessionId, uint systemBytes, SecsMessage message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        SessionId = sessionId;
        SystemBytes = systemBytes;
    }

    /// <summary>Gets the HSMS session identifier.</summary>
    public ushort SessionId { get; }

    /// <summary>Gets the transaction identifier, commonly called System Bytes.</summary>
    public uint SystemBytes { get; }

    /// <summary>Gets the dynamic SECS-II message.</summary>
    public SecsMessage Message { get; }
}
