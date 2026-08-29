using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace SecsFrame.Trace;

/// <summary>Encodes and decodes explicitly classified decode-fault samples.</summary>
public sealed class SecsTraceFaultSampleCodec
{
    /// <summary>The exact fault-sample trace format identifier.</summary>
    public const string FormatIdentifier = "SecsFrame-FaultSampleTrace/1";

    /// <summary>The default maximum number of records in one fault-sample trace.</summary>
    public const int DefaultMaxRecordCount = SecsTraceCodec.DefaultMaxRecordCount;

    /// <summary>The default maximum fault-sample trace text length.</summary>
    public const int DefaultMaxTextLength = SecsTraceCodec.DefaultMaxTextLength;

    /// <summary>The default maximum body accepted in one payload-bearing record.</summary>
    public const int DefaultMaxBodyBytes =
        SecsTraceFaultSampleCaptureOptions.DefaultMaxBodyBytes;

    /// <summary>Creates a fault-sample codec with explicit data and resource limits.</summary>
    public SecsTraceFaultSampleCodec(
        bool allowPayloadRecords = false,
        int maxBodyBytes = DefaultMaxBodyBytes,
        int maxRecordCount = DefaultMaxRecordCount,
        int maxTextLength = DefaultMaxTextLength)
    {
        if (maxBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBodyBytes), maxBodyBytes, "The maximum body size must be positive.");
        if (maxRecordCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRecordCount), maxRecordCount, "The maximum record count must be positive.");
        if (maxTextLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTextLength), maxTextLength, "The maximum text length must be positive.");

        AllowPayloadRecords = allowPayloadRecords;
        MaxBodyBytes = maxBodyBytes;
        MaxRecordCount = maxRecordCount;
        MaxTextLength = maxTextLength;
    }

    /// <summary>Gets whether payload-bearing records can be imported or exported.</summary>
    public bool AllowPayloadRecords { get; }

    /// <summary>Gets the maximum body bytes in one payload-bearing record.</summary>
    public int MaxBodyBytes { get; }

    /// <summary>Gets the maximum number of records in one fault-sample trace.</summary>
    public int MaxRecordCount { get; }

    /// <summary>Gets the maximum accepted or produced fault-sample trace length.</summary>
    public int MaxTextLength { get; }

    /// <summary>Encodes records in enumeration order using LF line endings.</summary>
    public string Encode(IEnumerable<SecsTraceFaultSampleRecord> records)
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
                throw new ArgumentException("The fault-sample trace sequence contains a null record.", nameof(records));
            if (++recordCount > MaxRecordCount)
                throw new InvalidOperationException($"The fault-sample trace record count exceeds the configured maximum {MaxRecordCount}.");

            EnsureRecordAllowed(record);
            AppendRecord(text, record);
            EnsureTextLength(text);
        }

        return text.ToString();
    }

    /// <summary>Strictly decodes one complete fault-sample trace file.</summary>
    public IReadOnlyList<SecsTraceFaultSampleRecord> Decode(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), text.Length, $"The fault-sample trace text length cannot exceed {MaxTextLength} characters.");

        return new Parser(text, this).Parse();
    }

    private void EnsureRecordAllowed(SecsTraceFaultSampleRecord record)
    {
        if (record.DataClassification !=
                SecsTraceFaultSampleDataClassification.MetadataOnly &&
            !AllowPayloadRecords)
        {
            throw new InvalidOperationException(
                "Payload-bearing fault samples require AllowPayloadRecords to be enabled explicitly.");
        }

        if (record.Body.Length > MaxBodyBytes)
        {
            throw new InvalidOperationException(
                $"The fault-sample body length {record.Body.Length} exceeds the configured maximum {MaxBodyBytes}.");
        }
    }

    private static void AppendRecord(
        StringBuilder text,
        SecsTraceFaultSampleRecord record)
    {
        text.Append("Fault ");
        text.Append(record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(record.Code);
        text.Append(' ');
        text.Append(record.State);
        text.Append(' ');
        text.Append(record.DataClassification);
        text.Append(' ');
        text.Append(record.Header.SessionId.ToString(CultureInfo.InvariantCulture));
        text.Append(' ');
        AppendByte(text, record.Header.HeaderByte2);
        text.Append(' ');
        AppendByte(text, record.Header.HeaderByte3);
        text.Append(' ');
        AppendByte(text, record.Header.PresentationType);
        text.Append(' ');
        AppendByte(text, (byte)record.Header.MessageType);
        text.Append(' ');
        text.Append("0x");
        text.Append(record.Header.SystemBytes.ToString("X8", CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(record.OriginalBodyLength.ToString(CultureInfo.InvariantCulture));
        text.Append(' ');
        AppendRanges(text, record.RedactionRanges);
        text.Append(' ');
        AppendBody(text, record.Body);
        text.Append('\n');
    }

    private static void AppendByte(StringBuilder text, byte value)
    {
        text.Append("0x");
        text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
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

    private static void AppendBody(StringBuilder text, ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            text.Append('-');
            return;
        }

        foreach (var value in body)
            text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
    }

    private void EnsureTextLength(StringBuilder text)
    {
        if (text.Length > MaxTextLength)
            throw new InvalidOperationException($"The fault-sample trace text length exceeds the configured maximum {MaxTextLength}.");
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly SecsTraceFaultSampleCodec _options;
        private int _index;
        private int _recordIndex;

        public Parser(string text, SecsTraceFaultSampleCodec options)
        {
            _text = text;
            _options = options;
        }

        public IReadOnlyList<SecsTraceFaultSampleRecord> Parse()
        {
            var headerOffset = _index;
            var formatIdentifier = ReadLine();
            if (!string.Equals(formatIdentifier, FormatIdentifier, StringComparison.Ordinal))
                throw Error("The fault-sample trace format identifier is missing or unsupported", -1, headerOffset);

            var records = new List<SecsTraceFaultSampleRecord>();
            while (!IsEnd)
            {
                if (_recordIndex >= _options.MaxRecordCount)
                    throw Error($"The fault-sample trace record count exceeds the configured maximum {_options.MaxRecordCount}", _recordIndex, _index);

                records.Add(ParseRecord());
                _recordIndex++;
            }

            return new ReadOnlyCollection<SecsTraceFaultSampleRecord>(records);
        }

        private SecsTraceFaultSampleRecord ParseRecord()
        {
            var headerOffset = _index;
            var fields = ReadLine().Split(new[] { ' ' }, StringSplitOptions.None);
            if (fields.Length != 14 || !string.Equals(fields[0], "Fault", StringComparison.Ordinal))
                throw Error("A fault-sample record must contain exactly fourteen single-space-separated fields", _recordIndex, headerOffset);

            ParseCode(fields[2], headerOffset);
            var classification = ParseEnum<SecsTraceFaultSampleDataClassification>(
                fields[4],
                "data classification",
                headerOffset);
            if (classification != SecsTraceFaultSampleDataClassification.MetadataOnly &&
                !_options.AllowPayloadRecords)
            {
                throw Error(
                    "Payload-bearing fault samples require AllowPayloadRecords to be enabled explicitly",
                    _recordIndex,
                    headerOffset);
            }

            var header = new HsmsMessageHeader
            {
                SessionId = ParseUInt16(fields[5], "protocol Session ID", headerOffset),
                HeaderByte2 = ParseByte(fields[6], "header byte 2", headerOffset),
                HeaderByte3 = ParseByte(fields[7], "header byte 3", headerOffset),
                PresentationType = ParseByte(fields[8], "PType", headerOffset),
                MessageType = (HsmsMessageType)ParseByte(fields[9], "SType", headerOffset),
                SystemBytes = ParseUInt32(fields[10], "System Bytes", headerOffset),
            };
            var body = ParseBody(fields[13], headerOffset);
            var ranges = ParseRanges(fields[12], headerOffset);

            try
            {
                return new SecsTraceFaultSampleRecord(
                    ParseTimestamp(fields[1], headerOffset),
                    ParseEnum<HsmsSessionState>(fields[3], "session state", headerOffset),
                    classification,
                    header,
                    ParseInt32(fields[11], "original body length", headerOffset),
                    body,
                    ranges);
            }
            catch (ArgumentException ex)
            {
                throw Error(
                    "The fault-sample data boundary is invalid",
                    _recordIndex,
                    headerOffset,
                    ex);
            }
        }

        private void ParseCode(string value, int offset)
        {
            if (!string.Equals(
                value,
                HsmsDiagnosticCode.DataMessageDecodeFailed.ToString(),
                StringComparison.Ordinal))
            {
                throw Error(
                    "The fault-sample diagnostic code must be DataMessageDecodeFailed",
                    _recordIndex,
                    offset);
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
                throw Error("The fault-sample timestamp must use the round-trip format", _recordIndex, offset);
            }

            return timestamp;
        }

        private TEnum ParseEnum<TEnum>(
            string value,
            string fieldName,
            int offset)
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

        private ushort ParseUInt16(
            string value,
            string fieldName,
            int offset)
        {
            if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw Error($"The {fieldName} must be an unsigned 16-bit integer", _recordIndex, offset);
            return parsed;
        }

        private int ParseInt32(
            string value,
            string fieldName,
            int offset)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw Error($"The {fieldName} must be a nonnegative 32-bit integer", _recordIndex, offset);
            return parsed;
        }

        private byte ParseByte(
            string value,
            string fieldName,
            int offset)
        {
            if (!HasExactHexForm(value, 2) ||
                !byte.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            {
                throw Error($"The {fieldName} must use the exact form 0xHH", _recordIndex, offset);
            }

            return parsed;
        }

        private uint ParseUInt32(
            string value,
            string fieldName,
            int offset)
        {
            if (!HasExactHexForm(value, 8) ||
                !uint.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            {
                throw Error($"The {fieldName} must use the exact form 0xHHHHHHHH", _recordIndex, offset);
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
                var rangeOffset = ParseCanonicalInt32(fields[0], "redaction offset", offset, allowZero: true);
                var length = ParseCanonicalInt32(fields[1], "redaction length", offset, allowZero: false);
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

        private int ParseCanonicalInt32(
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

        private byte[] ParseBody(string value, int offset)
        {
            if (string.Equals(value, "-", StringComparison.Ordinal))
                return Array.Empty<byte>();
            if (value.Length % 2 != 0)
                throw Error("The body must contain an even number of uppercase hexadecimal digits", _recordIndex, offset);

            var bodyLength = value.Length / 2;
            if (bodyLength > _options.MaxBodyBytes)
                throw Error($"The fault-sample body length exceeds the configured maximum {_options.MaxBodyBytes}", _recordIndex, offset);

            var body = new byte[bodyLength];
            for (var index = 0; index < body.Length; index++)
            {
                var high = ParseHexDigit(value[index * 2]);
                var low = ParseHexDigit(value[(index * 2) + 1]);
                if (high < 0 || low < 0)
                    throw Error("The body must contain only uppercase hexadecimal digits", _recordIndex, offset);
                body[index] = (byte)((high << 4) | low);
            }

            return body;
        }

        private static int ParseHexDigit(char value)
            => value switch
            {
                >= '0' and <= '9' => value - '0',
                >= 'A' and <= 'F' => value - 'A' + 10,
                _ => -1,
            };

        private static bool HasExactHexForm(string value, int digitCount)
        {
            if (value.Length != digitCount + 2 || value[0] != '0' || value[1] != 'x')
                return false;

            for (var index = 2; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') ||
                    (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

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
