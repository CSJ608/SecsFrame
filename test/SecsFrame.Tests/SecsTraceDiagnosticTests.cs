using System.Text;
using SecsFrame.Trace;

namespace SecsFrame.Tests;

public sealed class SecsTraceDiagnosticTests
{
    [Fact]
    public void Codec_produces_a_deterministic_restricted_diagnostic_vector()
    {
        var diagnostic = HsmsDiagnostic.Classify(
            new HsmsDataTransactionTimeoutException(new HsmsDataMessage(
                sessionId: 10,
                systemBytes: 0x10203040,
                new SecsMessage(6, 11, true))),
            HsmsSessionState.Selected)!;
        var timestamp = new DateTimeOffset(2026, 8, 29, 13, 0, 0, TimeSpan.FromHours(8));
        var record = SecsTraceDiagnosticRecord.Create(timestamp, diagnostic);
        var codec = new SecsTraceDiagnosticCodec();

        var text = codec.Encode(new[] { record });

        Assert.Equal(
            "SecsFrame-DiagnosticTrace/1\n" +
            "Diagnostic 2026-08-29T05:00:00.0000000Z T3Timeout Transaction WaitForSecondary Selected T3 10 0x10203040 - -\n",
            text);
        var decoded = Assert.Single(codec.Decode(text));
        AssertRecordEqual(record, decoded);
    }

    [Fact]
    public void Codec_round_trips_optional_peer_fields_and_empty_values()
    {
        var records = new[]
        {
            new SecsTraceDiagnosticRecord(
                Epoch,
                HsmsDiagnosticCode.ControlRejected,
                HsmsDiagnosticLayer.Session,
                HsmsOperation.Deselect,
                HsmsSessionState.Selected,
                peerStatus: 0x03,
                rejectedMessageType: 0x07),
            new SecsTraceDiagnosticRecord(
                Epoch.AddSeconds(1),
                HsmsDiagnosticCode.TransportFailure,
                HsmsDiagnosticLayer.Transport,
                HsmsOperation.Connect,
                HsmsSessionState.Disconnected),
        };
        var codec = new SecsTraceDiagnosticCodec();

        var decoded = codec.Decode(codec.Encode(records));

        Assert.Equal(2, decoded.Count);
        AssertRecordEqual(records[0], decoded[0]);
        AssertRecordEqual(records[1], decoded[1]);
    }

    [Fact]
    public void Snapshot_and_export_exclude_exception_and_frame_content()
    {
        const string secret = "SECRET-PROCESS-DATA";
        var frame = new HsmsFrame(
            HsmsMessageHeader.CreateData(23, 1, 2, false, 0xAABBCCDD),
            Encoding.ASCII.GetBytes(secret));
        var connectionEvent = HsmsConnectionEvent.DataMessageDecodeFailed(
            frame,
            new SecsProtocolException(secret));

        var record = SecsTraceDiagnosticRecord.Create(Epoch, connectionEvent.Diagnostic!);
        var text = new SecsTraceDiagnosticCodec().Encode(new[] { record });

        Assert.Equal(HsmsDiagnosticCode.DataMessageDecodeFailed, record.Code);
        Assert.Equal((ushort)23, record.ProtocolSessionId);
        Assert.Equal(0xAABBCCDDu, record.SystemBytes);
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Codec_rejects_unknown_fields_noncanonical_hex_and_spacing()
    {
        const string valid =
            "SecsFrame-DiagnosticTrace/1\n" +
            "Diagnostic 1970-01-01T00:00:00.0000000Z ControlRejected Session Deselect Selected - - - 0x03 0x07\n";
        var codec = new SecsTraceDiagnosticCodec();

        Assert.Throws<SecsTraceParseException>(() => codec.Decode(string.Empty));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("DiagnosticTrace/1", "DiagnosticTrace/2")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("ControlRejected", "UnknownCode")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("ControlRejected", "10")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Session", "session")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x03", "0x3")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x07", "0x0a")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Diagnostic ", "Diagnostic  ")));
    }

    [Fact]
    public void Codec_and_record_enforce_resource_and_enum_boundaries()
    {
        var record = new SecsTraceDiagnosticRecord(
            Epoch,
            HsmsDiagnosticCode.TransportFailure,
            HsmsDiagnosticLayer.Transport,
            HsmsOperation.Connect,
            HsmsSessionState.Disconnected);
        var records = new[] { record, record };
        var codec = new SecsTraceDiagnosticCodec();
        var text = codec.Encode(records);

        Assert.Throws<ArgumentNullException>(() => codec.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => codec.Decode(null!));
        Assert.Throws<ArgumentException>(() => codec.Encode(new SecsTraceDiagnosticRecord[] { record, null! }));
        Assert.Throws<InvalidOperationException>(() => new SecsTraceDiagnosticCodec(maxRecordCount: 1).Encode(records));
        Assert.Throws<SecsTraceParseException>(() => new SecsTraceDiagnosticCodec(maxRecordCount: 1).Decode(text));
        Assert.Throws<InvalidOperationException>(() => new SecsTraceDiagnosticCodec(maxTextLength: 20).Encode(Array.Empty<SecsTraceDiagnosticRecord>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceDiagnosticCodec(maxTextLength: 20).Decode(text));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceDiagnosticCodec(maxRecordCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceDiagnosticCodec(maxTextLength: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceDiagnosticRecord(
            Epoch,
            (HsmsDiagnosticCode)int.MaxValue,
            HsmsDiagnosticLayer.Transport,
            HsmsOperation.Connect,
            HsmsSessionState.Disconnected));
        Assert.Throws<ArgumentNullException>(() => SecsTraceDiagnosticRecord.Create(Epoch, null!));
    }

    private static DateTimeOffset Epoch
        => new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static void AssertRecordEqual(
        SecsTraceDiagnosticRecord expected,
        SecsTraceDiagnosticRecord actual)
    {
        Assert.Equal(expected.Timestamp, actual.Timestamp);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Layer, actual.Layer);
        Assert.Equal(expected.Operation, actual.Operation);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Timer, actual.Timer);
        Assert.Equal(expected.ProtocolSessionId, actual.ProtocolSessionId);
        Assert.Equal(expected.SystemBytes, actual.SystemBytes);
        Assert.Equal(expected.PeerStatus, actual.PeerStatus);
        Assert.Equal(expected.RejectedMessageType, actual.RejectedMessageType);
    }
}
