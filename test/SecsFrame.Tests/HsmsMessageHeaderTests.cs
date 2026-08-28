using System.Buffers;

namespace SecsFrame.Tests;

public sealed class HsmsMessageHeaderTests
{
    [Fact]
    public void Data_header_round_trips_known_vector()
    {
        var expected = new byte[]
        {
            0x00, 0x01, 0x81, 0x0D, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04,
        };

        var header = HsmsMessageHeader.CreateData(
            sessionId: 1,
            stream: 1,
            function: 13,
            replyExpected: true,
            systemBytes: 0x01020304);

        Span<byte> encoded = stackalloc byte[HsmsMessageHeader.EncodedSize];
        header.Encode(encoded);

        Assert.Equal(expected, encoded.ToArray());
        Assert.Equal(header, HsmsMessageHeader.Decode(encoded));
        Assert.True(header.IsDataMessage);
        Assert.True(header.ReplyExpected);
        Assert.Equal(1, header.Stream);
        Assert.Equal(13, header.Function);
    }

    [Fact]
    public void Control_header_uses_control_session_id_and_status()
    {
        var header = HsmsMessageHeader.CreateControl(
            HsmsMessageType.SelectResponse,
            systemBytes: 42,
            status: 3);

        Span<byte> encoded = stackalloc byte[HsmsMessageHeader.EncodedSize];
        header.Encode(encoded);

        Assert.Equal(ushort.MaxValue, header.SessionId);
        Assert.Equal(3, header.HeaderByte3);
        Assert.Equal((byte)HsmsMessageType.SelectResponse, encoded[5]);
        Assert.Equal(header, HsmsMessageHeader.Decode(encoded));
    }

    [Fact]
    public void Reject_header_round_trips_rejected_SType_and_reason()
    {
        var expected = new byte[]
        {
            0xFF, 0xFF, 0x05, 0x03, 0x00, 0x07, 0x01, 0x02, 0x03, 0x04,
        };
        var header = HsmsMessageHeader.CreateReject(
            systemBytes: 0x01020304,
            rejectedMessageType: (byte)HsmsMessageType.LinktestRequest,
            reason: (byte)HsmsRejectReason.TransactionNotOpen);

        Span<byte> encoded = stackalloc byte[HsmsMessageHeader.EncodedSize];
        header.Encode(encoded);

        Assert.Equal(expected, encoded.ToArray());
        Assert.Equal(header, HsmsMessageHeader.Decode(encoded));
    }

    [Fact]
    public void Stream_number_must_fit_seven_bits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HsmsMessageHeader.CreateData(0, 128, 1, false, 1));
    }
}
