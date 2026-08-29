using System.Collections.ObjectModel;

namespace SecsFrame.Trace;

/// <summary>Defines the explicit data boundary for one transport-fault capture.</summary>
public sealed class SecsTraceTransportFaultCaptureOptions
{
    /// <summary>The maximum retained transport-fault prefix snapshot: 8 KiB.</summary>
    public const int DefaultMaxSnapshotBytes =
        HsmsTransportFaultObservation.MaxSnapshotBytes;

    private SecsTraceTransportFaultCaptureOptions(
        SecsTraceFaultSampleDataClassification dataClassification,
        IEnumerable<SecsTraceByteRedactionRange>? redactionRanges,
        int maxSnapshotBytes)
    {
        if (maxSnapshotBytes is <= 0 or > DefaultMaxSnapshotBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSnapshotBytes),
                maxSnapshotBytes,
                $"The maximum snapshot size must be between 1 and {DefaultMaxSnapshotBytes} bytes.");
        }

        var ranges = PrepareRanges(redactionRanges);
        if (dataClassification ==
            SecsTraceFaultSampleDataClassification.RedactedPayload)
        {
            if (ranges.Count == 0)
            {
                throw new ArgumentException(
                    "Redacted snapshot capture requires at least one byte range.",
                    nameof(redactionRanges));
            }
        }
        else if (ranges.Count != 0)
        {
            throw new ArgumentException(
                "Only redacted snapshot capture can declare byte ranges.",
                nameof(redactionRanges));
        }

        DataClassification = dataClassification;
        RedactionRanges = ranges;
        MaxSnapshotBytes = maxSnapshotBytes;
    }

    /// <summary>Gets how much snapshot data can be copied.</summary>
    public SecsTraceFaultSampleDataClassification DataClassification { get; }

    /// <summary>Gets the ordered, nonoverlapping ranges zeroed before capture.</summary>
    public IReadOnlyList<SecsTraceByteRedactionRange> RedactionRanges { get; }

    /// <summary>Gets the maximum snapshot size copied by payload-bearing modes.</summary>
    public int MaxSnapshotBytes { get; }

    /// <summary>Creates an option that captures no snapshot bytes.</summary>
    public static SecsTraceTransportFaultCaptureOptions MetadataOnly()
        => new(
            SecsTraceFaultSampleDataClassification.MetadataOnly,
            null,
            DefaultMaxSnapshotBytes);

    /// <summary>Creates an option that copies and zeroes declared snapshot ranges.</summary>
    public static SecsTraceTransportFaultCaptureOptions RedactedPayload(
        IEnumerable<SecsTraceByteRedactionRange> redactionRanges,
        int maxSnapshotBytes = DefaultMaxSnapshotBytes)
    {
        if (redactionRanges is null)
            throw new ArgumentNullException(nameof(redactionRanges));

        return new(
            SecsTraceFaultSampleDataClassification.RedactedPayload,
            redactionRanges,
            maxSnapshotBytes);
    }

    /// <summary>Creates an option that explicitly copies the unredacted snapshot.</summary>
    public static SecsTraceTransportFaultCaptureOptions RawPayload(
        int maxSnapshotBytes = DefaultMaxSnapshotBytes)
        => new(
            SecsTraceFaultSampleDataClassification.RawPayload,
            null,
            maxSnapshotBytes);

    private static IReadOnlyList<SecsTraceByteRedactionRange> PrepareRanges(
        IEnumerable<SecsTraceByteRedactionRange>? ranges)
    {
        if (ranges is null)
            return Array.Empty<SecsTraceByteRedactionRange>();

        var prepared = ranges.OrderBy(static item => item.Offset).ToArray();
        for (var index = 1; index < prepared.Length; index++)
        {
            var previous = prepared[index - 1];
            if (previous.Offset + previous.Length > prepared[index].Offset)
            {
                throw new ArgumentException(
                    "Byte redaction ranges cannot overlap.",
                    nameof(ranges));
            }
        }

        return new ReadOnlyCollection<SecsTraceByteRedactionRange>(prepared);
    }
}
