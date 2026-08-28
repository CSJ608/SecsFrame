using System.IO;

namespace SecsFrame;

/// <summary>Represents an invalid SECS-II wire item.</summary>
public sealed class SecsProtocolException : IOException
{
    /// <summary>Creates a SECS-II protocol exception.</summary>
    /// <param name="message">A description of the protocol violation.</param>
    public SecsProtocolException(string message)
        : base(message)
    {
    }
}
