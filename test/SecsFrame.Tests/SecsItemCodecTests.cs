using System.Buffers;

namespace SecsFrame.Tests;

public sealed class SecsItemCodecTests
{
    public static TheoryData<SecsItem, byte[]> KnownVectors => new()
    {
        { SecsItem.List(), new byte[] { 0x01, 0x00 } },
        { SecsItem.Binary(0x00, 0x80, 0xFF), new byte[] { 0x21, 0x03, 0x00, 0x80, 0xFF } },
        { SecsItem.Boolean(false, true, true), new byte[] { 0x25, 0x03, 0x00, 0x01, 0x01 } },
        { SecsItem.Ascii("ABC"), new byte[] { 0x41, 0x03, 0x41, 0x42, 0x43 } },
        { SecsItem.Jis8(0xA1, 0xFE), new byte[] { 0x45, 0x02, 0xA1, 0xFE } },
        {
            SecsItem.I8(long.MinValue, long.MaxValue),
            new byte[]
            {
                0x61, 0x10,
                0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            }
        },
        { SecsItem.I1(sbyte.MinValue, sbyte.MaxValue), new byte[] { 0x65, 0x02, 0x80, 0x7F } },
        { SecsItem.I2(short.MinValue, short.MaxValue), new byte[] { 0x69, 0x04, 0x80, 0x00, 0x7F, 0xFF } },
        {
            SecsItem.I4(int.MinValue, int.MaxValue),
            new byte[] { 0x71, 0x08, 0x80, 0x00, 0x00, 0x00, 0x7F, 0xFF, 0xFF, 0xFF }
        },
        {
            SecsItem.F8(-1d, 1.5d),
            new byte[]
            {
                0x81, 0x10,
                0xBF, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x3F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            }
        },
        {
            SecsItem.F4(-1f, 1.5f),
            new byte[] { 0x91, 0x08, 0xBF, 0x80, 0x00, 0x00, 0x3F, 0xC0, 0x00, 0x00 }
        },
        {
            SecsItem.U8(ulong.MinValue, ulong.MaxValue),
            new byte[]
            {
                0xA1, 0x10,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            }
        },
        { SecsItem.U1(byte.MinValue, byte.MaxValue), new byte[] { 0xA5, 0x02, 0x00, 0xFF } },
        { SecsItem.U2(ushort.MinValue, ushort.MaxValue), new byte[] { 0xA9, 0x04, 0x00, 0x00, 0xFF, 0xFF } },
        {
            SecsItem.U4(uint.MinValue, uint.MaxValue),
            new byte[] { 0xB1, 0x08, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF }
        },
    };

    public static TheoryData<byte[]> InvalidVectors => new()
    {
        Array.Empty<byte>(),
        new byte[] { 0x20 },
        new byte[] { 0x05, 0x00 },
        new byte[] { 0x22, 0x01 },
        new byte[] { 0x21, 0x02, 0xAA },
        new byte[] { 0x69, 0x01, 0x00 },
        new byte[] { 0x41, 0x01, 0x80 },
        new byte[] { 0x01, 0x02, 0x01, 0x00 },
        new byte[] { 0x01, 0x00, 0x01, 0x00 },
    };

