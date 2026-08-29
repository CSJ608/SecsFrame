using SecsFrame.Trace;

namespace SecsFrame.Tests;

public sealed class SecsTraceTransportFaultTests
{
    [Fact]
    public void Redacted_capture_produces_a_deterministic_verifiable_vector()
    {
        var observation = CreateObservation(
            new byte[]
            {
                0x00, 0x00, 0x00, 0x0A,
                0x53, 0x45, 0x43, 0x52, 0x45, 0x54,
            });
        var options = SecsTraceTransportFaultCaptureOptions.RedactedPayload(
            new[] { new SecsTraceByteRedactionRange(4, 6) });
        var timestamp = new DateTimeOffset(
            2026,
            8,
            29,
            13,
            0,
            0,
            TimeSpan.FromHours(8));
        var record = SecsTraceTransportFaultRecord.Create(
            timestamp,
            observation,
            options);
        var codec = new SecsTraceTransportFaultCodec(
            allowPayloadRecords: true);

        var text = codec.Encode(new[] { record });

        Assert.Equal(
            "SecsFrame-TransportFaultTrace/1\n" +
            "TransportFault 2026-08-29T05:00:00.0000000Z IncompleteFrameTimeout Connected 17 RedactedPayload 10 4:6 0000000A000000000000\n",
            text);
        Assert.DoesNotContain("534543524554", text, StringComparison.Ordinal);
        var decoded = Assert.Single(codec.Decode(text));
        AssertRecordEqual(record, decoded);
        Assert.All(
            decoded.Snapshot.Slice(4, 6).ToArray(),
            static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void Default_codec_allows_metadata_only_and_rejects_payload_records()
    {
        var observation = CreateObservation(new byte[] { 0x00, 0x01 });
        var metadata = SecsTraceTransportFaultRecord.Create(
            Epoch,
            observation,
            SecsTraceTransportFaultCaptureOptions.MetadataOnly());
        var raw = SecsTraceTransportFaultRecord.Create(
            Epoch,
            observation,
            SecsTraceTransportFaultCaptureOptions.RawPayload());
        var defaultCodec = new SecsTraceTransportFaultCodec();
        var payloadCodec = new SecsTraceTransportFaultCodec(
            allowPayloadRecords: true);

        var metadataText = defaultCodec.Encode(new[] { metadata });
        var decoded = Assert.Single(defaultCodec.Decode(metadataText));
        var rawText = payloadCodec.Encode(new[] { raw });

        Assert.Equal(
            SecsTraceFaultSampleDataClassification.MetadataOnly,
            decoded.DataClassification);
        Assert.Equal(2, decoded.ObservedSnapshotLength);
        Assert.True(decoded.Snapshot.IsEmpty);
        Assert.Throws<InvalidOperationException>(
            () => defaultCodec.Encode(new[] { raw }));
        Assert.Throws<SecsTraceParseException>(
            () => defaultCodec.Decode(rawText));
    }

    [Fact]
    public void Raw_capture_is_defensively_copied_and_round_trips_when_enabled()
    {
        var snapshot = new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x20 };
        var observation = CreateObservation(snapshot);
        snapshot[0] = 0xFF;
        var record = SecsTraceTransportFaultRecord.Create(
            Epoch,
            observation,
            SecsTraceTransportFaultCaptureOptions.RawPayload());
        var codec = new SecsTraceTransportFaultCodec(
            allowPayloadRecords: true);

        var decoded = Assert.Single(codec.Decode(codec.Encode(new[] { record })));

        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x20 },
            record.Snapshot.ToArray());
        AssertRecordEqual(record, decoded);
    }

    [Fact]
    public void Capture_factory_rejects_null_oversize_and_outside_ranges()
    {
        var observation = CreateObservation(new byte[4]);

        Assert.Throws<ArgumentNullException>(() =>
            SecsTraceTransportFaultRecord.Create(
                Epoch,
                null!,
                SecsTraceTransportFaultCaptureOptions.MetadataOnly()));
        Assert.Throws<ArgumentNullException>(() =>
            SecsTraceTransportFaultRecord.Create(Epoch, observation, null!));
        Assert.Throws<InvalidOperationException>(() =>
            SecsTraceTransportFaultRecord.Create(
                Epoch,
                observation,
                SecsTraceTransportFaultCaptureOptions.RawPayload(
                    maxSnapshotBytes: 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SecsTraceTransportFaultRecord.Create(
                Epoch,
                observation,
                SecsTraceTransportFaultCaptureOptions.RedactedPayload(
                    new[] { new SecsTraceByteRedactionRange(3, 2) })));
    }

    [Fact]
    public void Codec_rejects_malformed_fields_and_unverifiable_redaction()
    {
        const string valid =
            "SecsFrame-TransportFaultTrace/1\n" +
            "TransportFault 1970-01-01T00:00:00.0000000Z IncompleteFrameTimeout Connected 17 RedactedPayload 4 1:2 41000044\n";
        var codec = new SecsTraceTransportFaultCodec(
            allowPayloadRecords: true);

        Assert.Throws<SecsTraceParseException>(() => codec.Decode(string.Empty));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("TransportFaultTrace/1", "TransportFaultTrace/2")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("IncompleteFrameTimeout", "DecodeFailed")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Connected", "connected")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace(" 17 ", " 01 ")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("RedactedPayload", "Payload")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace(" 4 1:2 ", " 04 1:2 ")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("1:2", "01:2")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("1:2 41000044", "3:1,0:1 00000000")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("41000044", "41424344")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("41000044", "4100044")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("41000044", "4100004a")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("TransportFault ", "TransportFault  ")));
    }

    [Fact]
    public void Options_observations_and_records_enforce_data_boundaries()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SecsTraceTransportFaultCaptureOptions.RedactedPayload(null!));
        Assert.Throws<ArgumentException>(() =>
            SecsTraceTransportFaultCaptureOptions.RedactedPayload(
                Array.Empty<SecsTraceByteRedactionRange>()));
        Assert.Throws<ArgumentException>(() =>
            SecsTraceTransportFaultCaptureOptions.RedactedPayload(
                new[]
                {
                    new SecsTraceByteRedactionRange(0, 2),
                    new SecsTraceByteRedactionRange(1, 2),
                }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SecsTraceTransportFaultCaptureOptions.RawPayload(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SecsTraceTransportFaultCaptureOptions.RawPayload(
                HsmsTransportFaultObservation.MaxSnapshotBytes + 1));
        Assert.Throws<ArgumentException>(() =>
            new HsmsTransportFaultObservation(
                HsmsTransportFaultKind.IncompleteFrameTimeout,
                1,
                HsmsSessionState.Connected,
                ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HsmsTransportFaultObservation(
                HsmsTransportFaultKind.IncompleteFrameTimeout,
                1,
                HsmsSessionState.Connected,
                new byte[HsmsTransportFaultObservation.MaxSnapshotBytes + 1]));
        Assert.Throws<ArgumentException>(() =>
            new SecsTraceTransportFaultRecord(
                Epoch,
                HsmsTransportFaultKind.IncompleteFrameTimeout,
                HsmsSessionState.Connected,
                17,
                SecsTraceFaultSampleDataClassification.RedactedPayload,
                2,
                new byte[] { 1, 2 },
                new[] { new SecsTraceByteRedactionRange(0, 1) }));
        Assert.Throws<ArgumentException>(() =>
            new SecsTraceTransportFaultRecord(
                Epoch,
                HsmsTransportFaultKind.IncompleteFrameTimeout,
                HsmsSessionState.Connected,
                17,
                SecsTraceFaultSampleDataClassification.RedactedPayload,
                4,
                new byte[4],
                new[]
                {
                    new SecsTraceByteRedactionRange(2, 1),
                    new SecsTraceByteRedactionRange(0, 1),
                }));
    }

    [Fact]
    public void Codec_enforces_record_text_and_snapshot_resource_limits()
    {
        var observation = CreateObservation(new byte[4]);
        var metadata = SecsTraceTransportFaultRecord.Create(
            Epoch,
            observation,
            SecsTraceTransportFaultCaptureOptions.MetadataOnly());
        var raw = SecsTraceTransportFaultRecord.Create(
            Epoch,
            observation,
            SecsTraceTransportFaultCaptureOptions.RawPayload());
        var records = new[] { metadata, metadata };
        var metadataText = new SecsTraceTransportFaultCodec().Encode(records);
        var rawText = new SecsTraceTransportFaultCodec(
            allowPayloadRecords: true).Encode(new[] { raw });

        Assert.Throws<ArgumentNullException>(() =>
            new SecsTraceTransportFaultCodec().Encode(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new SecsTraceTransportFaultCodec().Decode(null!));
        Assert.Throws<ArgumentException>(() =>
            new SecsTraceTransportFaultCodec().Encode(
                new SecsTraceTransportFaultRecord[] { metadata, null! }));
        Assert.Throws<InvalidOperationException>(() =>
            new SecsTraceTransportFaultCodec(maxRecordCount: 1).Encode(records));
        Assert.Throws<SecsTraceParseException>(() =>
            new SecsTraceTransportFaultCodec(maxRecordCount: 1).Decode(metadataText));
        Assert.Throws<InvalidOperationException>(() =>
            new SecsTraceTransportFaultCodec(maxTextLength: 20)
                .Encode(Array.Empty<SecsTraceTransportFaultRecord>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SecsTraceTransportFaultCodec(maxTextLength: 20)
                .Decode(metadataText));
        Assert.Throws<InvalidOperationException>(() =>
            new SecsTraceTransportFaultCodec(
                allowPayloadRecords: true,
                maxSnapshotBytes: 3).Encode(new[] { raw }));
        Assert.Throws<SecsTraceParseException>(() =>
            new SecsTraceTransportFaultCodec(
                allowPayloadRecords: true,
                maxSnapshotBytes: 3).Decode(rawText));
    }

    private static HsmsTransportFaultObservation CreateObservation(
        byte[] snapshot)
        => new(
            HsmsTransportFaultKind.IncompleteFrameTimeout,
            17,
            HsmsSessionState.Connected,
            snapshot);

    private static DateTimeOffset Epoch
        => new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static void AssertRecordEqual(
        SecsTraceTransportFaultRecord expected,
        SecsTraceTransportFaultRecord actual)
    {
        Assert.Equal(expected.Timestamp, actual.Timestamp);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.TransportSessionId, actual.TransportSessionId);
        Assert.Equal(expected.DataClassification, actual.DataClassification);
        Assert.Equal(
            expected.ObservedSnapshotLength,
            actual.ObservedSnapshotLength);
        Assert.Equal(expected.Snapshot.ToArray(), actual.Snapshot.ToArray());
        Assert.Equal(expected.RedactionRanges, actual.RedactionRanges);
    }
}
