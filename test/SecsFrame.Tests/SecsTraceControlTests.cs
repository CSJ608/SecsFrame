using SecsFrame.Trace;

namespace SecsFrame.Tests;

public sealed class SecsTraceControlTests
{
    [Fact]
    public void Codec_produces_a_deterministic_unclaimed_reject_vector()
    {
        var frame = new HsmsFrame(HsmsMessageHeader.CreateReject(
            systemBytes: 0x10203040,
            rejectedMessageType: (byte)HsmsMessageType.LinktestRequest,
            reason: 0x03));
        var connectionEvent = HsmsConnectionEvent.ControlMessageReceived(
            HsmsSessionState.Selected,
            frame);
        var timestamp = new DateTimeOffset(2026, 8, 29, 13, 0, 0, TimeSpan.FromHours(8));
        var record = SecsTraceControlRecord.CreateReceived(timestamp, connectionEvent);
        var codec = new SecsTraceControlCodec();

        var text = codec.Encode(new[] { record });

        Assert.Equal(
            "SecsFrame-ControlTrace/1\n" +
            "Control 2026-08-29T05:00:00.0000000Z Received Selected 65535 0x05 0x03 0x00 0x07 0x10203040\n",
            text);
        AssertRecordEqual(record, Assert.Single(codec.Decode(text)));
    }

    [Fact]
    public void Codec_preserves_unknown_nonzero_stype_and_sent_direction()
    {
        var record = new SecsTraceControlRecord(
            Epoch,
            SecsTraceDirection.Sent,
            HsmsSessionState.Connected,
            protocolSessionId: 0x1234,
            headerByte2: 0x56,
            headerByte3: 0x78,
            presentationType: 0x9A,
            messageType: 0x7F,
            systemBytes: 0xABCDEF01);
        var codec = new SecsTraceControlCodec();

        var decoded = Assert.Single(codec.Decode(codec.Encode(new[] { record })));

        AssertRecordEqual(record, decoded);
    }

    [Fact]
    public void Factories_reject_data_frames_and_non_control_events()
    {
        var dataFrame = new HsmsFrame(
            HsmsMessageHeader.CreateData(10, 1, 1, false, 1));
        var decodeEvent = HsmsConnectionEvent.DataMessageDecodeFailed(
            dataFrame,
            new SecsProtocolException("Invalid data."));

        Assert.Throws<ArgumentException>(() => SecsTraceControlRecord.Create(
            Epoch,
            SecsTraceDirection.Received,
            HsmsSessionState.Selected,
            dataFrame));
        Assert.Throws<ArgumentException>(() => SecsTraceControlRecord.CreateReceived(Epoch, decodeEvent));
        Assert.Throws<ArgumentNullException>(() => SecsTraceControlRecord.Create(
            Epoch,
            SecsTraceDirection.Received,
            HsmsSessionState.Selected,
            null!));
        Assert.Throws<ArgumentNullException>(() => SecsTraceControlRecord.CreateReceived(Epoch, null!));
    }

    [Fact]
    public void Codec_rejects_malformed_fields_data_stype_and_spacing()
    {
        const string valid =
            "SecsFrame-ControlTrace/1\n" +
            "Control 1970-01-01T00:00:00.0000000Z Received Selected 65535 0x05 0x03 0x00 0x07 0x10203040\n";
        var codec = new SecsTraceControlCodec();

        Assert.Throws<SecsTraceParseException>(() => codec.Decode(string.Empty));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("ControlTrace/1", "ControlTrace/2")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Received", "Inbound")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Selected", "selected")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x05", "0x5")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x03", "0x0a")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x07", "0x00")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Control ", "Control  ")));
    }

    [Fact]
    public void Codec_and_record_enforce_resource_and_value_boundaries()
    {
        var record = new SecsTraceControlRecord(
            Epoch,
            SecsTraceDirection.Received,
            HsmsSessionState.Selected,
            ushort.MaxValue,
            0,
            0,
            0,
            (byte)HsmsMessageType.LinktestRequest,
            1);
        var records = new[] { record, record };
        var codec = new SecsTraceControlCodec();
        var text = codec.Encode(records);

        Assert.Throws<ArgumentNullException>(() => codec.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => codec.Decode(null!));
        Assert.Throws<ArgumentException>(() => codec.Encode(new SecsTraceControlRecord[] { record, null! }));
        Assert.Throws<InvalidOperationException>(() => new SecsTraceControlCodec(maxRecordCount: 1).Encode(records));
        Assert.Throws<SecsTraceParseException>(() => new SecsTraceControlCodec(maxRecordCount: 1).Decode(text));
        Assert.Throws<InvalidOperationException>(() => new SecsTraceControlCodec(maxTextLength: 20).Encode(Array.Empty<SecsTraceControlRecord>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceControlCodec(maxTextLength: 20).Decode(text));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceControlCodec(maxRecordCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceControlCodec(maxTextLength: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceControlRecord(
            Epoch,
            (SecsTraceDirection)int.MaxValue,
            HsmsSessionState.Selected,
            ushort.MaxValue,
            0,
            0,
            0,
            1,
            1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceControlRecord(
            Epoch,
            SecsTraceDirection.Received,
            HsmsSessionState.Selected,
            ushort.MaxValue,
            0,
            0,
            0,
            0,
            1));
    }

    private static DateTimeOffset Epoch
        => new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static void AssertRecordEqual(
        SecsTraceControlRecord expected,
        SecsTraceControlRecord actual)
    {
        Assert.Equal(expected.Timestamp, actual.Timestamp);
        Assert.Equal(expected.Direction, actual.Direction);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.ProtocolSessionId, actual.ProtocolSessionId);
        Assert.Equal(expected.HeaderByte2, actual.HeaderByte2);
        Assert.Equal(expected.HeaderByte3, actual.HeaderByte3);
        Assert.Equal(expected.PresentationType, actual.PresentationType);
        Assert.Equal(expected.MessageType, actual.MessageType);
        Assert.Equal(expected.SystemBytes, actual.SystemBytes);
    }
}
