using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace SecsFrame.Trace;

/// <summary>Encodes and decodes restricted-field control-message trace files.</summary>
public sealed class SecsTraceControlCodec
{
    /// <summary>The exact control-message trace format identifier.</summary>
    public const string FormatIdentifier = "SecsFrame-ControlTrace/1";

    /// <summary>The default maximum number of records in one control trace.</summary>
    public const int DefaultMaxRecordCount = SecsTraceCodec.DefaultMaxRecordCount;

    /// <summary>The default maximum control trace text length.</summary>
    public const int DefaultMaxTextLength = SecsTraceCodec.DefaultMaxTextLength;

    /// <summary>Creates a control trace codec with explicit resource limits.</summary>
    public SecsTraceControlCodec(
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

    /// <summary>Gets the maximum number of records in one control trace.</summary>
    public int MaxRecordCount { get; }

    /// <summary>Gets the maximum accepted or produced control trace length.</summary>
    public int MaxTextLength { get; }

    /// <summary>Encodes records in enumeration order using LF line endings.</summary>
    public string Encode(IEnumerable<SecsTraceControlRecord> records)
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
                throw new ArgumentException("The control trace sequence contains a null record.", nameof(records));
            if (++recordCount > MaxRecordCount)
                throw new InvalidOperationException($"The control trace record count exceeds the configured maximum {MaxRecordCount}.");

            AppendRecord(text, record);
            EnsureTextLength(text);
        }

        return text.ToString();
    }

    /// <summary>Strictly decodes one complete control trace file.</summary>
    public IReadOnlyList<SecsTraceControlRecord> Decode(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), text.Length, $"The control trace text length cannot exceed {MaxTextLength} characters.");

        return new Parser(text, this).Parse();
    }

    private static void AppendRecord(StringBuilder text, SecsTraceControlRecord record)
    {
        text.Append("Control ");
        text.Append(record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(record.Direction == SecsTraceDirection.Sent ? "Sent" : "Received");
        text.Append(' ');
        text.Append(record.State);
        text.Append(' ');
        text.Append(record.ProtocolSessionId.ToString(CultureInfo.InvariantCulture));
        text.Append(' ');
        AppendByte(text, record.HeaderByte2);
        text.Append(' ');
        AppendByte(text, record.HeaderByte3);
        text.Append(' ');
        AppendByte(text, record.PresentationType);
        text.Append(' ');
        AppendByte(text, record.MessageType);
        text.Append(' ');
        text.Append("0x");
        text.Append(record.SystemBytes.ToString("X8", CultureInfo.InvariantCulture));
        text.Append('\n');
    }

    private static void AppendByte(StringBuilder text, byte value)
    {
        text.Append("0x");
        text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
    }

    private void EnsureTextLength(StringBuilder text)
    {
        if (text.Length > MaxTextLength)
            throw new InvalidOperationException($"The control trace text length exceeds the configured maximum {MaxTextLength}.");
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly SecsTraceControlCodec _options;
        private int _index;
        private int _recordIndex;

        public Parser(string text, SecsTraceControlCodec options)
        {
            _text = text;
            _options = options;
        }

        public IReadOnlyList<SecsTraceControlRecord> Parse()
        {
            var headerOffset = _index;
            var formatIdentifier = ReadLine();
            if (!string.Equals(formatIdentifier, FormatIdentifier, StringComparison.Ordinal))
                throw Error("The control trace format identifier is missing or unsupported", -1, headerOffset);

            var records = new List<SecsTraceControlRecord>();
            while (!IsEnd)
            {
                if (_recordIndex >= _options.MaxRecordCount)
                    throw Error($"The control trace record count exceeds the configured maximum {_options.MaxRecordCount}", _recordIndex, _index);

                records.Add(ParseRecord());
                _recordIndex++;
            }

            return new ReadOnlyCollection<SecsTraceControlRecord>(records);
        }

        private SecsTraceControlRecord ParseRecord()
        {
            var headerOffset = _index;
            var fields = ReadLine().Split(new[] { ' ' }, StringSplitOptions.None);
            if (fields.Length != 10 || !string.Equals(fields[0], "Control", StringComparison.Ordinal))
                throw Error("A control record must contain exactly ten single-space-separated fields", _recordIndex, headerOffset);

            var messageType = ParseByte(fields[8], "SType", headerOffset);
            if (messageType == (byte)HsmsMessageType.DataMessage)
                throw Error("A control-message SType must be nonzero", _recordIndex, headerOffset);

            return new SecsTraceControlRecord(
                ParseTimestamp(fields[1], headerOffset),
                ParseDirection(fields[2], headerOffset),
                ParseState(fields[3], headerOffset),
                ParseSessionId(fields[4], headerOffset),
                ParseByte(fields[5], "header byte 2", headerOffset),
                ParseByte(fields[6], "header byte 3", headerOffset),
                ParseByte(fields[7], "PType", headerOffset),
                messageType,
                ParseSystemBytes(fields[9], headerOffset));
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
                throw Error("The control timestamp must use the round-trip format", _recordIndex, offset);
            }

            return timestamp;
        }

        private SecsTraceDirection ParseDirection(string value, int offset)
            => value switch
            {
                "Sent" => SecsTraceDirection.Sent,
                "Received" => SecsTraceDirection.Received,
                _ => throw Error("The control direction must be Sent or Received", _recordIndex, offset),
            };

        private HsmsSessionState ParseState(string value, int offset)
        {
            if (!Enum.TryParse(value, ignoreCase: false, out HsmsSessionState parsed) ||
                !Enum.IsDefined(typeof(HsmsSessionState), parsed) ||
                !string.Equals(value, parsed.ToString(), StringComparison.Ordinal))
            {
                throw Error("The HSMS session state is unknown", _recordIndex, offset);
            }

            return parsed;
        }

        private ushort ParseSessionId(string value, int offset)
        {
            if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                throw Error("The protocol Session ID must be an unsigned 16-bit integer", _recordIndex, offset);
            return parsed;
        }

        private byte ParseByte(string value, string fieldName, int offset)
        {
            if (!HasExactHexForm(value, 2) ||
                !byte.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            {
                throw Error($"The {fieldName} must use the exact form 0xHH", _recordIndex, offset);
            }

            return parsed;
        }

        private uint ParseSystemBytes(string value, int offset)
        {
            if (!HasExactHexForm(value, 8) ||
                !uint.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            {
                throw Error("System Bytes must use the exact form 0xHHHHHHHH", _recordIndex, offset);
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
