using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using StreamFrame;

namespace SecsFrame;

/// <summary>Strictly encodes and decodes one complete SEMI E5-0725 Item tree.</summary>
public sealed class SecsItemCodec : ICodec<SecsItem>
{
    /// <summary>The default maximum nested Item depth.</summary>
    public const int DefaultMaxNestingDepth = 100;

    /// <summary>The default maximum number of Items in one decoded tree.</summary>
    public const int DefaultMaxItemCount = 1_000_000;

    /// <summary>Creates a strict Item codec with bounded resource usage.</summary>
    /// <param name="maxNestingDepth">Maximum root-inclusive Item depth.</param>
    /// <param name="maxItemCount">Maximum total Item nodes in one tree.</param>
    public SecsItemCodec(
        int maxNestingDepth = DefaultMaxNestingDepth,
        int maxItemCount = DefaultMaxItemCount)
    {
        if (maxNestingDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxNestingDepth), maxNestingDepth, "The maximum depth must be positive.");
        if (maxItemCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItemCount), maxItemCount, "The maximum Item count must be positive.");

        MaxNestingDepth = maxNestingDepth;
        MaxItemCount = maxItemCount;
    }

    /// <summary>Gets the maximum root-inclusive Item depth.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets the maximum total Item nodes in one tree.</summary>
    public int MaxItemCount { get; }

    /// <inheritdoc />
    public SecsItem Decode(in ReadOnlySequence<byte> frame, CancellationToken ct = default)
    {
        if (frame.IsEmpty)
            throw ProtocolError(0, "A SECS-II Item requires a format byte.");

        var remaining = frame;
        var itemCount = 0;
        var item = DecodeItem(ref remaining, frame.Length, depth: 1, ref itemCount, ct);
        if (!remaining.IsEmpty)
        {
            throw ProtocolError(
                frame.Length - remaining.Length,
                $"A complete Item tree was followed by {remaining.Length} trailing byte(s).");
        }

        return item;
    }

    /// <inheritdoc />
    public void Encode(SecsItem message, IBufferWriter<byte> writer, CancellationToken ct = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        var itemCount = 0;
        EncodeItem(message, writer, depth: 1, ref itemCount, ct);
    }

    private SecsItem DecodeItem(
        ref ReadOnlySequence<byte> remaining,
        long totalLength,
        int depth,
        ref int itemCount,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var offset = totalLength - remaining.Length;
        if (depth > MaxNestingDepth)
            throw ProtocolError(offset, $"The Item nesting depth exceeds the configured maximum {MaxNestingDepth}.");
        if (++itemCount > MaxItemCount)
            throw ProtocolError(offset, $"The Item count exceeds the configured maximum {MaxItemCount}.");

        var formatAndLength = ReadByte(ref remaining, totalLength, "format byte");
        var lengthByteCount = formatAndLength & 0x03;
        if (lengthByteCount == 0)
            throw ProtocolError(offset, "The Item length-byte count must be between one and three.");

        var format = DecodeFormat((byte)(formatAndLength >> 2), offset);
        var length = 0;
        for (var index = 0; index < lengthByteCount; index++)
            length = (length << 8) | ReadByte(ref remaining, totalLength, "length field");

        if (format == SecsItemFormat.List)
        {
            if (length > MaxItemCount - itemCount)
                throw ProtocolError(offset, $"The declared List count exceeds the configured maximum {MaxItemCount}.");
            if ((long)length * 2 > remaining.Length)
                throw ProtocolError(offset, $"The declared List count {length} cannot fit in the remaining {remaining.Length} byte(s).");

            var children = new SecsItem[length];
            for (var index = 0; index < children.Length; index++)
                children[index] = DecodeItem(ref remaining, totalLength, depth + 1, ref itemCount, ct);

            return SecsItem.FromDecodedList(children);
        }

        if (remaining.Length < length)
        {
            throw ProtocolError(
                offset,
                $"The Item declares {length} data byte(s), but only {remaining.Length} remain.");
        }

        var payload = remaining.Slice(0, length);
        remaining = remaining.Slice(length);
        return DecodeDataItem(format, payload, offset);
    }

    private static SecsItem DecodeDataItem(SecsItemFormat format, in ReadOnlySequence<byte> payload, long offset)
    {
        var bytes = payload.ToArray();
        ValidateElementAlignment(format, bytes.Length, offset);

        return format switch
        {
            SecsItemFormat.Binary => SecsItem.FromDecodedValues(format, bytes),
            SecsItemFormat.Boolean => SecsItem.FromDecodedValues(format, DecodeBooleans(bytes)),
            SecsItemFormat.Ascii => SecsItem.FromDecodedAscii(DecodeAscii(bytes, offset)),
            SecsItemFormat.Jis8 => SecsItem.FromDecodedValues(format, bytes),
            SecsItemFormat.I8 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(long), BinaryPrimitives.ReadInt64BigEndian)),
            SecsItemFormat.I1 => SecsItem.FromDecodedValues(format, DecodeI1(bytes)),
            SecsItemFormat.I2 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(short), BinaryPrimitives.ReadInt16BigEndian)),
            SecsItemFormat.I4 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(int), BinaryPrimitives.ReadInt32BigEndian)),
            SecsItemFormat.F8 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(double), ReadDoubleBigEndian)),
            SecsItemFormat.F4 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(float), ReadSingleBigEndian)),
            SecsItemFormat.U8 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(ulong), BinaryPrimitives.ReadUInt64BigEndian)),
            SecsItemFormat.U1 => SecsItem.FromDecodedValues(format, bytes),
            SecsItemFormat.U2 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(ushort), BinaryPrimitives.ReadUInt16BigEndian)),
            SecsItemFormat.U4 => SecsItem.FromDecodedValues(format, DecodeValues(bytes, sizeof(uint), BinaryPrimitives.ReadUInt32BigEndian)),
            _ => throw ProtocolError(offset, $"Unsupported data Item format {format}."),
        };
    }

    private static bool[] DecodeBooleans(byte[] bytes)
    {
        var values = new bool[bytes.Length];
        for (var index = 0; index < values.Length; index++)
            values[index] = bytes[index] != 0;
        return values;
    }

    private static string DecodeAscii(byte[] bytes, long offset)
    {
        var characters = new char[bytes.Length];
        for (var index = 0; index < characters.Length; index++)
        {
            if (bytes[index] > 0x7F)
                throw ProtocolError(offset, $"ASCII data byte {index} is outside the seven-bit range.");
            characters[index] = (char)bytes[index];
        }

        return new string(characters);
    }

    private static sbyte[] DecodeI1(byte[] bytes)
    {
        var values = new sbyte[bytes.Length];
        for (var index = 0; index < values.Length; index++)
            values[index] = unchecked((sbyte)bytes[index]);
        return values;
    }

    private static T[] DecodeValues<T>(byte[] bytes, int elementSize, BigEndianReader<T> reader)
    {
        var values = new T[bytes.Length / elementSize];
        for (var index = 0; index < values.Length; index++)
            values[index] = reader(bytes.AsSpan(index * elementSize, elementSize));
        return values;
    }

    private static float ReadSingleBigEndian(ReadOnlySpan<byte> source)
        => new SingleBits { Bits = BinaryPrimitives.ReadInt32BigEndian(source) }.Value;

    private static double ReadDoubleBigEndian(ReadOnlySpan<byte> source)
        => new DoubleBits { Bits = BinaryPrimitives.ReadInt64BigEndian(source) }.Value;

    private void EncodeItem(
        SecsItem item,
        IBufferWriter<byte> writer,
        int depth,
        ref int itemCount,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > MaxNestingDepth)
            throw new ArgumentException($"The Item nesting depth exceeds the configured maximum {MaxNestingDepth}.", nameof(item));
        if (++itemCount > MaxItemCount)
            throw new ArgumentException($"The Item count exceeds the configured maximum {MaxItemCount}.", nameof(item));

        if (item.Format == SecsItemFormat.List)
        {
            WriteHeader(item.Format, item.Count, writer);
            foreach (var child in item.GetListValues())
                EncodeItem(child, writer, depth + 1, ref itemCount, ct);
            return;
        }

        var byteLength = GetDataByteLength(item);
        WriteHeader(item.Format, byteLength, writer);
        EncodeDataItem(item, byteLength, writer);
    }

    private static void EncodeDataItem(SecsItem item, int byteLength, IBufferWriter<byte> writer)
    {
        switch (item.Format)
        {
            case SecsItemFormat.Binary:
            case SecsItemFormat.Jis8:
            case SecsItemFormat.U1:
                writer.Write(item.GetValues<byte>());
                return;
            case SecsItemFormat.Boolean:
                EncodeBooleans(item.GetValues<bool>(), writer);
                return;
            case SecsItemFormat.Ascii:
                EncodeAscii(item.GetString(), writer);
                return;
            case SecsItemFormat.I8:
                EncodeValues(item.GetValues<long>(), byteLength, sizeof(long), writer, BinaryPrimitives.WriteInt64BigEndian);
                return;
            case SecsItemFormat.I1:
                EncodeI1(item.GetValues<sbyte>(), writer);
                return;
            case SecsItemFormat.I2:
                EncodeValues(item.GetValues<short>(), byteLength, sizeof(short), writer, BinaryPrimitives.WriteInt16BigEndian);
                return;
            case SecsItemFormat.I4:
                EncodeValues(item.GetValues<int>(), byteLength, sizeof(int), writer, BinaryPrimitives.WriteInt32BigEndian);
                return;
            case SecsItemFormat.F8:
                EncodeValues(item.GetValues<double>(), byteLength, sizeof(double), writer, WriteDoubleBigEndian);
                return;
            case SecsItemFormat.F4:
                EncodeValues(item.GetValues<float>(), byteLength, sizeof(float), writer, WriteSingleBigEndian);
                return;
            case SecsItemFormat.U8:
                EncodeValues(item.GetValues<ulong>(), byteLength, sizeof(ulong), writer, BinaryPrimitives.WriteUInt64BigEndian);
                return;
            case SecsItemFormat.U2:
                EncodeValues(item.GetValues<ushort>(), byteLength, sizeof(ushort), writer, BinaryPrimitives.WriteUInt16BigEndian);
                return;
            case SecsItemFormat.U4:
                EncodeValues(item.GetValues<uint>(), byteLength, sizeof(uint), writer, BinaryPrimitives.WriteUInt32BigEndian);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item.Format, "Unsupported Item format.");
        }
    }

    private static void EncodeBooleans(ReadOnlySpan<bool> values, IBufferWriter<byte> writer)
    {
        var destination = writer.GetSpan(values.Length).Slice(0, values.Length);
        for (var index = 0; index < values.Length; index++)
            destination[index] = values[index] ? (byte)1 : (byte)0;
        writer.Advance(values.Length);
    }

    private static void EncodeAscii(string text, IBufferWriter<byte> writer)
    {
        var destination = writer.GetSpan(text.Length).Slice(0, text.Length);
        for (var index = 0; index < text.Length; index++)
            destination[index] = (byte)text[index];
        writer.Advance(text.Length);
    }

    private static void EncodeI1(ReadOnlySpan<sbyte> values, IBufferWriter<byte> writer)
    {
        var destination = writer.GetSpan(values.Length).Slice(0, values.Length);
        for (var index = 0; index < values.Length; index++)
            destination[index] = unchecked((byte)values[index]);
        writer.Advance(values.Length);
    }

    private static void EncodeValues<T>(
        ReadOnlySpan<T> values,
        int byteLength,
        int elementSize,
        IBufferWriter<byte> writer,
        BigEndianWriter<T> valueWriter)
    {
        var destination = writer.GetSpan(byteLength).Slice(0, byteLength);
        for (var index = 0; index < values.Length; index++)
            valueWriter(destination.Slice(index * elementSize, elementSize), values[index]);
        writer.Advance(byteLength);
    }

    private static void WriteSingleBigEndian(Span<byte> destination, float value)
        => BinaryPrimitives.WriteInt32BigEndian(destination, new SingleBits { Value = value }.Bits);

    private static void WriteDoubleBigEndian(Span<byte> destination, double value)
        => BinaryPrimitives.WriteInt64BigEndian(destination, new DoubleBits { Value = value }.Bits);

    private static int GetDataByteLength(SecsItem item)
        => item.Format switch
        {
            SecsItemFormat.Binary or SecsItemFormat.Boolean or SecsItemFormat.Ascii or
            SecsItemFormat.Jis8 or SecsItemFormat.I1 or SecsItemFormat.U1 => item.Count,
            SecsItemFormat.I2 or SecsItemFormat.U2 => item.Count * sizeof(short),
            SecsItemFormat.I4 or SecsItemFormat.F4 or SecsItemFormat.U4 => item.Count * sizeof(int),
            SecsItemFormat.I8 or SecsItemFormat.F8 or SecsItemFormat.U8 => item.Count * sizeof(long),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Format, "A List does not have a data byte length."),
        };

    private static void WriteHeader(SecsItemFormat format, int length, IBufferWriter<byte> writer)
    {
        var lengthByteCount = length <= byte.MaxValue ? 1 : length <= ushort.MaxValue ? 2 : 3;
        var destination = writer.GetSpan(lengthByteCount + 1);
        destination[0] = (byte)(((byte)format << 2) | lengthByteCount);

        for (var index = lengthByteCount; index > 0; index--)
        {
            destination[index] = (byte)length;
            length >>= 8;
        }

        writer.Advance(lengthByteCount + 1);
    }

    private static void ValidateElementAlignment(SecsItemFormat format, int byteLength, long offset)
    {
        var elementSize = format switch
        {
            SecsItemFormat.I2 or SecsItemFormat.U2 => sizeof(short),
            SecsItemFormat.I4 or SecsItemFormat.F4 or SecsItemFormat.U4 => sizeof(int),
            SecsItemFormat.I8 or SecsItemFormat.F8 or SecsItemFormat.U8 => sizeof(long),
            _ => 1,
        };

        if (byteLength % elementSize != 0)
        {
            throw ProtocolError(
                offset,
                $"The {format} data length {byteLength} is not divisible by its {elementSize}-byte element width.");
        }
    }

    private static SecsItemFormat DecodeFormat(byte formatCode, long offset)
        => formatCode switch
        {
            (byte)SecsItemFormat.List => SecsItemFormat.List,
            (byte)SecsItemFormat.Binary => SecsItemFormat.Binary,
            (byte)SecsItemFormat.Boolean => SecsItemFormat.Boolean,
            (byte)SecsItemFormat.Ascii => SecsItemFormat.Ascii,
            (byte)SecsItemFormat.Jis8 => SecsItemFormat.Jis8,
            (byte)SecsItemFormat.I8 => SecsItemFormat.I8,
            (byte)SecsItemFormat.I1 => SecsItemFormat.I1,
            (byte)SecsItemFormat.I2 => SecsItemFormat.I2,
            (byte)SecsItemFormat.I4 => SecsItemFormat.I4,
            (byte)SecsItemFormat.F8 => SecsItemFormat.F8,
            (byte)SecsItemFormat.F4 => SecsItemFormat.F4,
            (byte)SecsItemFormat.U8 => SecsItemFormat.U8,
            (byte)SecsItemFormat.U1 => SecsItemFormat.U1,
            (byte)SecsItemFormat.U2 => SecsItemFormat.U2,
            (byte)SecsItemFormat.U4 => SecsItemFormat.U4,
            _ => throw ProtocolError(offset, $"The Item format code 0x{formatCode:X2} is reserved or unsupported."),
        };

    private static byte ReadByte(ref ReadOnlySequence<byte> source, long totalLength, string fieldName)
    {
        if (source.IsEmpty)
            throw ProtocolError(totalLength, $"The Item ended before its {fieldName} was complete.");

        Span<byte> value = stackalloc byte[1];
        source.Slice(0, 1).CopyTo(value);
        source = source.Slice(1);
        return value[0];
    }

    private static SecsProtocolException ProtocolError(long offset, string message)
        => new($"Invalid SECS-II Item at byte offset {offset}: {message}");

    private delegate T BigEndianReader<T>(ReadOnlySpan<byte> source);

    private delegate void BigEndianWriter<T>(Span<byte> destination, T value);

    [StructLayout(LayoutKind.Explicit)]
    private struct SingleBits
    {
        [FieldOffset(0)]
        public int Bits;

        [FieldOffset(0)]
        public float Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DoubleBits
    {
        [FieldOffset(0)]
        public long Bits;

        [FieldOffset(0)]
        public double Value;
    }
}
