using System.Collections.ObjectModel;

namespace SecsFrame.Trace;

/// <summary>
/// Defines the explicit data boundary for one decode-fault sample capture.
/// </summary>
public sealed class SecsTraceFaultSampleCaptureOptions
{
    /// <summary>The default maximum body copied into one sample: 64 KiB.</summary>
    public const int DefaultMaxBodyBytes = 64 * 1024;

    private SecsTraceFaultSampleCaptureOptions(
        SecsTraceFaultSampleDataClassification dataClassification,
        IEnumerable<SecsTraceByteRedactionRange>? redactionRanges,
        int maxBodyBytes)
    {
        if (maxBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBodyBytes), maxBodyBytes, "The maximum body size must be positive.");

        var ranges = PrepareRanges(redactionRanges);
        if (dataClassification == SecsTraceFaultSampleDataClassification.RedactedPayload)
        {
            if (ranges.Count == 0)
                throw new ArgumentException("Redacted payload capture requires at least one byte range.", nameof(redactionRanges));
        }
        else if (ranges.Count != 0)
        {
            throw new ArgumentException("Only redacted payload capture can declare byte ranges.", nameof(redactionRanges));
        }

        DataClassification = dataClassification;
        RedactionRanges = ranges;
        MaxBodyBytes = maxBodyBytes;
    }

    /// <summary>Gets how much payload data can be copied.</summary>
    public SecsTraceFaultSampleDataClassification DataClassification { get; }

    /// <summary>Gets the ordered, nonoverlapping ranges zeroed before capture.</summary>
    public IReadOnlyList<SecsTraceByteRedactionRange> RedactionRanges { get; }

    /// <summary>Gets the maximum body size copied by payload-bearing modes.</summary>
    public int MaxBodyBytes { get; }

    /// <summary>Creates an option that captures no body bytes.</summary>
    public static SecsTraceFaultSampleCaptureOptions MetadataOnly()
        => new(
            SecsTraceFaultSampleDataClassification.MetadataOnly,
            null,
            DefaultMaxBodyBytes);

    /// <summary>Creates an option that copies and zeroes declared body ranges.</summary>
    public static SecsTraceFaultSampleCaptureOptions RedactedPayload(
        IEnumerable<SecsTraceByteRedactionRange> redactionRanges,
        int maxBodyBytes = DefaultMaxBodyBytes)
    {
        if (redactionRanges is null)
            throw new ArgumentNullException(nameof(redactionRanges));

        return new(
            SecsTraceFaultSampleDataClassification.RedactedPayload,
            redactionRanges,
            maxBodyBytes);
    }

    /// <summary>Creates an option that explicitly copies the unredacted body.</summary>
    public static SecsTraceFaultSampleCaptureOptions RawPayload(
        int maxBodyBytes = DefaultMaxBodyBytes)
        => new(
            SecsTraceFaultSampleDataClassification.RawPayload,
            null,
            maxBodyBytes);

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
