using System.Buffers;
using System.Buffers.Binary;
using StreamFrame;

namespace SecsFrame.Tests;

public sealed class HsmsFramerTests
{
    [Fact]
    public void Encode_and_decode_round_trip()
    {
        var payload = Enumerable.Range(0, 13).Select(static value => (byte)value).ToArray();
        var writer = new TestBufferWriter();
        var framer = new HsmsFramer();

        framer.EncodeFrame(payload, writer);

        Assert.Equal(payload.Length, BinaryPrimitives.ReadInt32BigEndian(writer.WrittenSpan));
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);
        Assert.True(framer.TryDecodeFrame(ref buffer, out var decoded));
        Assert.Equal(payload, decoded.ToArray());
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Partial_frame_is_retained()
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 10);
        var buffer = new ReadOnlySequence<byte>(bytes);
        var framer = new HsmsFramer();

        Assert.False(framer.TryDecodeFrame(ref buffer, out _));
        Assert.Equal(bytes.Length, buffer.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(65)]
    public void Invalid_declared_length_is_a_protocol_error(int declaredLength)
    {
        var maxPayloadBytes = declaredLength == 65 ? 64 : 64 * 1024 * 1024;
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, declaredLength);
        var framer = new HsmsFramer(maxPayloadBytes);

        Assert.Throws<HsmsProtocolException>(() =>
        {
            var buffer = new ReadOnlySequence<byte>(bytes);
            framer.TryDecodeFrame(ref buffer, out _);
        });
    }

    [Fact]
    public void Streaming_and_regular_encoding_are_identical()
    {
        var payload = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        var regular = new TestBufferWriter();
        var streaming = new TestBufferWriter();
        var framer = new HsmsFramer();

        framer.EncodeFrame(payload, regular);
        framer.BeginFrame(streaming);
        streaming.Write(payload);
        framer.EndFrame(streaming);

        Assert.Equal(regular.WrittenSpan.ToArray(), streaming.WrittenSpan.ToArray());
    }

}
