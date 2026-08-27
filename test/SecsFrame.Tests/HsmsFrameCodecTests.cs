using System.Buffers;

namespace SecsFrame.Tests;

public sealed class HsmsFrameCodecTests
{
    [Fact]
    public void Data_frame_round_trips()
    {
        var expected = new HsmsFrame(
            HsmsMessageHeader.CreateData(7, 6, 11, true, 0xAABBCCDD),
            new byte[] { 0x20, 0x01, 0xFF });
        var codec = new HsmsFrameCodec();
        var writer = new TestBufferWriter();

        codec.Encode(expected, writer);
        var decoded = codec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory));

        Assert.Equal(expected.Header, decoded.Header);
        Assert.Equal(expected.Body.ToArray(), decoded.Body.ToArray());
    }

    [Fact]
    public void Control_frame_must_not_contain_a_body()
    {
        var codec = new HsmsFrameCodec();
        var bytes = new byte[HsmsMessageHeader.EncodedSize + 1];
        HsmsMessageHeader.CreateControl(HsmsMessageType.LinktestRequest, 1).Encode(bytes);

        Assert.Throws<HsmsProtocolException>(
            () => codec.Decode(new ReadOnlySequence<byte>(bytes)));
    }

    [Fact]
    public void Control_model_rejects_a_body()
    {
        var header = HsmsMessageHeader.CreateControl(HsmsMessageType.SelectRequest, 1);
        Assert.Throws<ArgumentException>(() => new HsmsFrame(header, new byte[] { 1 }));
    }
}
