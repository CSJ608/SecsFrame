using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace SecsFrame.Trace;

/// <summary>Encodes and decodes restricted-field structured-diagnostic trace files.</summary>
public sealed class SecsTraceDiagnosticCodec
{
    /// <summary>The exact diagnostic trace format identifier.</summary>
    public const string FormatIdentifier = "SecsFrame-DiagnosticTrace/1";

    /// <summary>The default maximum number of records in one diagnostic trace.</summary>
    public const int DefaultMaxRecordCount = SecsTraceCodec.DefaultMaxRecordCount;

    /// <summary>The default maximum diagnostic trace text length.</summary>
    public const int DefaultMaxTextLength = SecsTraceCodec.DefaultMaxTextLength;

    /// <summary>Creates a diagnostic trace codec with explicit resource limits.</summary>
    public SecsTraceDiagnosticCodec(
        int maxRecordCount = DefaultMaxRecordCount,
        int maxTextLength = DefaultMaxTextLength)
    {
        if (maxRecordCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRecordCount), maxRecordCount, "The maximum record count must be positive.");
        if (maxTextLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTextLength), maxTextLength, "The maximum text length must be positive.");

        MaxRecordCount = maxRecordCount;
        MaxTextLength = maxTextLength;
    }

    /// <summary>Gets the maximum number of records in one diagnostic trace.</summary>
    public int MaxRecordCount { get; }

    /// <summary>Gets the maximum accepted or produced diagnostic trace length.</summary>
    public int MaxTextLength { get; }

    /// <summary>Encodes records in enumeration order using LF line endings.</summary>
    public string Encode(IEnumerable<SecsTraceDiagnosticRecord> records)
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
                throw new ArgumentException("The diagnostic trace sequence contains a null record.", nameof(records));
            if (++recordCount > MaxRecordCount)
                throw new InvalidOperationException($"The diagnostic trace record count exceeds the configured maximum {MaxRecordCount}.");

            AppendRecord(text, record);
            EnsureTextLength(text);
        }

        return text.ToString();
    }

    /// <summary>Strictly decodes one complete diagnostic trace file.</summary>
    public IReadOnlyList<SecsTraceDiagnosticRecord> Decode(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), text.Length, $"The diagnostic trace text length cannot exceed {MaxTextLength} characters.");

        return new Parser(text, this).Parse();
    }

    private static void AppendRecord(StringBuilder text, SecsTraceDiagnosticRecord record)
    {
        text.Append("Diagnostic ");
        text.Append(record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(record.Code);
        text.Append(' ');
        text.Append(record.Layer);
        text.Append(' ');
        text.Append(record.Operation);
        text.Append(' ');
        text.Append(record.State);
        text.Append(' ');
        text.Append(record.Timer.HasValue ? record.Timer.Value.ToString() : "-");
        text.Append(' ');
        text.Append(record.ProtocolSessionId.HasValue
            ? record.ProtocolSessionId.Value.ToString(CultureInfo.InvariantCulture)
            : "-");
        text.Append(' ');
        text.Append(FormatOptionalUInt32(record.SystemBytes));
        text.Append(' ');
        text.Append(FormatOptionalByte(record.PeerStatus));
        text.Append(' ');
        text.Append(FormatOptionalByte(record.RejectedMessageType));
        text.Append('\n');
    }

    private static string FormatOptionalUInt32(uint? value)
        => value.HasValue
            ? "0x" + value.Value.ToString("X8", CultureInfo.InvariantCulture)
            : "-";

    private static string FormatOptionalByte(byte? value)
        => value.HasValue
            ? "0x" + value.Value.ToString("X2", CultureInfo.InvariantCulture)
            : "-";

    private void EnsureTextLength(StringBuilder text)
    {
        if (text.Length > MaxTextLength)
            throw new InvalidOperationException($"The diagnostic trace text length exceeds the configured maximum {MaxTextLength}.");
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly SecsTraceDiagnosticCodec _options;
        private int _index;
        private int _recordIndex;

        public Parser(string text, SecsTraceDiagnosticCodec options)
        {
            _text = text;
            _options = options;
        }

        public IReadOnlyList<SecsTraceDiagnosticRecord> Parse()
        {
            var headerOffset = _index;
            var formatIdentifier = ReadLine();
            if (!string.Equals(formatIdentifier, FormatIdentifier, StringComparison.Ordinal))
                throw Error("The diagnostic trace format identifier is missing or unsupported", -1, headerOffset);

            var records = new List<SecsTraceDiagnosticRecord>();
            while (!IsEnd)
            {
                if (_recordIndex >= _options.MaxRecordCount)
                    throw Error($"The diagnostic trace record count exceeds the configured maximum {_options.MaxRecordCount}", _recordIndex, _index);

                records.Add(ParseRecord());
                _recordIndex++;
            }

            return new ReadOnlyCollection<SecsTraceDiagnosticRecord>(records);
        }

        private SecsTraceDiagnosticRecord ParseRecord()
        {
            var headerOffset = _index;
            var fields = ReadLine().Split(new[] { ' ' }, StringSplitOptions.None);
            if (fields.Length != 11 || !string.Equals(fields[0], "Diagnostic", StringComparison.Ordinal))
                throw Error("A diagnostic record must contain exactly eleven single-space-separated fields", _recordIndex, headerOffset);

            return new SecsTraceDiagnosticRecord(
                ParseTimestamp(fields[1], headerOffset),
                ParseEnum<HsmsDiagnosticCode>(fields[2], "diagnostic code", headerOffset),
                ParseEnum<HsmsDiagnosticLayer>(fields[3], "diagnostic layer", headerOffset),
                ParseEnum<HsmsOperation>(fields[4], "diagnostic operation", headerOffset),
                ParseEnum<HsmsSessionState>(fields[5], "session state", headerOffset),
                ParseOptionalEnum<HsmsTimer>(fields[6], "timer", headerOffset),
                ParseOptionalUInt16(fields[7], "protocol Session ID", headerOffset),
                ParseOptionalUInt32(fields[8], "System Bytes", headerOffset),
                ParseOptionalByte(fields[9], "peer status", headerOffset),
                ParseOptionalByte(fields[10], "rejected message type", headerOffset));
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
                throw Error("The diagnostic timestamp must use the round-trip format", _recordIndex, offset);
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

        private TEnum? ParseOptionalEnum<TEnum>(string value, string fieldName, int offset)
            where TEnum : struct, Enum
            => string.Equals(value, "-", StringComparison.Ordinal)
                ? null
                : ParseEnum<TEnum>(value, fieldName, offset);

        private ushort? ParseOptionalUInt16(string value, string fieldName, int offset)
        {
            if (string.Equals(value, "-", StringComparison.Ordinal))
                return null;
            if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw Error($"The {fieldName} must be '-' or an unsigned 16-bit integer", _recordIndex, offset);
            return parsed;
        }

        private uint? ParseOptionalUInt32(string value, string fieldName, int offset)
        {
            if (string.Equals(value, "-", StringComparison.Ordinal))
                return null;
            if (!HasExactHexForm(value, 8) ||
                !uint.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            {
                throw Error($"The {fieldName} must be '-' or use the exact form 0xHHHHHHHH", _recordIndex, offset);
            }

            return parsed;
        }

        private byte? ParseOptionalByte(string value, string fieldName, int offset)
        {
            if (string.Equals(value, "-", StringComparison.Ordinal))
                return null;
            if (!HasExactHexForm(value, 2) ||
                !byte.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            {
                throw Error($"The {fieldName} must be '-' or use the exact form 0xHH", _recordIndex, offset);
            }

            return parsed;
        }

        private static bool HasExactHexForm(string value, int digitCount)
        {
            if (value.Length != digitCount + 2 || value[0] != '0' || value[1] != 'x')
                return false;

            for (var index = 2; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'A' && character <= 'F')))
                    return false;
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

        private static SecsTraceParseException Error(string message, int recordIndex, int offset)
            => new(message, recordIndex, offset);
    }
}
