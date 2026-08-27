using System.IO;

namespace SecsFrame;

/// <summary>Represents an invalid HSMS wire message or state transition.</summary>
public sealed class HsmsProtocolException : IOException
{
    /// <summary>Creates an HSMS protocol exception.</summary>
    /// <param name="message">A description of the protocol violation.</param>
    public HsmsProtocolException(string message)
        : base(message)
    {
    }
}
