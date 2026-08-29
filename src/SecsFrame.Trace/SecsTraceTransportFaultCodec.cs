using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace SecsFrame.Trace;

/// <summary>Encodes and decodes explicitly classified T8 prefix snapshots.</summary>
public sealed class SecsTraceTransportFaultCodec
{
    /// <summary>The exact transport-fault trace format identifier.</summary>
    public const string FormatIdentifier = "SecsFrame-TransportFaultTrace/1";

    /// <summary>The default maximum number of records.</summary>
    public const int DefaultMaxRecordCount = SecsTraceCodec.DefaultMaxRecordCount;

    /// <summary>The default maximum trace text length.</summary>
    public const int DefaultMaxTextLength = SecsTraceCodec.DefaultMaxTextLength;

    /// <summary>The default maximum snapshot accepted in one payload record.</summary>
    public const int DefaultMaxSnapshotBytes =
        SecsTraceTransportFaultCaptureOptions.DefaultMaxSnapshotBytes;

    /// <summary>Creates a transport-fault codec with explicit data and resource limits.</summary>
    public SecsTraceTransportFaultCodec(
        bool allowPayloadRecords = false,
        int maxSnapshotBytes = DefaultMaxSnapshotBytes,
        int maxRecordCount = DefaultMaxRecordCount,
        int maxTextLength = DefaultMaxTextLength)
    {
        if (maxSnapshotBytes is <= 0 or > DefaultMaxSnapshotBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSnapshotBytes),
                maxSnapshotBytes,
                $"The maximum snapshot size must be between 1 and {DefaultMaxSnapshotBytes} bytes.");
        }
        if (maxRecordCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRecordCount), maxRecordCount, "The maximum record count must be positive.");
        if (maxTextLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTextLength), maxTextLength, "The maximum text length must be positive.");

        AllowPayloadRecords = allowPayloadRecords;
        MaxSnapshotBytes = maxSnapshotBytes;
        MaxRecordCount = maxRecordCount;
        MaxTextLength = maxTextLength;
    }

    /// <summary>Gets whether payload-bearing records can be imported or exported.</summary>
    public bool AllowPayloadRecords { get; }

    /// <summary>Gets the maximum snapshot bytes in one payload-bearing record.</summary>
    public int MaxSnapshotBytes { get; }

    /// <summary>Gets the maximum number of records.</summary>
    public int MaxRecordCount { get; }

    /// <summary>Gets the maximum accepted or produced trace length.</summary>
    public int MaxTextLength { get; }

    /// <summary>Encodes records in enumeration order using LF line endings.</summary>
    public string Encode(IEnumerable<SecsTraceTransportFaultRecord> records)
    {
        if (records is null)
            throw new ArgumentNullException(nameof(records));

        var text = new StringBuilder(FormatIdentifier);
        text.Append('\n');
        EnsureTextLength(text);
        var recordCount = 0;
        foreach (var record in records)
        {
            if (record is null)
                throw new ArgumentException("The transport-fault trace sequence contains a null record.", nameof(records));
            if (++recordCount > MaxRecordCount)
                throw new InvalidOperationException($"The transport-fault trace record count exceeds the configured maximum {MaxRecordCount}.");

            EnsureRecordAllowed(record);
            AppendRecord(text, record);
            EnsureTextLength(text);
        }

        return text.ToString();
    }

    /// <summary>Strictly decodes one complete transport-fault trace file.</summary>
    public IReadOnlyList<SecsTraceTransportFaultRecord> Decode(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), text.Length, $"The transport-fault trace text length cannot exceed {MaxTextLength} characters.");

        return new Parser(text, this).Parse();
    }

    private void EnsureRecordAllowed(SecsTraceTransportFaultRecord record)
    {
        if (record.DataClassification !=
                SecsTraceFaultSampleDataClassification.MetadataOnly &&
            !AllowPayloadRecords)
        {
            throw new InvalidOperationException(
                "Payload-bearing transport-fault samples require AllowPayloadRecords to be enabled explicitly.");
        }

        if (record.Snapshot.Length > MaxSnapshotBytes)
        {
            throw new InvalidOperationException(
                $"The transport-fault snapshot length {record.Snapshot.Length} exceeds the configured maximum {MaxSnapshotBytes}.");
        }
    }

    private static void AppendRecord(
        StringBuilder text,
        SecsTraceTransportFaultRecord record)
    {
        text.Append("TransportFault ");
        text.Append(record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(record.Kind);
        text.Append(' ');
        text.Append(record.State);
        text.Append(' ');
        text.Append(record.TransportSessionId.ToString(CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(record.DataClassification);
        text.Append(' ');
        text.Append(record.ObservedSnapshotLength.ToString(CultureInfo.InvariantCulture));
        text.Append(' ');
        AppendRanges(text, record.RedactionRanges);
        text.Append(' ');
        AppendSnapshot(text, record.Snapshot);
        text.Append('\n');
    }

    private static void AppendRanges(
        StringBuilder text,
        IReadOnlyList<SecsTraceByteRedactionRange> ranges)
    {
        if (ranges.Count == 0)
        {
            text.Append('-');
            return;
        }

        for (var index = 0; index < ranges.Count; index++)
        {
            if (index != 0)
                text.Append(',');
            text.Append(ranges[index].Offset.ToString(CultureInfo.InvariantCulture));
            text.Append(':');
            text.Append(ranges[index].Length.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendSnapshot(
        StringBuilder text,
        ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.IsEmpty)
        {
            text.Append('-');
            return;
        }

        foreach (var value in snapshot)
            text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
    }

    private void EnsureTextLength(StringBuilder text)
    {
        if (text.Length > MaxTextLength)
            throw new InvalidOperationException($"The transport-fault trace text length exceeds the configured maximum {MaxTextLength}.");
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly SecsTraceTransportFaultCodec _options;
        private int _index;
        private int _recordIndex;

        public Parser(string text, SecsTraceTransportFaultCodec options)
        {
            _text = text;
            _options = options;
        }

        public IReadOnlyList<SecsTraceTransportFaultRecord> Parse()
        {
            var headerOffset = _index;
            var formatIdentifier = ReadLine();
            if (!string.Equals(formatIdentifier, FormatIdentifier, StringComparison.Ordinal))
                throw Error("The transport-fault trace format identifier is missing or unsupported", -1, headerOffset);

            var records = new List<SecsTraceTransportFaultRecord>();
            while (!IsEnd)
            {
                if (_recordIndex >= _options.MaxRecordCount)
                    throw Error($"The transport-fault trace record count exceeds the configured maximum {_options.MaxRecordCount}", _recordIndex, _index);
                records.Add(ParseRecord());
                _recordIndex++;
            }

            return new ReadOnlyCollection<SecsTraceTransportFaultRecord>(records);
        }

        private SecsTraceTransportFaultRecord ParseRecord()
        {
            var recordOffset = _index;
            var fields = ReadLine().Split(new[] { ' ' }, StringSplitOptions.None);
            if (fields.Length != 9 ||
                !string.Equals(fields[0], "TransportFault", StringComparison.Ordinal))
            {
                throw Error("A transport-fault record must contain exactly nine single-space-separated fields", _recordIndex, recordOffset);
            }

            var classification = ParseEnum<SecsTraceFaultSampleDataClassification>(fields[5], "data classification", recordOffset);
            if (classification != SecsTraceFaultSampleDataClassification.MetadataOnly &&
                !_options.AllowPayloadRecords)
            {
                throw Error("Payload-bearing transport-fault samples require AllowPayloadRecords to be enabled explicitly", _recordIndex, recordOffset);
            }

            var snapshot = ParseSnapshot(fields[8], recordOffset);
            var ranges = ParseRanges(fields[7], recordOffset);
            try
            {
                return new SecsTraceTransportFaultRecord(
                    ParseTimestamp(fields[1], recordOffset),
                    ParseEnum<HsmsTransportFaultKind>(fields[2], "transport-fault kind", recordOffset),
                    ParseEnum<HsmsSessionState>(fields[3], "session state", recordOffset),
                    ParseInt64(fields[4], "transport Session ID", recordOffset),
                    classification,
                    ParseInt32(fields[6], "observed snapshot length", recordOffset),
                    snapshot,
                    ranges);
            }
            catch (ArgumentException ex)
            {
                throw Error("The transport-fault data boundary is invalid", _recordIndex, recordOffset, ex);
            }
        }

        private DateTimeOffset ParseTimestamp(string value, int offset)
        {
            if (!DateTimeOffset.TryParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var timestamp))
            {
                throw Error("The transport-fault timestamp must use the round-trip format", _recordIndex, offset);
            }

            return timestamp;
        }

        private TEnum ParseEnum<TEnum>(string value, string fieldName, int offset)
            where TEnum : struct, Enum
        {
            if (!Enum.TryParse(value, ignoreCase: false, out TEnum parsed) ||
                !Enum.IsDefined(typeof(TEnum), parsed) ||
                !string.Equals(value, parsed.ToString(), StringComparison.Ordinal))
            {
                throw Error($"The {fieldName} is unknown", _recordIndex, offset);
            }

            return parsed;
        }

        private int ParseInt32(string value, string fieldName, int offset)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed <= 0 ||
                !string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw Error($"The {fieldName} must be a canonical positive 32-bit integer", _recordIndex, offset);
            }

            return parsed;
        }

        private long ParseInt64(string value, string fieldName, int offset)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed <= 0 ||
                !string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw Error($"The {fieldName} must be a canonical positive 64-bit integer", _recordIndex, offset);
            }

            return parsed;
        }

        private IReadOnlyList<SecsTraceByteRedactionRange> ParseRanges(
            string value,
            int offset)
        {
            if (string.Equals(value, "-", StringComparison.Ordinal))
                return Array.Empty<SecsTraceByteRedactionRange>();

            var parts = value.Split(',');
            var ranges = new SecsTraceByteRedactionRange[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                var fields = parts[index].Split(':');
                if (fields.Length != 2)
                    throw Error("A redaction range must use the exact form offset:length", _recordIndex, offset);
                var rangeOffset = ParseCanonicalRangeValue(fields[0], "redaction offset", offset, allowZero: true);
                var length = ParseCanonicalRangeValue(fields[1], "redaction length", offset, allowZero: false);
                try
                {
                    ranges[index] = new SecsTraceByteRedactionRange(rangeOffset, length);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw Error("A redaction range is outside the supported integer range", _recordIndex, offset, ex);
                }
            }

            return ranges;
        }

        private int ParseCanonicalRangeValue(
            string value,
            string fieldName,
            int offset,
            bool allowZero)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed < (allowZero ? 0 : 1) ||
                !string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw Error($"The {fieldName} is not canonical", _recordIndex, offset);
            }

            return parsed;
        }

        private byte[] ParseSnapshot(string value, int offset)
        {
            if (string.Equals(value, "-", StringComparison.Ordinal))
                return Array.Empty<byte>();
            if (value.Length % 2 != 0)
                throw Error("The snapshot must contain an even number of uppercase hexadecimal digits", _recordIndex, offset);

            var snapshotLength = value.Length / 2;
            if (snapshotLength > _options.MaxSnapshotBytes)
                throw Error($"The transport-fault snapshot length exceeds the configured maximum {_options.MaxSnapshotBytes}", _recordIndex, offset);

            var snapshot = new byte[snapshotLength];
            for (var index = 0; index < snapshot.Length; index++)
            {
                var high = ParseHexDigit(value[index * 2]);
                var low = ParseHexDigit(value[(index * 2) + 1]);
                if (high < 0 || low < 0)
                    throw Error("The snapshot must contain only uppercase hexadecimal digits", _recordIndex, offset);
                snapshot[index] = (byte)((high << 4) | low);
            }

            return snapshot;
        }

        private static int ParseHexDigit(char value)
            => value switch
            {
                >= '0' and <= '9' => value - '0',
                >= 'A' and <= 'F' => value - 'A' + 10,
                _ => -1,
            };

        private string ReadLine()
        {
            var start = _index;
            while (!IsEnd && _text[_index] != '\n')
                _index++;

            var length = _index - start;
            if (length > 0 && _text[start + length - 1] == '\r')
                length--;
            var line = _text.Substring(start, length);
            if (!IsEnd)
                _index++;
            return line;
        }

        private bool IsEnd => _index >= _text.Length;

        private static SecsTraceParseException Error(
            string message,
            int recordIndex,
            int offset,
            Exception? innerException = null)
            => new(message, recordIndex, offset, innerException);
    }
}
