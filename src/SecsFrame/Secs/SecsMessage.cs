namespace SecsFrame;

/// <summary>
/// An immutable, dynamically shaped SECS-II message without transport-specific
/// session or transaction identifiers.
/// </summary>
public sealed class SecsMessage
{
    /// <summary>Creates a SECS-II message.</summary>
    /// <param name="stream">The stream number, from 0 through 127.</param>
    /// <param name="function">The function number.</param>
    /// <param name="replyExpected">Whether the W-Bit requests a secondary reply.</param>
    /// <param name="rootItem">The optional single root Item.</param>
    public SecsMessage(
        byte stream,
        byte function,
        bool replyExpected = false,
        SecsItem? rootItem = null)
    {
        if (stream > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(stream), stream, "The stream number must be between 0 and 127.");

        Stream = stream;
        Function = function;
        ReplyExpected = replyExpected;
        RootItem = rootItem;
    }

    /// <summary>Gets the stream number.</summary>
    public byte Stream { get; }

    /// <summary>Gets the function number.</summary>
    public byte Function { get; }

    /// <summary>Gets whether the W-Bit requests a secondary reply.</summary>
    public bool ReplyExpected { get; }

    /// <summary>Gets the optional single root Item.</summary>
    public SecsItem? RootItem { get; }
}
