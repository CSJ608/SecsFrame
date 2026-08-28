using System.Buffers;
using StreamFrame;

namespace SecsFrame;

/// <summary>
/// Strictly encodes and decodes an HSMS data payload containing a ten-byte
/// header and an optional single SECS-II root Item.
/// </summary>
public sealed class HsmsDataMessageCodec : ICodec<HsmsDataMessage>
{
    /// <summary>Creates a data message codec.</summary>
    /// <param name="itemCodec">
    /// The Item codec that supplies resource limits. A default strict codec is
    /// used when omitted.
    /// </param>
    public HsmsDataMessageCodec(SecsItemCodec? itemCodec = null)
    {
        ItemCodec = itemCodec ?? new SecsItemCodec();
    }

    /// <summary>Gets the Item codec used for message bodies.</summary>
    public SecsItemCodec ItemCodec { get; }

    /// <inheritdoc />
    public HsmsDataMessage Decode(in ReadOnlySequence<byte> frame, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (frame.Length < HsmsMessageHeader.EncodedSize)
        {
            throw new HsmsProtocolException(
                $"An HSMS data payload requires at least {HsmsMessageHeader.EncodedSize} bytes; received {frame.Length}.");
        }

        Span<byte> headerBytes = stackalloc byte[HsmsMessageHeader.EncodedSize];
        frame.Slice(0, HsmsMessageHeader.EncodedSize).CopyTo(headerBytes);
        var header = HsmsMessageHeader.Decode(headerBytes);
        if (!header.IsDataMessage)
        {
            throw new HsmsProtocolException(
                $"An HSMS data message requires SType 0; received {(byte)header.MessageType}.");
        }

        if (header.PresentationType != 0)
        {
            throw new HsmsProtocolException(
                $"SECS-II over HSMS requires PType 0; received {header.PresentationType}.");
        }

        var body = frame.Slice(HsmsMessageHeader.EncodedSize);
        var rootItem = body.IsEmpty ? null : ItemCodec.Decode(body, ct);
        var message = new SecsMessage(header.Stream, header.Function, header.ReplyExpected, rootItem);
        return new HsmsDataMessage(header.SessionId, header.SystemBytes, message);
    }

    /// <inheritdoc />
    public void Encode(HsmsDataMessage message, IBufferWriter<byte> writer, CancellationToken ct = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));
        ct.ThrowIfCancellationRequested();

        var header = HsmsMessageHeader.CreateData(
            message.SessionId,
            message.Message.Stream,
            message.Message.Function,
            message.Message.ReplyExpected,
            message.SystemBytes);
        var destination = writer.GetSpan(HsmsMessageHeader.EncodedSize);
        header.Encode(destination);
        writer.Advance(HsmsMessageHeader.EncodedSize);

        if (message.Message.RootItem is not null)
            ItemCodec.Encode(message.Message.RootItem, writer, ct);
    }
}
