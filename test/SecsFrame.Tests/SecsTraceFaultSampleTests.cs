using System.Text;
using SecsFrame.Trace;

namespace SecsFrame.Tests;

public sealed class SecsTraceFaultSampleTests
{
    [Fact]
    public void Redacted_capture_produces_a_deterministic_verifiable_vector()
    {
        var body = Encoding.ASCII.GetBytes("A-SECRET-Z");
        var connectionEvent = CreateDecodeEvent(body);
        var options = SecsTraceFaultSampleCaptureOptions.RedactedPayload(
            new[] { new SecsTraceByteRedactionRange(2, 6) });
        var timestamp = new DateTimeOffset(
            2026,
            8,
            29,
            13,
            0,
            0,
            TimeSpan.FromHours(8));
        var record = SecsTraceFaultSampleRecord.Create(
            timestamp,
            connectionEvent,
            options);
        var codec = new SecsTraceFaultSampleCodec(
            allowPayloadRecords: true);

        var text = codec.Encode(new[] { record });

        Assert.Equal(
            "SecsFrame-FaultSampleTrace/1\n" +
            "Fault 2026-08-29T05:00:00.0000000Z DataMessageDecodeFailed Selected RedactedPayload 23 0x81 0x02 0x00 0x00 0xAABBCCDD 10 2:6 412D0000000000002D5A\n",
            text);
        Assert.DoesNotContain("534543524554", text, StringComparison.Ordinal);
        var decoded = Assert.Single(codec.Decode(text));
        AssertRecordEqual(record, decoded);
        Assert.All(
            decoded.Body.Slice(2, 6).ToArray(),
            static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void Default_codec_allows_metadata_only_and_rejects_payload_records()
    {
        var connectionEvent = CreateDecodeEvent(
            Encoding.ASCII.GetBytes("SECRET"));
        var metadata = SecsTraceFaultSampleRecord.Create(
            Epoch,
            connectionEvent,
            SecsTraceFaultSampleCaptureOptions.MetadataOnly());
        var raw = SecsTraceFaultSampleRecord.Create(
            Epoch,
            connectionEvent,
            SecsTraceFaultSampleCaptureOptions.RawPayload());
        var defaultCodec = new SecsTraceFaultSampleCodec();
        var payloadCodec = new SecsTraceFaultSampleCodec(
            allowPayloadRecords: true);

        var metadataText = defaultCodec.Encode(new[] { metadata });
        var decoded = Assert.Single(defaultCodec.Decode(metadataText));
        var rawText = payloadCodec.Encode(new[] { raw });

        Assert.Equal(
            SecsTraceFaultSampleDataClassification.MetadataOnly,
            decoded.DataClassification);
        Assert.Equal(6, decoded.OriginalBodyLength);
        Assert.True(decoded.Body.IsEmpty);
        Assert.Throws<InvalidOperationException>(
            () => defaultCodec.Encode(new[] { raw }));
        Assert.Throws<SecsTraceParseException>(
            () => defaultCodec.Decode(rawText));
    }

    [Fact]
    public void Raw_capture_is_defensively_copied_and_round_trips_when_enabled()
    {
        var body = new byte[] { 0x20, 0x01, 0xFF };
        var record = SecsTraceFaultSampleRecord.Create(
            Epoch,
            CreateDecodeEvent(body),
            SecsTraceFaultSampleCaptureOptions.RawPayload());
        body[0] = 0;
        var codec = new SecsTraceFaultSampleCodec(
            allowPayloadRecords: true);

        var decoded = Assert.Single(codec.Decode(codec.Encode(new[] { record })));

        Assert.Equal(new byte[] { 0x20, 0x01, 0xFF }, record.Body.ToArray());
        AssertRecordEqual(record, decoded);
    }

    [Fact]
    public void Capture_factory_rejects_wrong_events_oversize_and_outside_ranges()
    {
        var decodeEvent = CreateDecodeEvent(new byte[4]);
        var controlEvent = HsmsConnectionEvent.ControlMessageReceived(
            HsmsSessionState.Selected,
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.LinktestRequest,
                    1)));

        Assert.Throws<ArgumentNullException>(() =>
            SecsTraceFaultSampleRecord.Create(
                Epoch,
                null!,
                SecsTraceFaultSampleCaptureOptions.MetadataOnly()));
        Assert.Throws<ArgumentNullException>(() =>
            SecsTraceFaultSampleRecord.Create(Epoch, decodeEvent, null!));
        Assert.Throws<ArgumentException>(() =>
            SecsTraceFaultSampleRecord.Create(
                Epoch,
                controlEvent,
                SecsTraceFaultSampleCaptureOptions.MetadataOnly()));
        Assert.Throws<InvalidOperationException>(() =>
            SecsTraceFaultSampleRecord.Create(
                Epoch,
                decodeEvent,
                SecsTraceFaultSampleCaptureOptions.RawPayload(
                    maxBodyBytes: 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SecsTraceFaultSampleRecord.Create(
                Epoch,
                decodeEvent,
                SecsTraceFaultSampleCaptureOptions.RedactedPayload(
                    new[] { new SecsTraceByteRedactionRange(3, 2) })));
    }

    [Fact]
    public void Codec_rejects_malformed_fields_and_unverifiable_redaction()
    {
        const string valid =
            "SecsFrame-FaultSampleTrace/1\n" +
            "Fault 1970-01-01T00:00:00.0000000Z DataMessageDecodeFailed Selected RedactedPayload 23 0x81 0x02 0x00 0x00 0xAABBCCDD 4 1:2 41000044\n";
        var codec = new SecsTraceFaultSampleCodec(
            allowPayloadRecords: true);

        Assert.Throws<SecsTraceParseException>(() => codec.Decode(string.Empty));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("FaultSampleTrace/1", "FaultSampleTrace/2")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("DataMessageDecodeFailed", "CodecFailure")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Selected", "selected")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("RedactedPayload", "Payload")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x81", "0x8")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x00 0xAABB", "0x07 0xAABB")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("1:2", "01:2")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("1:2 41000044", "3:1,0:1 00000000")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("41000044", "41424344")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("41000044", "4100044")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("41000044", "4100004a")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Fault ", "Fault  ")));
    }

    [Fact]
    public void Options_and_records_enforce_classification_boundaries()
    {
        var header = HsmsMessageHeader.CreateData(
            23,
            1,
            2,
            true,
            0xAABBCCDD);

        Assert.Throws<ArgumentNullException>(() =>
            SecsTraceFaultSampleCaptureOptions.RedactedPayload(null!));
        Assert.Throws<ArgumentException>(() =>
            SecsTraceFaultSampleCaptureOptions.RedactedPayload(
                Array.Empty<SecsTraceByteRedactionRange>()));
        Assert.Throws<ArgumentException>(() =>
            SecsTraceFaultSampleCaptureOptions.RedactedPayload(
                new[]
                {
                    new SecsTraceByteRedactionRange(0, 2),
                    new SecsTraceByteRedactionRange(1, 2),
                }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SecsTraceFaultSampleCaptureOptions.RawPayload(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SecsTraceByteRedactionRange(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SecsTraceByteRedactionRange(0, 0));
        Assert.Throws<ArgumentException>(() =>
            new SecsTraceFaultSampleRecord(
                Epoch,
                HsmsSessionState.Selected,
                SecsTraceFaultSampleDataClassification.RedactedPayload,
                header,
                2,
                new byte[] { 1, 2 },
                new[] { new SecsTraceByteRedactionRange(0, 1) }));
    }

    [Fact]
    public void Codec_enforces_record_text_and_body_resource_limits()
    {
        var metadata = SecsTraceFaultSampleRecord.Create(
            Epoch,
            CreateDecodeEvent(new byte[4]),
            SecsTraceFaultSampleCaptureOptions.MetadataOnly());
        var raw = SecsTraceFaultSampleRecord.Create(
            Epoch,
            CreateDecodeEvent(new byte[4]),
            SecsTraceFaultSampleCaptureOptions.RawPayload());
        var records = new[] { metadata, metadata };
        var metadataText = new SecsTraceFaultSampleCodec().Encode(records);
        var rawText = new SecsTraceFaultSampleCodec(
            allowPayloadRecords: true).Encode(new[] { raw });

        Assert.Throws<ArgumentNullException>(() =>
            new SecsTraceFaultSampleCodec().Encode(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new SecsTraceFaultSampleCodec().Decode(null!));
        Assert.Throws<ArgumentException>(() =>
            new SecsTraceFaultSampleCodec().Encode(
                new SecsTraceFaultSampleRecord[] { metadata, null! }));
        Assert.Throws<InvalidOperationException>(() =>
            new SecsTraceFaultSampleCodec(maxRecordCount: 1).Encode(records));
        Assert.Throws<SecsTraceParseException>(() =>
            new SecsTraceFaultSampleCodec(maxRecordCount: 1).Decode(metadataText));
        Assert.Throws<InvalidOperationException>(() =>
            new SecsTraceFaultSampleCodec(maxTextLength: 20)
                .Encode(Array.Empty<SecsTraceFaultSampleRecord>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SecsTraceFaultSampleCodec(maxTextLength: 20).Decode(metadataText));
        Assert.Throws<InvalidOperationException>(() =>
            new SecsTraceFaultSampleCodec(
                allowPayloadRecords: true,
                maxBodyBytes: 3).Encode(new[] { raw }));
        Assert.Throws<SecsTraceParseException>(() =>
            new SecsTraceFaultSampleCodec(
                allowPayloadRecords: true,
                maxBodyBytes: 3).Decode(rawText));
    }

    private static HsmsConnectionEvent CreateDecodeEvent(byte[] body)
        => HsmsConnectionEvent.DataMessageDecodeFailed(
            new HsmsFrame(
                HsmsMessageHeader.CreateData(
                    23,
                    1,
                    2,
                    true,
                    0xAABBCCDD),
                body),
            new SecsProtocolException("Invalid SECS-II body."));

    private static DateTimeOffset Epoch
        => new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static void AssertRecordEqual(
        SecsTraceFaultSampleRecord expected,
        SecsTraceFaultSampleRecord actual)
    {
        Assert.Equal(expected.Timestamp, actual.Timestamp);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.DataClassification, actual.DataClassification);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.OriginalBodyLength, actual.OriginalBodyLength);
        Assert.Equal(expected.Body.ToArray(), actual.Body.ToArray());
        Assert.Equal(expected.RedactionRanges, actual.RedactionRanges);
    }
}
