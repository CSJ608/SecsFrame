using System.Runtime.InteropServices;

namespace SecsFrame.Gem;

/// <summary>Identifies one configurable GEM Primary/Secondary message pair.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GemMessagePair
{
    /// <summary>Creates a message pair without inferring Function parity.</summary>
    public GemMessagePair(
        byte stream,
        byte primaryFunction,
        byte secondaryFunction)
    {
        if (stream > 0x7F)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stream),
                stream,
                "The stream number must be between 0 and 127.");
        }

        Stream = stream;
        PrimaryFunction = primaryFunction;
        SecondaryFunction = secondaryFunction;
    }

    /// <summary>Gets the Stream shared by the pair.</summary>
    public byte Stream { get; }

    /// <summary>Gets the Primary Function.</summary>
    public byte PrimaryFunction { get; }

    /// <summary>Gets the Secondary Function.</summary>
    public byte SecondaryFunction { get; }
}
