using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SecsFrame;

/// <summary>The ten-byte HSMS message header.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct HsmsMessageHeader
{
    /// <summary>The encoded header size in bytes.</summary>
    public const int EncodedSize = 10;

    /// <summary>Gets the HSMS session identifier.</summary>
    public ushort SessionId { get; init; }

    /// <summary>Gets header byte 2, containing Stream and W-Bit for data messages.</summary>
    public byte HeaderByte2 { get; init; }

    /// <summary>Gets header byte 3, containing Function for data messages or a control status.</summary>
    public byte HeaderByte3 { get; init; }

    /// <summary>Gets the presentation type (PType).</summary>
    public byte PresentationType { get; init; }

    /// <summary>Gets the HSMS session type (SType).</summary>
    public HsmsMessageType MessageType { get; init; }

    /// <summary>Gets the transaction identifier, commonly called System Bytes.</summary>
    public uint SystemBytes { get; init; }

    /// <summary>Gets whether this is a SECS-II data message.</summary>
    public bool IsDataMessage => MessageType == HsmsMessageType.DataMessage;

    /// <summary>Gets the SECS-II stream number for a data message.</summary>
    public byte Stream => (byte)(HeaderByte2 & 0x7F);

    /// <summary>Gets whether a secondary reply is expected for a data message.</summary>
    public bool ReplyExpected => (HeaderByte2 & 0x80) != 0;

    /// <summary>Gets the SECS-II function number for a data message.</summary>
    public byte Function => HeaderByte3;

    /// <summary>Creates a SECS-II data message header.</summary>
    public static HsmsMessageHeader CreateData(
        ushort sessionId,
        byte stream,
        byte function,
        bool replyExpected,
        uint systemBytes)
    {
        if (stream > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(stream), stream, "The stream number must be between 0 and 127.");

        return new HsmsMessageHeader
        {
            SessionId = sessionId,
            HeaderByte2 = (byte)(stream | (replyExpected ? 0x80 : 0)),
            HeaderByte3 = function,
            PresentationType = 0,
            MessageType = HsmsMessageType.DataMessage,
            SystemBytes = systemBytes,
        };
    }

    /// <summary>Creates an HSMS control message header.</summary>
    /// <param name="messageType">The control message type.</param>
    /// <param name="systemBytes">The transaction identifier.</param>
    /// <param name="status">An optional response status encoded in header byte 3.</param>
    public static HsmsMessageHeader CreateControl(
        HsmsMessageType messageType,
        uint systemBytes,
        byte status = 0)
    {
        if (messageType == HsmsMessageType.DataMessage)
            throw new ArgumentOutOfRangeException(nameof(messageType), messageType, "Use CreateData for data messages.");

        return new HsmsMessageHeader
        {
            SessionId = ushort.MaxValue,
            HeaderByte2 = 0,
            HeaderByte3 = status,
            PresentationType = 0,
            MessageType = messageType,
            SystemBytes = systemBytes,
        };
    }

    /// <summary>Decodes an HSMS header from exactly ten or more bytes.</summary>
    /// <param name="source">The source bytes.</param>
    public static HsmsMessageHeader Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < EncodedSize)
            throw new HsmsProtocolException($"An HSMS header requires {EncodedSize} bytes; received {source.Length}.");

        return new HsmsMessageHeader
        {
            SessionId = BinaryPrimitives.ReadUInt16BigEndian(source),
            HeaderByte2 = source[2],
            HeaderByte3 = source[3],
            PresentationType = source[4],
            MessageType = (HsmsMessageType)source[5],
            SystemBytes = BinaryPrimitives.ReadUInt32BigEndian(source[6..]),
        };
    }

    /// <summary>Encodes this header into a ten-byte destination.</summary>
    /// <param name="destination">The destination span.</param>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < EncodedSize)
            throw new ArgumentException($"The destination must contain at least {EncodedSize} bytes.", nameof(destination));

        BinaryPrimitives.WriteUInt16BigEndian(destination, SessionId);
        destination[2] = HeaderByte2;
        destination[3] = HeaderByte3;
        destination[4] = PresentationType;
        destination[5] = (byte)MessageType;
        BinaryPrimitives.WriteUInt32BigEndian(destination[6..], SystemBytes);
    }
}
