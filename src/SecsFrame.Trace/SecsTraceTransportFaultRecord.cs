using System.Collections.ObjectModel;

namespace SecsFrame.Trace;

/// <summary>Describes an explicitly classified T8 prefix-snapshot sample.</summary>
/// <remarks>
/// The sample never contains an exception or a claimed complete TCP fragment.
/// StreamFrame provides at most the first 8 KiB and does not expose the original
/// buffered length or a completeness flag.
/// </remarks>
public sealed class SecsTraceTransportFaultRecord
{
    private readonly byte[] _snapshot;

    /// <summary>Creates an immutable T8 transport-fault record.</summary>
    public SecsTraceTransportFaultRecord(
        DateTimeOffset timestamp,
        HsmsTransportFaultKind kind,
        HsmsSessionState state,
        long transportSessionId,
        SecsTraceFaultSampleDataClassification dataClassification,
        int observedSnapshotLength,
        ReadOnlySpan<byte> snapshot = default,
        IEnumerable<SecsTraceByteRedactionRange>? redactionRanges = null)
    {
        if (!Enum.IsDefined(typeof(HsmsTransportFaultKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown HSMS transport-fault kind.");
        if (!Enum.IsDefined(typeof(HsmsSessionState), state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown HSMS session state.");
        if (transportSessionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(transportSessionId), transportSessionId, "The transport session identifier must be positive.");
        if (!Enum.IsDefined(typeof(SecsTraceFaultSampleDataClassification), dataClassification))
            throw new ArgumentOutOfRangeException(nameof(dataClassification), dataClassification, "Unknown fault-sample data classification.");
        if (observedSnapshotLength is <= 0 or >
            HsmsTransportFaultObservation.MaxSnapshotBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedSnapshotLength),
                observedSnapshotLength,
                $"The observed snapshot length must be between 1 and {HsmsTransportFaultObservation.MaxSnapshotBytes} bytes.");
        }

        var ranges = PrepareRanges(redactionRanges);
        ValidateDataBoundary(
            dataClassification,
            observedSnapshotLength,
            snapshot,
            ranges);

        Timestamp = timestamp.ToUniversalTime();
        Kind = kind;
        State = state;
        TransportSessionId = transportSessionId;
        DataClassification = dataClassification;
        ObservedSnapshotLength = observedSnapshotLength;
        _snapshot = snapshot.ToArray();
        RedactionRanges = ranges;
    }

    /// <summary>Gets the UTC timestamp associated with the local observation.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the transport fault kind.</summary>
    public HsmsTransportFaultKind Kind { get; }

    /// <summary>Gets the HSMS session state observed with the fault.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the StreamFrame TCP-session generation.</summary>
    public long TransportSessionId { get; }

    /// <summary>Gets how much snapshot data the record contains.</summary>
    public SecsTraceFaultSampleDataClassification DataClassification { get; }

    /// <summary>Gets the number of prefix bytes supplied by StreamFrame.</summary>
    public int ObservedSnapshotLength { get; }

    /// <summary>Gets the copied snapshot bytes, empty for metadata-only records.</summary>
    public ReadOnlySpan<byte> Snapshot => _snapshot;

    /// <summary>Gets the ordered ranges that were zeroed before capture.</summary>
    public IReadOnlyList<SecsTraceByteRedactionRange> RedactionRanges { get; }

    /// <summary>Creates a record from a public T8 transport-fault observation.</summary>
    public static SecsTraceTransportFaultRecord Create(
        DateTimeOffset timestamp,
        HsmsTransportFaultObservation observation,
        SecsTraceTransportFaultCaptureOptions options)
    {
        if (observation is null)
            throw new ArgumentNullException(nameof(observation));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        return options.DataClassification switch
        {
            SecsTraceFaultSampleDataClassification.MetadataOnly =>
                new SecsTraceTransportFaultRecord(
                    timestamp,
                    observation.Kind,
                    observation.State,
                    observation.TransportSessionId,
                    options.DataClassification,
                    observation.Snapshot.Length),
            SecsTraceFaultSampleDataClassification.RedactedPayload =>
                CreateRedacted(timestamp, observation, options),
            SecsTraceFaultSampleDataClassification.RawPayload =>
                CreateRaw(timestamp, observation, options),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.DataClassification,
                "Unknown fault-sample data classification."),
        };
    }

    private static SecsTraceTransportFaultRecord CreateRedacted(
        DateTimeOffset timestamp,
        HsmsTransportFaultObservation observation,
        SecsTraceTransportFaultCaptureOptions options)
    {
        ValidateSnapshotSize(observation.Snapshot.Length, options.MaxSnapshotBytes);
        var snapshot = observation.Snapshot.ToArray();
        foreach (var range in options.RedactionRanges)
        {
            if (range.Offset + range.Length > snapshot.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    range,
                    "A byte redaction range exceeds the T8 prefix snapshot.");
            }

            Array.Clear(snapshot, range.Offset, range.Length);
        }

        return new SecsTraceTransportFaultRecord(
            timestamp,
            observation.Kind,
            observation.State,
            observation.TransportSessionId,
            options.DataClassification,
            snapshot.Length,
            snapshot,
            options.RedactionRanges);
    }

    private static SecsTraceTransportFaultRecord CreateRaw(
        DateTimeOffset timestamp,
        HsmsTransportFaultObservation observation,
        SecsTraceTransportFaultCaptureOptions options)
    {
        ValidateSnapshotSize(observation.Snapshot.Length, options.MaxSnapshotBytes);
        return new SecsTraceTransportFaultRecord(
            timestamp,
            observation.Kind,
            observation.State,
            observation.TransportSessionId,
            options.DataClassification,
            observation.Snapshot.Length,
            observation.Snapshot);
    }

    private static void ValidateSnapshotSize(
        int snapshotLength,
        int maxSnapshotBytes)
    {
        if (snapshotLength > maxSnapshotBytes)
        {
            throw new InvalidOperationException(
                $"The T8 prefix snapshot length {snapshotLength} exceeds the configured maximum {maxSnapshotBytes}.");
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
        int observedSnapshotLength,
        ReadOnlySpan<byte> snapshot,
        IReadOnlyList<SecsTraceByteRedactionRange> ranges)
    {
        if (dataClassification == SecsTraceFaultSampleDataClassification.MetadataOnly)
        {
            if (!snapshot.IsEmpty || ranges.Count != 0)
                throw new ArgumentException("A metadata-only sample cannot contain snapshot bytes or redaction ranges.", nameof(snapshot));
            return;
        }

        if (snapshot.Length != observedSnapshotLength)
            throw new ArgumentException("A payload sample must retain every observed snapshot byte.", nameof(snapshot));
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
            if (range.Offset + range.Length > snapshot.Length)
                throw new ArgumentOutOfRangeException(nameof(ranges), range, "A byte redaction range exceeds the T8 prefix snapshot.");
            for (var index = range.Offset; index < range.Offset + range.Length; index++)
            {
                if (snapshot[index] != 0)
                    throw new ArgumentException("Every declared redaction range must contain only zero bytes.", nameof(snapshot));
            }
        }
    }
}
