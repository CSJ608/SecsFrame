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

    /// <summary>Decodes an already framed HSMS data message.</summary>
    /// <param name="frame">The HSMS frame to decode.</param>
    /// <param name="ct">A token used to cancel Item decoding.</param>
    public HsmsDataMessage Decode(HsmsFrame frame, CancellationToken ct = default)
    {
        if (frame is null)
            throw new ArgumentNullException(nameof(frame));
        ct.ThrowIfCancellationRequested();

        var header = frame.Header;
        ValidateDataHeader(header);
        var body = new ReadOnlySequence<byte>(frame.Body);
        var rootItem = body.IsEmpty ? null : ItemCodec.Decode(body, ct);
        var message = new SecsMessage(
            header.Stream,
            header.Function,
            header.ReplyExpected,
            rootItem);
        return new HsmsDataMessage(
            header.SessionId,
            header.SystemBytes,
            message);
    }

    /// <summary>Encodes a data message as an HSMS frame without its four-byte length prefix.</summary>
    /// <param name="message">The data message to encode.</param>
    /// <param name="ct">A token used to cancel Item encoding.</param>
    public HsmsFrame EncodeFrame(
        HsmsDataMessage message,
        CancellationToken ct = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        ct.ThrowIfCancellationRequested();

        var header = HsmsMessageHeader.CreateData(
            message.SessionId,
            message.Message.Stream,
            message.Message.Function,
            message.Message.ReplyExpected,
            message.SystemBytes);
        if (message.Message.RootItem is null)
            return new HsmsFrame(header);

        var body = new GrowingBufferWriter();
        ItemCodec.Encode(message.Message.RootItem, body, ct);
        return new HsmsFrame(header, body.WrittenMemory.ToArray());
    }

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
        ValidateDataHeader(header);

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

    private static void ValidateDataHeader(HsmsMessageHeader header)
    {
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
    }

    private sealed class GrowingBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[64];

        public int WrittenCount { get; private set; }

        public ReadOnlyMemory<byte> WrittenMemory
            => _buffer.AsMemory(0, WrittenCount);

        public void Advance(int count)
        {
            if (count < 0 || WrittenCount + count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            WrittenCount += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(WrittenCount);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(WrittenCount);
        }

        private void EnsureCapacity(int sizeHint)
        {
            sizeHint = Math.Max(sizeHint, 1);
            if (_buffer.Length - WrittenCount >= sizeHint)
                return;

            Array.Resize(
                ref _buffer,
                Math.Max(_buffer.Length * 2, WrittenCount + sizeHint));
        }
    }
}