    [Theory]
    [MemberData(nameof(KnownVectors))]
    public void All_item_formats_match_known_vectors(SecsItem item, byte[] expected)
    {
        var codec = new SecsItemCodec();
        var writer = new TestBufferWriter();

        codec.Encode(item, writer);
        var decoded = codec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory));

        Assert.Equal(expected, writer.WrittenSpan.ToArray());
        Assert.Equal(item, decoded);
    }

    [Fact]
    public void Nested_list_matches_known_vector()
    {
        var item = SecsItem.List(
            SecsItem.Ascii("A"),
            SecsItem.List(SecsItem.U2(0x1234), SecsItem.List()),
            SecsItem.Boolean(false, true));
        var expected = new byte[]
        {
            0x01, 0x03,
            0x41, 0x01, 0x41,
            0x01, 0x02,
            0xA9, 0x02, 0x12, 0x34,
            0x01, 0x00,
            0x25, 0x02, 0x00, 0x01,
        };
        var codec = new SecsItemCodec();
        var writer = new TestBufferWriter();

        codec.Encode(item, writer);

        Assert.Equal(expected, writer.WrittenSpan.ToArray());
        Assert.Equal(item, codec.Decode(new ReadOnlySequence<byte>(expected)));
    }

    [Theory]
    [InlineData(255, 1)]
    [InlineData(256, 2)]
    [InlineData(65_535, 2)]
    [InlineData(65_536, 3)]
    public void Length_header_uses_the_shortest_representation(int length, int expectedLengthByteCount)
    {
        var codec = new SecsItemCodec();
        var writer = new TestBufferWriter();

        codec.Encode(SecsItem.Binary(new byte[length]), writer);

        Assert.Equal((byte)(0x20 | expectedLengthByteCount), writer.WrittenSpan[0]);
        var decoded = codec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.Equal(length, decoded.Count);
    }

    [Fact]
    public void Decoder_accepts_a_non_minimal_legal_length_field_and_reencodes_canonically()
    {
        var codec = new SecsItemCodec();
        var decoded = codec.Decode(new ReadOnlySequence<byte>(new byte[] { 0x22, 0x00, 0x01, 0xAA }));
        var writer = new TestBufferWriter();

        codec.Encode(decoded, writer);

        Assert.Equal(new byte[] { 0x21, 0x01, 0xAA }, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Decoder_handles_empty_and_single_byte_sequence_segments()
    {
        var expected = SecsItem.List(
            SecsItem.Ascii("ABC"),
            SecsItem.I4(int.MinValue, int.MaxValue));
        var writer = new TestBufferWriter();
        var codec = new SecsItemCodec();
        codec.Encode(expected, writer);

        var sequence = CreateSegmentedSequence(writer.WrittenSpan.ToArray());
        var decoded = codec.Decode(sequence);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void Any_nonzero_boolean_byte_decodes_as_true_and_encodes_canonically()
    {
        var codec = new SecsItemCodec();
        var decoded = codec.Decode(new ReadOnlySequence<byte>(new byte[] { 0x25, 0x01, 0xFF }));
        var writer = new TestBufferWriter();

        codec.Encode(decoded, writer);

        Assert.True(decoded.GetValues<bool>()[0]);
        Assert.Equal(new byte[] { 0x25, 0x01, 0x01 }, writer.WrittenSpan.ToArray());
    }

    [Theory]
    [MemberData(nameof(InvalidVectors))]
    public void Invalid_input_is_rejected(byte[] bytes)
    {
        var codec = new SecsItemCodec();

        Assert.Throws<SecsProtocolException>(() => codec.Decode(new ReadOnlySequence<byte>(bytes)));
    }

    [Fact]
    public void Nesting_depth_is_bounded_for_encoding_and_decoding()
    {
        var item = SecsItem.List(SecsItem.List(SecsItem.List(SecsItem.List())));
        var unrestrictedWriter = new TestBufferWriter();
        new SecsItemCodec().Encode(item, unrestrictedWriter);
        var restricted = new SecsItemCodec(maxNestingDepth: 3);

        Assert.Throws<ArgumentException>(() => restricted.Encode(item, new TestBufferWriter()));
        Assert.Throws<SecsProtocolException>(
            () => restricted.Decode(new ReadOnlySequence<byte>(unrestrictedWriter.WrittenMemory)));
    }

    [Fact]
    public void Total_item_count_is_bounded_for_encoding_and_decoding()
    {
        var item = SecsItem.List(SecsItem.List(), SecsItem.List(), SecsItem.List());
        var unrestrictedWriter = new TestBufferWriter();
        new SecsItemCodec().Encode(item, unrestrictedWriter);
        var restricted = new SecsItemCodec(maxItemCount: 3);

        Assert.Throws<ArgumentException>(() => restricted.Encode(item, new TestBufferWriter()));
        Assert.Throws<SecsProtocolException>(
            () => restricted.Decode(new ReadOnlySequence<byte>(unrestrictedWriter.WrittenMemory)));
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var codec = new SecsItemCodec();

        Assert.Throws<OperationCanceledException>(
            () => codec.Encode(SecsItem.List(), new TestBufferWriter(), cancellation.Token));
        Assert.Throws<OperationCanceledException>(
            () => codec.Decode(new ReadOnlySequence<byte>(new byte[] { 0x01, 0x00 }), cancellation.Token));
    }

    private static ReadOnlySequence<byte> CreateSegmentedSequence(byte[] bytes)
    {
        var first = new BufferSegment(Array.Empty<byte>());
        var last = first;
        foreach (var value in bytes)
            last = last.Append(new[] { value });

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(Memory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(Memory<byte> memory)
        {
            var segment = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length,
            };
            Next = segment;
            return segment;
        }
    }
}
