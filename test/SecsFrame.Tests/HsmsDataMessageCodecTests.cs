using System.Buffers;

namespace SecsFrame.Tests;

public sealed class HsmsDataMessageCodecTests
{
    [Fact]
    public void Complete_wire_frame_matches_known_vector_and_round_trips()
    {
        var expected = new HsmsDataMessage(
            0x1234,
            0xAABBCCDD,
            new SecsMessage(
                127,
                byte.MaxValue,
                true,
                SecsItem.List(
                    SecsItem.Ascii("A"),
                    SecsItem.U2(0x1234))));
        var expectedBytes = new byte[]
        {
            0x00, 0x00, 0x00, 0x13,
            0x12, 0x34, 0xFF, 0xFF, 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD,
            0x01, 0x02,
            0x41, 0x01, 0x41,
            0xA9, 0x02, 0x12, 0x34,
        };
        var writer = new TestBufferWriter();
        var codec = new HsmsDataMessageCodec();
        var framer = new HsmsFramer();

        framer.BeginFrame(writer);
        codec.Encode(expected, writer);
        framer.EndFrame(writer);

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);
        Assert.True(framer.TryDecodeFrame(ref buffer, out var payload));
        var decoded = codec.Decode(payload);

        Assert.True(buffer.IsEmpty);
        AssertMessageEqual(expected, decoded);
    }

    [Fact]
    public void Header_only_message_round_trips_without_a_root_item()
    {
        var expected = new HsmsDataMessage(7, uint.MaxValue, new SecsMessage(0, 0));
        var writer = new TestBufferWriter();
        var codec = new HsmsDataMessageCodec();

        codec.Encode(expected, writer);
        var decoded = codec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory));

        Assert.Equal(HsmsMessageHeader.EncodedSize, writer.WrittenCount);
        AssertMessageEqual(expected, decoded);
        Assert.Null(decoded.Message.RootItem);
    }

    [Fact]
    public void Empty_list_is_distinct_from_an_absent_body()
    {
        var expected = new HsmsDataMessage(
            7,
            1,
            new SecsMessage(1, 2, rootItem: SecsItem.List()));
        var writer = new TestBufferWriter();
        var codec = new HsmsDataMessageCodec();

        codec.Encode(expected, writer);
        var decoded = codec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory));

        Assert.Equal(HsmsMessageHeader.EncodedSize + 2, writer.WrittenCount);
        Assert.NotNull(decoded.Message.RootItem);
        Assert.Equal(SecsItemFormat.List, decoded.Message.RootItem.Format);
        Assert.Empty(decoded.Message.RootItem.Items);
    }

    [Fact]
    public void Segmented_payload_decodes_across_every_byte_boundary()
    {
        var expected = new HsmsDataMessage(
            2,
            0x01020304,
            new SecsMessage(6, 11, true, SecsItem.List(SecsItem.Binary(0x80), SecsItem.I4(-1))));
        var writer = new TestBufferWriter();
        var codec = new HsmsDataMessageCodec();
        codec.Encode(expected, writer);

        var decoded = codec.Decode(CreateSegmentedSequence(writer.WrittenSpan.ToArray()));

        AssertMessageEqual(expected, decoded);
    }

    [Fact]
    public void Control_message_is_rejected()
    {
        var bytes = new byte[HsmsMessageHeader.EncodedSize];
        HsmsMessageHeader.CreateControl(HsmsMessageType.LinktestRequest, 1).Encode(bytes);

        Assert.Throws<HsmsProtocolException>(
            () => new HsmsDataMessageCodec().Decode(new ReadOnlySequence<byte>(bytes)));
    }

    [Fact]
    public void Nonzero_presentation_type_is_rejected()
    {
        var bytes = new byte[HsmsMessageHeader.EncodedSize];
        HsmsMessageHeader.CreateData(1, 1, 1, false, 1).Encode(bytes);
        bytes[4] = 1;

        Assert.Throws<HsmsProtocolException>(
            () => new HsmsDataMessageCodec().Decode(new ReadOnlySequence<byte>(bytes)));
    }

    [Fact]
    public void Truncated_header_is_rejected()
    {
        var bytes = new byte[HsmsMessageHeader.EncodedSize - 1];

        Assert.Throws<HsmsProtocolException>(
            () => new HsmsDataMessageCodec().Decode(new ReadOnlySequence<byte>(bytes)));
    }

    [Theory]
    [InlineData(new byte[] { 0x20 })]
    [InlineData(new byte[] { 0x01, 0x00, 0x01, 0x00 })]
    public void Invalid_or_multiple_root_items_are_rejected(byte[] body)
    {
        var bytes = new byte[HsmsMessageHeader.EncodedSize + body.Length];
        HsmsMessageHeader.CreateData(1, 1, 1, false, 1).Encode(bytes);
        body.CopyTo(bytes, HsmsMessageHeader.EncodedSize);

        Assert.Throws<SecsProtocolException>(
            () => new HsmsDataMessageCodec().Decode(new ReadOnlySequence<byte>(bytes)));
    }

    [Fact]
    public void Configured_item_resource_limits_are_honored()
    {
        var unrestricted = new HsmsDataMessageCodec();
        var writer = new TestBufferWriter();
        unrestricted.Encode(
            new HsmsDataMessage(1, 1, new SecsMessage(1, 1, rootItem: SecsItem.List(SecsItem.List()))),
            writer);
        var restricted = new HsmsDataMessageCodec(new SecsItemCodec(maxNestingDepth: 1));

        Assert.Throws<SecsProtocolException>(
            () => restricted.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory)));
    }

    [Fact]
    public void Cancellation_is_observed_for_header_only_messages()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var codec = new HsmsDataMessageCodec();
        var message = new HsmsDataMessage(1, 1, new SecsMessage(1, 1));
        var bytes = new byte[HsmsMessageHeader.EncodedSize];
        HsmsMessageHeader.CreateData(1, 1, 1, false, 1).Encode(bytes);

        Assert.Throws<OperationCanceledException>(
            () => codec.Encode(message, new TestBufferWriter(), cancellation.Token));
        Assert.Throws<OperationCanceledException>(
            () => codec.Decode(new ReadOnlySequence<byte>(bytes), cancellation.Token));
    }

    private static void AssertMessageEqual(HsmsDataMessage expected, HsmsDataMessage actual)
    {
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.SystemBytes, actual.SystemBytes);
        Assert.Equal(expected.Message.Stream, actual.Message.Stream);
        Assert.Equal(expected.Message.Function, actual.Message.Function);
        Assert.Equal(expected.Message.ReplyExpected, actual.Message.ReplyExpected);
        Assert.Equal(expected.Message.RootItem, actual.Message.RootItem);
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
