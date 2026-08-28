using System.IO;

namespace SecsFrame.Gem;

/// <summary>Reports malformed data in a configured foundational GEM dialogue.</summary>
public sealed class GemProtocolException : IOException
{
    /// <summary>Creates a GEM protocol error.</summary>
    public GemProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a GEM protocol error with its parsing cause.</summary>
    public GemProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
