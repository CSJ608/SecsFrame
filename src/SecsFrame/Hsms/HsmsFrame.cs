namespace SecsFrame;

/// <summary>An HSMS header and its optional SECS-II encoded body.</summary>
public sealed class HsmsFrame
{
    /// <summary>Creates an HSMS frame.</summary>
    /// <param name="header">The HSMS header.</param>
    /// <param name="body">The encoded SECS-II body. Control messages must have an empty body.</param>
    public HsmsFrame(HsmsMessageHeader header, ReadOnlyMemory<byte> body = default)
    {
        if (!header.IsDataMessage && !body.IsEmpty)
            throw new ArgumentException("HSMS control messages cannot contain a message body.", nameof(body));

        Header = header;
        Body = body;
    }

    /// <summary>Gets the HSMS message header.</summary>
    public HsmsMessageHeader Header { get; }

    /// <summary>Gets the encoded SECS-II message body.</summary>
    public ReadOnlyMemory<byte> Body { get; }
}
