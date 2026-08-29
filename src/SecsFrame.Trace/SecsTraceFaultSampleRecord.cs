using System.Collections.ObjectModel;

namespace SecsFrame.Trace;

/// <summary>
/// Describes an explicitly classified sample of one undecodable HSMS data message.
/// </summary>
/// <remarks>
/// The sample contains the original HSMS header fields and optionally a defensive
/// copy of the SECS-II body. It never contains an exception, TCP framing bytes,
/// transport-session generation, or socket-fragment timing.
/// </remarks>
public sealed class SecsTraceFaultSampleRecord
{
    private readonly byte[] _body;

    /// <summary>Creates an immutable decode-fault sample record.</summary>
    public SecsTraceFaultSampleRecord(
        DateTimeOffset timestamp,
        HsmsSessionState state,
        SecsTraceFaultSampleDataClassification dataClassification,
        HsmsMessageHeader header,
        int originalBodyLength,
        ReadOnlySpan<byte> body = default,
        IEnumerable<SecsTraceByteRedactionRange>? redactionRanges = null)
    {
        if (!Enum.IsDefined(typeof(HsmsSessionState), state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown HSMS session state.");
        if (!Enum.IsDefined(typeof(SecsTraceFaultSampleDataClassification), dataClassification))
            throw new ArgumentOutOfRangeException(nameof(dataClassification), dataClassification, "Unknown fault-sample data classification.");
        if (!header.IsDataMessage)
            throw new ArgumentException("A decode-fault sample requires an HSMS data-message header.", nameof(header));
        if (originalBodyLength < 0)
            throw new ArgumentOutOfRangeException(nameof(originalBodyLength), originalBodyLength, "The original body length cannot be negative.");

        var ranges = PrepareRanges(redactionRanges);
        ValidateDataBoundary(
            dataClassification,
            originalBodyLength,
            body,
            ranges);

        Timestamp = timestamp.ToUniversalTime();
        State = state;
        DataClassification = dataClassification;
        Header = header;
        OriginalBodyLength = originalBodyLength;
        _body = body.ToArray();
        RedactionRanges = ranges;
    }

    /// <summary>Gets the UTC timestamp associated with the local observation.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the stable diagnostic code represented by this sample.</summary>
    public HsmsDiagnosticCode Code => HsmsDiagnosticCode.DataMessageDecodeFailed;

    /// <summary>Gets the session state observed with the decode failure.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets how much payload data the record contains.</summary>
    public SecsTraceFaultSampleDataClassification DataClassification { get; }

    /// <summary>Gets the original ten-byte HSMS data-message header.</summary>
    public HsmsMessageHeader Header { get; }

    /// <summary>Gets the original SECS-II body length before classification.</summary>
    public int OriginalBodyLength { get; }

    /// <summary>Gets the copied body bytes, empty for metadata-only records.</summary>
    public ReadOnlySpan<byte> Body => _body;

    /// <summary>Gets the ordered ranges that were zeroed before capture.</summary>
    public IReadOnlyList<SecsTraceByteRedactionRange> RedactionRanges { get; }

    /// <summary>
    /// Creates a sample from a public data-message decode-failure event.
    /// </summary>
    public static SecsTraceFaultSampleRecord Create(
        DateTimeOffset timestamp,
        HsmsConnectionEvent connectionEvent,
        SecsTraceFaultSampleCaptureOptions options)
    {
        if (connectionEvent is null)
            throw new ArgumentNullException(nameof(connectionEvent));
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (connectionEvent.Kind != HsmsConnectionEventKind.DataMessageDecodeFailed ||
            connectionEvent.Frame is null ||
            connectionEvent.Diagnostic?.Code !=
                HsmsDiagnosticCode.DataMessageDecodeFailed)
        {
            throw new ArgumentException(
                "The connection event must contain a data-message decode failure.",
                nameof(connectionEvent));
        }

        var frame = connectionEvent.Frame;
        var body = frame.Body.Span;
        return options.DataClassification switch
        {
            SecsTraceFaultSampleDataClassification.MetadataOnly =>
                new SecsTraceFaultSampleRecord(
                    timestamp,
                    connectionEvent.State,
                    options.DataClassification,
                    frame.Header,
                    body.Length),
            SecsTraceFaultSampleDataClassification.RedactedPayload =>
                CreateRedacted(timestamp, connectionEvent.State, frame, options),
            SecsTraceFaultSampleDataClassification.RawPayload =>
                CreateRaw(timestamp, connectionEvent.State, frame, options),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.DataClassification,
                "Unknown fault-sample data classification."),
        };
    }

    private static SecsTraceFaultSampleRecord CreateRedacted(
        DateTimeOffset timestamp,
        HsmsSessionState state,
        HsmsFrame frame,
        SecsTraceFaultSampleCaptureOptions options)
    {
        ValidateBodySize(frame.Body.Length, options.MaxBodyBytes);
        var body = frame.Body.ToArray();
        foreach (var range in options.RedactionRanges)
        {
            if (range.Offset + range.Length > body.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    range,
                    "A byte redaction range exceeds the HSMS data-message body.");
            }

            Array.Clear(body, range.Offset, range.Length);
        }

        return new SecsTraceFaultSampleRecord(
            timestamp,
            state,
            options.DataClassification,
            frame.Header,
            body.Length,
            body,
            options.RedactionRanges);
    }

