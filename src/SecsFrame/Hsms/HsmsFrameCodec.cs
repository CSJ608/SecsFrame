using System.Buffers;
using StreamFrame;

namespace SecsFrame;

/// <summary>Encodes and decodes the payload selected by <see cref="HsmsFramer"/>.</summary>
public sealed class HsmsFrameCodec : ICodec<HsmsFrame>
{
    /// <inheritdoc />
    public HsmsFrame Decode(in ReadOnlySequence<byte> frame, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (frame.Length < HsmsMessageHeader.EncodedSize)
        {
            throw new HsmsProtocolException(
                $"An HSMS payload requires at least {HsmsMessageHeader.EncodedSize} bytes; received {frame.Length}.");
        }

        Span<byte> headerBytes = stackalloc byte[HsmsMessageHeader.EncodedSize];
        frame.Slice(0, HsmsMessageHeader.EncodedSize).CopyTo(headerBytes);
        var header = HsmsMessageHeader.Decode(headerBytes);
        var bodySequence = frame.Slice(HsmsMessageHeader.EncodedSize);

        if (!header.IsDataMessage && !bodySequence.IsEmpty)
            throw new HsmsProtocolException("HSMS control messages must have a message length of exactly ten bytes.");

        return new HsmsFrame(header, bodySequence.IsEmpty ? default : bodySequence.ToArray());
    }

    /// <inheritdoc />
    public void Encode(HsmsFrame message, IBufferWriter<byte> writer, CancellationToken ct = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        ct.ThrowIfCancellationRequested();

        var header = writer.GetSpan(HsmsMessageHeader.EncodedSize);
        message.Header.Encode(header);
        writer.Advance(HsmsMessageHeader.EncodedSize);
        writer.Write(message.Body.Span);
    }
}
