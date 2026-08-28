using System.Buffers;

namespace SecsFrame.Tests;

public sealed class SessionBoundHsmsFrameCodecTests
{
    [Fact]
    public void Decode_stamps_the_current_transport_session()
    {
        var context = new HsmsTransportSessionContext();
        var sessionId = context.Open();
        var codec = new SessionBoundHsmsFrameCodec(context);
        var bytes = new byte[HsmsMessageHeader.EncodedSize];
        HsmsMessageHeader.CreateData(1, 1, 1, false, 1).Encode(bytes);

        var decoded = codec.Decode(new ReadOnlySequence<byte>(bytes));

        Assert.Equal(sessionId, decoded.SessionId);
        Assert.Equal(1, decoded.Frame.Header.Stream);
    }

    [Fact]
    public void Encode_rejects_an_envelope_from_a_closed_session()
    {
        var context = new HsmsTransportSessionContext();
        var oldSessionId = context.Open();
        Assert.True(context.TryClose(out _));
        context.Open();
        var codec = new SessionBoundHsmsFrameCodec(context);
        var envelope = new HsmsTransportFrame(
            oldSessionId,
            new HsmsFrame(HsmsMessageHeader.CreateData(1, 1, 1, false, 1)));

        var exception = Assert.Throws<HsmsTransportSessionExpiredException>(
            () => codec.Encode(envelope, new TestBufferWriter()));

        Assert.Equal(oldSessionId, exception.SessionId);
    }

    [Fact]
    public void Decode_without_an_active_session_is_rejected()
    {
        var codec = new SessionBoundHsmsFrameCodec(new HsmsTransportSessionContext());
        var bytes = new byte[HsmsMessageHeader.EncodedSize];

        Assert.Throws<HsmsTransportSessionExpiredException>(
            () => codec.Decode(new ReadOnlySequence<byte>(bytes)));
    }
}