    private static SecsTraceFaultSampleRecord CreateRaw(
        DateTimeOffset timestamp,
        HsmsSessionState state,
        HsmsFrame frame,
        SecsTraceFaultSampleCaptureOptions options)
    {
        ValidateBodySize(frame.Body.Length, options.MaxBodyBytes);
        return new SecsTraceFaultSampleRecord(
            timestamp,
            state,
            options.DataClassification,
            frame.Header,
            frame.Body.Length,
            frame.Body.Span);
    }

    private static void ValidateBodySize(int bodyLength, int maxBodyBytes)
    {
        if (bodyLength > maxBodyBytes)
        {
            throw new InvalidOperationException(
                $"The fault-sample body length {bodyLength} exceeds the configured maximum {maxBodyBytes}.");
        }
    }

    private static IReadOnlyList<SecsTraceByteRedactionRange> PrepareRanges(
        IEnumerable<SecsTraceByteRedactionRange>? ranges)
    {
        if (ranges is null)
            return Array.Empty<SecsTraceByteRedactionRange>();

        var prepared = ranges.ToArray();
        for (var index = 1; index < prepared.Length; index++)
        {
            var previous = prepared[index - 1];
            if (previous.Offset + previous.Length > prepared[index].Offset)
            {
                throw new ArgumentException(
                    "Byte redaction ranges must be ordered and cannot overlap.",
                    nameof(ranges));
            }
        }

        return new ReadOnlyCollection<SecsTraceByteRedactionRange>(prepared);
    }

    private static void ValidateDataBoundary(
        SecsTraceFaultSampleDataClassification dataClassification,
        int originalBodyLength,
        ReadOnlySpan<byte> body,
        IReadOnlyList<SecsTraceByteRedactionRange> ranges)
    {
        if (dataClassification == SecsTraceFaultSampleDataClassification.MetadataOnly)
        {
            if (!body.IsEmpty || ranges.Count != 0)
                throw new ArgumentException("A metadata-only sample cannot contain body bytes or redaction ranges.", nameof(body));
            return;
        }

        if (body.Length != originalBodyLength)
            throw new ArgumentException("A payload sample must retain the original body length.", nameof(body));
        if (dataClassification == SecsTraceFaultSampleDataClassification.RawPayload)
        {
            if (ranges.Count != 0)
                throw new ArgumentException("A raw payload sample cannot declare redaction ranges.", nameof(ranges));
            return;
        }

        if (ranges.Count == 0)
            throw new ArgumentException("A redacted payload sample requires at least one byte range.", nameof(ranges));
        foreach (var range in ranges)
        {
            if (range.Offset + range.Length > body.Length)
                throw new ArgumentOutOfRangeException(nameof(ranges), range, "A byte redaction range exceeds the sample body.");
            for (var index = range.Offset; index < range.Offset + range.Length; index++)
            {
                if (body[index] != 0)
                    throw new ArgumentException("Every declared redaction range must contain only zero bytes.", nameof(body));
            }
        }
    }
}
