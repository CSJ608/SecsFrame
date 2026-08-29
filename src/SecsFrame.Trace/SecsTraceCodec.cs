using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using SecsFrame.Sml;

namespace SecsFrame.Trace;

/// <summary>Encodes and decodes deterministic decoded-message trace files.</summary>
public sealed class SecsTraceCodec
{
    /// <summary>The exact trace format identifier.</summary>
    public const string FormatIdentifier = "SecsFrame-Trace/1";

    /// <summary>The default maximum number of records in one trace.</summary>
    public const int DefaultMaxRecordCount = 100_000;

    /// <summary>The default maximum trace text length.</summary>
    public const int DefaultMaxTextLength = 64 * 1024 * 1024;

    /// <summary>Creates a trace codec with explicit resource limits.</summary>
    public SecsTraceCodec(
        SmlMessageCodec? messageCodec = null,
        int maxRecordCount = DefaultMaxRecordCount,
        int maxTextLength = DefaultMaxTextLength)
    {
        if (maxRecordCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRecordCount), maxRecordCount, "The maximum record count must be positive.");
        if (maxTextLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTextLength), maxTextLength, "The maximum text length must be positive.");

        MessageCodec = messageCodec ?? new SmlMessageCodec();
        MaxRecordCount = maxRecordCount;
        MaxTextLength = maxTextLength;
    }

    /// <summary>Gets the SML codec used for each decoded data message.</summary>
    public SmlMessageCodec MessageCodec { get; }

    /// <summary>Gets the maximum number of records in one trace.</summary>
    public int MaxRecordCount { get; }

    /// <summary>Gets the maximum accepted or produced trace text length.</summary>
    public int MaxTextLength { get; }

    /// <summary>Encodes records in enumeration order using LF line endings.</summary>
    public string Encode(IEnumerable<SecsTraceRecord> records)
    {
        if (records is null)
            throw new ArgumentNullException(nameof(records));

        var text = new StringBuilder(FormatIdentifier);
        text.Append('\n');
        var recordCount = 0;
        foreach (var record in records)
        {
            if (record is null)
                throw new ArgumentException("The trace sequence contains a null record.", nameof(records));
            if (++recordCount > MaxRecordCount)
                throw new InvalidOperationException($"The trace record count exceeds the configured maximum {MaxRecordCount}.");

            var messageText = MessageCodec.Encode(record.Message);
            AppendRecordHeader(text, record, messageText.Length);
            text.Append(messageText);
            if (text.Length > MaxTextLength)
                throw new InvalidOperationException($"The trace text length exceeds the configured maximum {MaxTextLength}.");
        }

        return text.ToString();
    }

    /// <summary>Strictly decodes one complete trace file.</summary>
    public IReadOnlyList<SecsTraceRecord> Decode(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), text.Length, $"The trace text length cannot exceed {MaxTextLength} characters.");

        return new SecsTraceParser(text, this).Parse();
    }

    private static void AppendRecordHeader(StringBuilder text, SecsTraceRecord record, int messageLength)
    {
        text.Append("Record ");
        text.Append(record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(record.Direction == SecsTraceDirection.Sent ? "Sent" : "Received");
        text.Append(' ');
        text.Append(record.SessionId.HasValue
            ? record.SessionId.Value.ToString(CultureInfo.InvariantCulture)
            : "-");
        text.Append(' ');
        text.Append(record.SystemBytes.HasValue
            ? "0x" + record.SystemBytes.Value.ToString("X8", CultureInfo.InvariantCulture)
            : "-");
        text.Append(' ');
        text.Append(messageLength.ToString(CultureInfo.InvariantCulture));
        text.Append('\n');
    }

    private sealed class SecsTraceParser
    {
    private readonly string _text;
    private readonly SecsTraceCodec _options;
    private int _index;
    private int _recordIndex;

    public SecsTraceParser(string text, SecsTraceCodec options)
    {
        _text = text;
        _options = options;
    }

    public IReadOnlyList<SecsTraceRecord> Parse()
    {
        var headerOffset = _index;
        var formatIdentifier = ReadLine();
        if (!string.Equals(formatIdentifier, SecsTraceCodec.FormatIdentifier, StringComparison.Ordinal))
            throw Error("The trace format identifier is missing or unsupported", recordIndex: -1, headerOffset);

        var records = new List<SecsTraceRecord>();
        while (!IsEnd)
        {
            if (_recordIndex >= _options.MaxRecordCount)
                throw Error($"The trace record count exceeds the configured maximum {_options.MaxRecordCount}", _recordIndex, _index);

            records.Add(ParseRecord());
            _recordIndex++;
        }

        return new ReadOnlyCollection<SecsTraceRecord>(records);
    }

    private SecsTraceRecord ParseRecord()
    {
        var headerOffset = _index;
        var header = ReadLine();
        var fields = header.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 6 || !string.Equals(fields[0], "Record", StringComparison.Ordinal))
            throw Error("A record header must contain exactly six fields", _recordIndex, headerOffset);

        var timestamp = ParseTimestamp(fields[1], headerOffset);
        var direction = ParseDirection(fields[2], headerOffset);
        var sessionId = ParseSessionId(fields[3], headerOffset);
        var systemBytes = ParseSystemBytes(fields[4], headerOffset);
        var messageLength = ParseMessageLength(fields[5], headerOffset);
        if (messageLength > _text.Length - _index)
            throw Error("The SML block is shorter than its declared character length", _recordIndex, _index);

        var messageOffset = _index;
        var messageText = _text.Substring(_index, messageLength);
        _index += messageLength;
        SecsMessage message;
        try
        {
            message = _options.MessageCodec.Decode(messageText);
        }
        catch (SmlParseException ex)
        {
            throw Error("The record contains invalid SML", _recordIndex, messageOffset + ex.Offset, ex);
        }

        return new SecsTraceRecord(timestamp, direction, message, sessionId, systemBytes);
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
            throw Error("The record timestamp must use the round-trip format", _recordIndex, offset);
        }

        return timestamp;
    }

    private SecsTraceDirection ParseDirection(string value, int offset)
        => value switch
        {
            "Sent" => SecsTraceDirection.Sent,
            "Received" => SecsTraceDirection.Received,
            _ => throw Error("The record direction must be Sent or Received", _recordIndex, offset),
        };

    private ushort? ParseSessionId(string value, int offset)
    {
        if (string.Equals(value, "-", StringComparison.Ordinal))
            return null;
        if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId))
            throw Error("The protocol Session ID must be '-' or an unsigned 16-bit integer", _recordIndex, offset);
        return sessionId;
    }

    private uint? ParseSystemBytes(string value, int offset)
    {
        if (string.Equals(value, "-", StringComparison.Ordinal))
            return null;
        if (value.Length != 10 || value[0] != '0' || value[1] != 'x')
            throw Error("System Bytes must be '-' or use the exact form 0xHHHHHHHH", _recordIndex, offset);

        for (var index = 2; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9') || (character >= 'A' && character <= 'F')))
                throw Error("System Bytes contain an invalid uppercase hexadecimal digit", _recordIndex, offset);
        }

        if (!uint.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var systemBytes))
            throw Error("System Bytes are outside the unsigned 32-bit range", _recordIndex, offset);
        return systemBytes;
    }

    private int ParseMessageLength(string value, int offset)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length <= 0)
            throw Error("The SML character length must be a positive integer", _recordIndex, offset);
        if (length > _options.MessageCodec.MaxTextLength)
            throw Error($"The SML character length exceeds the configured maximum {_options.MessageCodec.MaxTextLength}", _recordIndex, offset);
        return length;
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
