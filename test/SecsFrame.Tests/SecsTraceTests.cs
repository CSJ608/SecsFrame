using System.Net;
using SecsFrame.Trace;

namespace SecsFrame.Tests;

public sealed class SecsTraceTests
{
    [Fact]
    public void Codec_produces_a_deterministic_trace_vector()
    {
        var timestamp = new DateTimeOffset(2026, 8, 29, 13, 0, 0, TimeSpan.FromHours(8));
        var record = SecsTraceRecord.CreateSent(timestamp, new SecsMessage(1, 1, true));
        var codec = new SecsTraceCodec();

        var text = codec.Encode(new[] { record });

        Assert.Equal(
            "SecsFrame-Trace/1\n" +
            "Record 2026-08-29T05:00:00.0000000Z Sent - - 10\n" +
            "'S1F1'W\n" +
            ".\n",
            text);
        var decoded = Assert.Single(codec.Decode(text));
        Assert.Equal(timestamp.ToUniversalTime(), decoded.Timestamp);
        Assert.Equal(SecsTraceDirection.Sent, decoded.Direction);
        Assert.Null(decoded.SessionId);
        Assert.Null(decoded.SystemBytes);
        AssertMessageEqual(record.Message, decoded.Message);
    }

    [Fact]
    public void Codec_round_trips_multiple_records_and_optional_hsms_identifiers()
    {
        var records = new[]
        {
            new SecsTraceRecord(
                DateTimeOffset.Parse("2026-08-29T05:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                SecsTraceDirection.Received,
                new SecsMessage(6, 11, true, SecsItem.List(SecsItem.Ascii("LOT-1"))),
                sessionId: 10,
                systemBytes: 0x01020304),
            SecsTraceRecord.CreateSent(
                DateTimeOffset.Parse("2026-08-29T05:00:01Z", System.Globalization.CultureInfo.InvariantCulture),
                new SecsMessage(6, 12, rootItem: SecsItem.Binary(0x00))),
        };
        var codec = new SecsTraceCodec();

        var decoded = codec.Decode(codec.Encode(records));

        Assert.Equal(2, decoded.Count);
        Assert.Equal(SecsTraceDirection.Received, decoded[0].Direction);
        Assert.Equal((ushort)10, decoded[0].SessionId);
        Assert.Equal(0x01020304u, decoded[0].SystemBytes);
        AssertMessageEqual(records[0].Message, decoded[0].Message);
        AssertMessageEqual(records[1].Message, decoded[1].Message);
    }

    [Fact]
    public void Received_factory_preserves_decoded_protocol_identifiers()
    {
        var dataMessage = new HsmsDataMessage(42, 0x89ABCDEF, new SecsMessage(5, 1));
        var incoming = new HsmsIncomingDataMessage(new object(), new HsmsTransportSessionId(7), dataMessage);

        var record = SecsTraceRecord.CreateReceived(DateTimeOffset.UtcNow, incoming);

        Assert.Equal(SecsTraceDirection.Received, record.Direction);
        Assert.Equal((ushort)42, record.SessionId);
        Assert.Equal(0x89ABCDEFu, record.SystemBytes);
        Assert.Same(dataMessage.Message, record.Message);
    }

    [Fact]
    public void Codec_rejects_malformed_headers_lengths_and_embedded_sml()
    {
        var codec = new SecsTraceCodec();
        var valid = codec.Encode(new[]
        {
            new SecsTraceRecord(
                Epoch,
                SecsTraceDirection.Received,
                new SecsMessage(1, 1),
                1,
                0x01020304),
        });

        Assert.Throws<SecsTraceParseException>(() => codec.Decode(string.Empty));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Trace/1", "Trace/2")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("Received", "Sideways")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace("0x01020304", "0x0102030a")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(valid.Replace(" 9\n'S1F1'", " 99\n'S1F1'")));
        Assert.Throws<SecsTraceParseException>(() => codec.Decode(
            "SecsFrame-Trace/1\nRecord 1970-01-01T00:00:00.0000000Z Sent - - 4\nnope"));
    }

    [Fact]
    public void Codec_enforces_record_and_text_limits()
    {
        var record = SecsTraceRecord.CreateSent(Epoch, new SecsMessage(1, 1));
        var twoRecords = new[] { record, record };
        var encoded = new SecsTraceCodec().Encode(twoRecords);

        Assert.Throws<InvalidOperationException>(() => new SecsTraceCodec(maxRecordCount: 1).Encode(twoRecords));
        Assert.Throws<SecsTraceParseException>(() => new SecsTraceCodec(maxRecordCount: 1).Decode(encoded));
        Assert.Throws<InvalidOperationException>(() => new SecsTraceCodec(maxTextLength: 20).Encode(new[] { record }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceCodec(maxTextLength: 20).Decode(encoded));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceCodec(maxRecordCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceCodec(maxTextLength: 0));
    }

    [Fact]
    public void Structural_redaction_removes_sensitive_values_before_export()
    {
        var original = new SecsTraceRecord(
            Epoch,
            SecsTraceDirection.Received,
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.List(
                    SecsItem.Ascii("SECRET-LOT"),
                    SecsItem.List(SecsItem.U4(42), SecsItem.Ascii("SECRET-USER")))),
            10,
            0x01020304);
        var redactor = new SecsTraceRedactor(new[]
        {
            new SecsTraceRedactionRule(6, 11, new[] { 0 }, SecsItem.Ascii("REDACTED")),
            new SecsTraceRedactionRule(6, 11, new[] { 1, 1 }, SecsItem.Ascii("REDACTED")),
        });

        var redacted = redactor.Redact(original);
        var exported = new SecsTraceCodec().Encode(new[] { redacted });

        Assert.Equal("SECRET-LOT", original.Message.RootItem![0].GetString());
        Assert.Equal("REDACTED", redacted.Message.RootItem![0].GetString());
        Assert.Equal("REDACTED", redacted.Message.RootItem[1][1].GetString());
        Assert.Equal(original.Timestamp, redacted.Timestamp);
        Assert.Equal(original.SessionId, redacted.SessionId);
        Assert.Equal(original.SystemBytes, redacted.SystemBytes);
        Assert.DoesNotContain("SECRET", exported, StringComparison.Ordinal);
        Assert.Contains("REDACTED", exported, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_is_strict_about_paths_and_ambiguous_rules()
    {
        var record = SecsTraceRecord.CreateSent(
            Epoch,
            new SecsMessage(1, 1, rootItem: SecsItem.List(SecsItem.Ascii("value"))));
        var missing = new SecsTraceRedactor(new[]
        {
            new SecsTraceRedactionRule(1, 1, new[] { 1 }, SecsItem.Ascii("x")),
        });

        Assert.Throws<InvalidOperationException>(() => missing.Redact(record));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SecsTraceRedactionRule(1, 1, new[] { -1 }, SecsItem.Ascii("x")));
        Assert.Throws<ArgumentException>(() => new SecsTraceRedactor(new[]
        {
            new SecsTraceRedactionRule(1, 1, new[] { 0 }, SecsItem.Ascii("x")),
            new SecsTraceRedactionRule(1, 1, new[] { 0, 1 }, SecsItem.Ascii("y")),
        }));
    }

    [Fact]
    public async Task Replay_prevalidates_filters_and_sends_in_source_order()
    {
        var received = new SecsTraceRecord(Epoch, SecsTraceDirection.Received, new SecsMessage(1, 9));
        var first = SecsTraceRecord.CreateSent(Epoch, new SecsMessage(1, 1, true));
        var denied = SecsTraceRecord.CreateSent(Epoch, new SecsMessage(2, 1));
        var second = SecsTraceRecord.CreateSent(Epoch, new SecsMessage(1, 3));
        var sent = new List<SecsMessage>();

        var results = await new SecsTraceReplayer().ReplayAsync(
            new[] { received, first, denied, second },
            (message, _) =>
            {
                sent.Add(message);
                HsmsDataMessage? secondary = message.ReplyExpected
                    ? new HsmsDataMessage(10, 99, new SecsMessage(message.Stream, (byte)(message.Function + 1)))
                    : null;
                return Task.FromResult(secondary);
            },
            record => record.Message.Stream == 1).ConfigureAwait(true);

        Assert.Equal(new byte[] { 1, 3 }, sent.Select(message => message.Function));
        Assert.Equal(2, results.Count);
        Assert.Same(first, results[0].Record);
        Assert.NotNull(results[0].Secondary);
        Assert.Same(second, results[1].Record);
        Assert.Null(results[1].Secondary);
    }

    [Fact]
    public async Task Replay_validation_failure_occurs_before_any_send()
    {
        var record = SecsTraceRecord.CreateSent(Epoch, new SecsMessage(1, 1));
        var sends = 0;
        var replayer = new SecsTraceReplayer();

        await Assert.ThrowsAsync<ArgumentException>(() => replayer.ReplayAsync(
            new SecsTraceRecord[] { record, null! },
            (_, _) =>
            {
                sends++;
                return Task.FromResult<HsmsDataMessage?>(null);
            },
            _ => true)).ConfigureAwait(true);

        Assert.Equal(0, sends);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SecsTraceReplayer(1).ReplayAsync(
            new[] { record, record },
            (_, _) =>
            {
                sends++;
                return Task.FromResult<HsmsDataMessage?>(null);
            },
            _ => true)).ConfigureAwait(true);
        Assert.Equal(0, sends);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replayer.ReplayAsync(
            new[] { record },
            (_, _) =>
            {
                sends++;
                return Task.FromResult<HsmsDataMessage?>(null);
            },
            _ => true,
            cancellation.Token)).ConfigureAwait(true);
        Assert.Equal(0, sends);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceReplayer(0));
    }

    [Fact]
    public async Task Timed_replay_scales_caps_and_filters_source_intervals()
    {
        var delays = new List<TimeSpan>();
        var sent = new List<byte>();
        var replayer = new SecsTraceReplayer(
            SecsTraceReplayer.DefaultMaxRecordCount,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });
        var records = new[]
        {
            SentAt(TimeSpan.Zero, stream: 1, function: 1),
            SentAt(TimeSpan.FromSeconds(100), stream: 2, function: 2),
            new SecsTraceRecord(Epoch.AddSeconds(200), SecsTraceDirection.Received, new SecsMessage(1, 9)),
            SentAt(TimeSpan.FromSeconds(10), stream: 1, function: 3),
            SentAt(TimeSpan.FromSeconds(13), stream: 1, function: 5),
        };
        var timing = new SecsTraceReplayTimingOptions(
            speedMultiplier: 2,
            maxDelay: TimeSpan.FromSeconds(4));

        var results = await replayer.ReplayWithTimingAsync(
            records,
            (message, _) =>
            {
                sent.Add(message.Function);
                return Task.FromResult<HsmsDataMessage?>(null);
            },
            record => record.Message.Stream == 1,
            timing).ConfigureAwait(true);

        Assert.Equal(new byte[] { 1, 3, 5 }, sent);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1.5) }, delays);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Timed_replay_rejects_timestamp_regression_before_delay_or_send()
    {
        var delays = 0;
        var sends = 0;
        var replayer = new SecsTraceReplayer(
            SecsTraceReplayer.DefaultMaxRecordCount,
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });
        var records = new[]
        {
            SentAt(TimeSpan.FromSeconds(2), stream: 1, function: 1),
            SentAt(TimeSpan.FromSeconds(1), stream: 1, function: 3),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => replayer.ReplayWithTimingAsync(
            records,
            (_, _) =>
            {
                sends++;
                return Task.FromResult<HsmsDataMessage?>(null);
            },
            _ => true,
            new SecsTraceReplayTimingOptions())).ConfigureAwait(true);

        Assert.Equal(0, delays);
        Assert.Equal(0, sends);
    }

    [Fact]
    public async Task Default_replay_ignores_timestamps_and_never_invokes_delay()
    {
        var sends = 0;
        var replayer = new SecsTraceReplayer(
            SecsTraceReplayer.DefaultMaxRecordCount,
            (_, _) => throw new InvalidOperationException("Default replay must not delay."));
        var records = new[]
        {
            SentAt(TimeSpan.FromSeconds(2), stream: 1, function: 1),
            SentAt(TimeSpan.FromSeconds(1), stream: 1, function: 3),
        };

        var results = await replayer.ReplayAsync(
            records,
            (_, _) =>
            {
                sends++;
                return Task.FromResult<HsmsDataMessage?>(null);
            },
            _ => true).ConfigureAwait(true);

        Assert.Equal(2, sends);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Canceling_a_timed_delay_prevents_the_next_send()
    {
        using var cancellation = new CancellationTokenSource();
        var sends = 0;
        var replayer = new SecsTraceReplayer(
            SecsTraceReplayer.DefaultMaxRecordCount,
            (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            });
        var records = new[]
        {
            SentAt(TimeSpan.Zero, stream: 1, function: 1),
            SentAt(TimeSpan.FromSeconds(1), stream: 1, function: 3),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replayer.ReplayWithTimingAsync(
            records,
            (_, _) =>
            {
                sends++;
                return Task.FromResult<HsmsDataMessage?>(null);
            },
            _ => true,
            new SecsTraceReplayTimingOptions(),
            cancellation.Token)).ConfigureAwait(true);

        Assert.Equal(1, sends);
    }

    [Fact]
    public void Timing_options_reject_non_finite_speed_and_non_positive_delay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceReplayTimingOptions(speedMultiplier: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceReplayTimingOptions(speedMultiplier: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceReplayTimingOptions(speedMultiplier: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceReplayTimingOptions(speedMultiplier: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceReplayTimingOptions(maxDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecsTraceReplayTimingOptions(maxDelay: TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public async Task Replay_uses_a_new_real_tcp_transaction_and_ignores_received_records()
    {
        var port = GetFreePort();
        await using var passive = new HsmsConnection(CreateOptions(port, HsmsConnectionMode.Passive));
        await using var active = new HsmsConnection(CreateOptions(port, HsmsConnectionMode.Active));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        passive.Start();
        active.Start();
        await using var events = passive.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await Task.WhenAll(
            passive.WaitUntilSelectedAsync(cancellation.Token),
            active.WaitUntilSelectedAsync(cancellation.Token)).ConfigureAwait(true);

        var primary = new SecsMessage(1, 1, true, SecsItem.Ascii("REPLAY"));
        var source = new[]
        {
            new SecsTraceRecord(Epoch, SecsTraceDirection.Received, new SecsMessage(9, 9)),
            new SecsTraceRecord(Epoch, SecsTraceDirection.Sent, primary, 999, 0xDEADBEEF),
        };
        var replay = new SecsTraceReplayer().ReplayAsync(source, active, _ => true, cancellation.Token);
        var received = await NextDataMessageAsync(events).ConfigureAwait(true);

        Assert.Equal((ushort)10, received.DataMessage.SessionId);
        Assert.NotEqual(0xDEADBEEFu, received.DataMessage.SystemBytes);
        AssertMessageEqual(primary, received.DataMessage.Message);
        await passive.ReplyAsync(
            received,
            new SecsMessage(1, 2, rootItem: SecsItem.Boolean(true)),
            cancellation.Token).ConfigureAwait(true);
        var result = Assert.Single(await replay.ConfigureAwait(true));

        Assert.NotNull(result.Secondary);
        Assert.Equal((ushort)10, result.Secondary.SessionId);
        Assert.NotEqual(0xDEADBEEFu, result.Secondary.SystemBytes);
        Assert.Equal(2, result.Secondary.Message.Function);
    }

    private static HsmsConnectionOptions CreateOptions(int port, HsmsConnectionMode mode)
        => new(
            IPAddress.Loopback,
            port,
            mode,
            sessionId: 10,
            t3: TimeSpan.FromSeconds(5),
            t5: TimeSpan.FromMilliseconds(10),
            t6: TimeSpan.FromSeconds(5),
            t7: TimeSpan.FromSeconds(10),
            t8: TimeSpan.FromSeconds(5));

    private static DateTimeOffset Epoch
        => new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SecsTraceRecord SentAt(
        TimeSpan offset,
        byte stream,
        byte function)
        => SecsTraceRecord.CreateSent(
            Epoch.Add(offset),
            new SecsMessage(stream, function));

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<HsmsIncomingDataMessage> NextDataMessageAsync(
        IAsyncEnumerator<HsmsConnectionEvent> events)
    {
        while (await events.MoveNextAsync().ConfigureAwait(true))
        {
            if (events.Current.Kind == HsmsConnectionEventKind.DataMessageReceived)
                return events.Current.IncomingMessage!;
        }

        Assert.Fail("The replayed data message was not received.");
        return null!;
    }

    private static void AssertMessageEqual(SecsMessage expected, SecsMessage actual)
    {
        Assert.Equal(expected.Stream, actual.Stream);
        Assert.Equal(expected.Function, actual.Function);
        Assert.Equal(expected.ReplyExpected, actual.ReplyExpected);
        Assert.Equal(expected.RootItem, actual.RootItem);
    }
}
