using System.Buffers;
using System.Buffers.Binary;
using StreamFrame;

namespace SecsFrame;

/// <summary>
/// Strict HSMS framing: a four-byte big-endian message length followed by a payload
/// containing the ten-byte HSMS header and optional SECS-II body.
/// </summary>
public sealed class HsmsFramer : IStreamingFramer
{
    /// <summary>The four-byte message length prefix size.</summary>
    public const int LengthPrefixSize = 4;

    /// <summary>The default maximum HSMS payload size: 64 MiB.</summary>
    public const int DefaultMaxPayloadBytes = 64 * 1024 * 1024;

    /// <summary>Creates a strict HSMS framer.</summary>
    /// <param name="maxPayloadBytes">Maximum payload size, including the ten-byte HSMS header.</param>
    public HsmsFramer(int maxPayloadBytes = DefaultMaxPayloadBytes)
    {
        if (maxPayloadBytes < HsmsMessageHeader.EncodedSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPayloadBytes),
                maxPayloadBytes,
                $"The maximum must be at least {HsmsMessageHeader.EncodedSize} bytes.");
        }

        MaxPayloadBytes = maxPayloadBytes;
    }

    /// <inheritdoc />
    public int MaxPayloadBytes { get; }

    /// <inheritdoc />
    public void EncodeFrame(ReadOnlySpan<byte> payload, IBufferWriter<byte> writer)
    {
        ValidatePayloadLength(payload.Length);
        var prefix = writer.GetSpan(LengthPrefixSize);
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
        writer.Advance(LengthPrefixSize);
        writer.Write(payload);
    }

    /// <inheritdoc />
    public bool TryDecodeFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> payload)
    {
        payload = default;
        if (buffer.Length < LengthPrefixSize)
            return false;

        Span<byte> prefix = stackalloc byte[LengthPrefixSize];
        buffer.Slice(0, LengthPrefixSize).CopyTo(prefix);
        var declaredLength = BinaryPrimitives.ReadUInt32BigEndian(prefix);

        if (declaredLength < HsmsMessageHeader.EncodedSize)
        {
            throw new HsmsProtocolException(
                $"The declared HSMS payload length {declaredLength} is smaller than the ten-byte header.");
        }

        if (declaredLength > MaxPayloadBytes)
        {
            throw new HsmsProtocolException(
                $"The declared HSMS payload length {declaredLength} exceeds the configured maximum {MaxPayloadBytes}.");
        }

        var frameLength = LengthPrefixSize + (long)declaredLength;
        if (buffer.Length < frameLength)
            return false;

        payload = buffer.Slice(LengthPrefixSize, declaredLength);
        buffer = buffer.Slice(frameLength);
        return true;
    }

    /// <inheritdoc />
    public void BeginFrame(IWrittenBufferWriter writer)
    {
        var prefix = writer.GetSpan(LengthPrefixSize);
        prefix.Clear();
        writer.Advance(LengthPrefixSize);
    }

    /// <inheritdoc />
    public void EndFrame(IWrittenBufferWriter writer)
    {
        var payloadLength = writer.WrittenCount - LengthPrefixSize;
        ValidatePayloadLength(payloadLength);
        BinaryPrimitives.WriteInt32BigEndian(writer.WrittenSpan[..LengthPrefixSize], payloadLength);
    }

    private void ValidatePayloadLength(int payloadLength)
    {
        if (payloadLength < HsmsMessageHeader.EncodedSize)
        {
            throw new HsmsProtocolException(
                $"An HSMS payload requires at least {HsmsMessageHeader.EncodedSize} bytes; received {payloadLength}.");
        }

        if (payloadLength > MaxPayloadBytes)
        {
            throw new HsmsProtocolException(
                $"The HSMS payload length {payloadLength} exceeds the configured maximum {MaxPayloadBytes}.");
        }
    }
}
