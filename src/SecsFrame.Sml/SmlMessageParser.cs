using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SecsFrame.Sml;

internal sealed class SmlMessageParser
{
    private readonly string _text;
    private readonly SmlMessageCodec _options;
    private int _index;
    private int _line = 1;
    private int _column = 1;
    private int _itemCount;

    public SmlMessageParser(string text, SmlMessageCodec options)
    {
        _text = text;
        _options = options;
    }

    public SecsMessage Parse()
    {
        SkipWhitespace();
        var (stream, function, replyExpected) = ParseHeader();
        SkipWhitespace();

        SecsItem? rootItem = null;
        if (!IsAt('.'))
            rootItem = ParseItem(depth: 1);

        SkipWhitespace();
        Expect('.', "the message terminator");
        SkipWhitespace();
        if (!IsEnd)
            throw Error("A complete SML message was followed by trailing text");

        return new SecsMessage(stream, function, replyExpected, rootItem);
    }

    private (byte Stream, byte Function, bool ReplyExpected) ParseHeader()
    {
        Expect('\'', "the opening message quote");
        Expect('S', "an uppercase S in the message header");
        var stream = ReadDecimal("the stream number", byte.MaxValue);
        if (stream > 0x7F)
            throw Error("The stream number must be between 0 and 127");

        Expect('F', "an uppercase F in the message header");
        var function = ReadDecimal("the function number", byte.MaxValue);
        Expect('\'', "the closing message quote");
        SkipWhitespace();

        var replyExpected = false;
        if (IsAt('W'))
        {
            Advance();
            replyExpected = true;
        }

        return ((byte)stream, (byte)function, replyExpected);
    }

    private SecsItem ParseItem(int depth)
    {
        if (depth > _options.MaxNestingDepth)
            throw Error($"The Item nesting depth exceeds the configured maximum {_options.MaxNestingDepth}");
        if (++_itemCount > _options.MaxItemCount)
            throw Error($"The Item count exceeds the configured maximum {_options.MaxItemCount}");

        SkipWhitespace();
        Expect('<', "an Item opening delimiter");
        SkipWhitespace();
        var formatToken = ReadToken("an Item format", stopAtOpenBracket: true);
        var format = ParseFormat(formatToken);
        SkipWhitespace();
        Expect('[', "an Item count opening bracket");
        var count = ReadDecimal("the Item count", SecsItem.MaxEncodedLength);
        SkipWhitespace();
        Expect(']', "an Item count closing bracket");

        if (format == SecsItemFormat.List)
            return ParseList(count, depth);
        if (count > _options.MaxValueCount)
            throw Error($"The Item value count exceeds the configured maximum {_options.MaxValueCount}");

        return ParsePrimitive(format, count);
    }

    private SecsItem ParseList(int count, int depth)
    {
        if (count > _options.MaxItemCount - _itemCount)
            throw Error($"The declared List count exceeds the configured maximum Item count {_options.MaxItemCount}");

        var items = new SecsItem[count];
        for (var index = 0; index < items.Length; index++)
        {
            SkipWhitespace();
            items[index] = ParseItem(depth + 1);
        }

        SkipWhitespace();
        Expect('>', "the List closing delimiter");
        return SecsItem.List(items);
    }

    private SecsItem ParsePrimitive(SecsItemFormat format, int count)
    {
        SkipWhitespace();
        SecsItem item = format switch
        {
            SecsItemFormat.Binary => SecsItem.Binary(ReadValues(count, ParseHexByte)),
            SecsItemFormat.Boolean => SecsItem.Boolean(ReadValues(count, ParseBoolean)),
            SecsItemFormat.Ascii => ParseAscii(count),
            SecsItemFormat.Jis8 => SecsItem.Jis8(ReadValues(count, ParseHexByte)),
            SecsItemFormat.I8 => SecsItem.I8(ReadValues(count, ParseInt64)),
            SecsItemFormat.I1 => SecsItem.I1(ReadValues(count, ParseSByte)),
            SecsItemFormat.I2 => SecsItem.I2(ReadValues(count, ParseInt16)),
            SecsItemFormat.I4 => SecsItem.I4(ReadValues(count, ParseInt32)),
            SecsItemFormat.F8 => SecsItem.F8(ReadValues(count, ParseDouble)),
            SecsItemFormat.F4 => SecsItem.F4(ReadValues(count, ParseSingle)),
            SecsItemFormat.U8 => SecsItem.U8(ReadValues(count, ParseUInt64)),
            SecsItemFormat.U1 => SecsItem.U1(ReadValues(count, ParseByte)),
            SecsItemFormat.U2 => SecsItem.U2(ReadValues(count, ParseUInt16)),
            SecsItemFormat.U4 => SecsItem.U4(ReadValues(count, ParseUInt32)),
            _ => throw Error($"Unsupported Item format {format}"),
        };

        SkipWhitespace();
        Expect('>', "the Item closing delimiter");
        return item;
    }

    private SecsItem ParseAscii(int expectedCount)
    {
        Expect('\'', "an ASCII opening quote");
        var value = new StringBuilder(expectedCount);
        while (!IsEnd && !IsAt('\''))
        {
            var character = Current;
            if (character == '\r' || character == '\n')
                throw Error("ASCII text cannot contain an unescaped line break");

            Advance();
            if (character != '\\')
            {
                if (character > 0x7F)
                    throw Error("ASCII text contains a character outside the seven-bit range");
                value.Append(character);
                continue;
            }

            value.Append(ParseAsciiEscape());
        }

        Expect('\'', "an ASCII closing quote");
        if (value.Length != expectedCount)
            throw Error($"The ASCII value count {value.Length} does not match the declared count {expectedCount}");

        return SecsItem.Ascii(value.ToString());
    }

    private char ParseAsciiEscape()
    {
        if (IsEnd)
            throw Error("An ASCII escape sequence is incomplete");

        var escape = Current;
        Advance();
        return escape switch
        {
            '\\' => '\\',
            '\'' => '\'',
            'r' => '\r',
            'n' => '\n',
            't' => '\t',
            'x' => ParseAsciiHexEscape(),
            _ => throw Error($"Unsupported ASCII escape sequence \\{escape}"),
        };
    }

    private char ParseAsciiHexEscape()
    {
        var high = ReadHexDigit("the first ASCII hexadecimal escape digit");
        var low = ReadHexDigit("the second ASCII hexadecimal escape digit");
        var value = (high << 4) | low;
        if (value > 0x7F)
            throw Error("An ASCII hexadecimal escape must remain in the seven-bit range");
        return (char)value;
    }

    private T[] ReadValues<T>(int count, Func<Token, T> parse)
    {
        var values = new T[count];
        for (var index = 0; index < values.Length; index++)
        {
            SkipWhitespace();
            values[index] = parse(ReadToken("an Item value", stopAtOpenBracket: false));
        }
        return values;
    }

    private byte ParseHexByte(Token token)
    {
        if (token.Value.Length != 4 || token.Value[0] != '0' || token.Value[1] != 'x')
            throw ErrorAt(token, "A Binary or JIS-8 value must use the exact form 0xHH");

        var high = ParseHexDigit(token.Value[2]);
        var low = ParseHexDigit(token.Value[3]);
        if (high < 0 || low < 0)
            throw ErrorAt(token, "A Binary or JIS-8 value contains an invalid hexadecimal digit");
        return (byte)((high << 4) | low);
    }

    private bool ParseBoolean(Token token)
        => token.Value switch
        {
            "True" => true,
            "False" => false,
            _ => throw ErrorAt(token, "A Boolean value must be True or False"),
        };

    private sbyte ParseSByte(Token token)
        => sbyte.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The I1 value is outside its signed 8-bit range");

    private short ParseInt16(Token token)
        => short.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The I2 value is outside its signed 16-bit range");

    private int ParseInt32(Token token)
        => int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The I4 value is outside its signed 32-bit range");

    private long ParseInt64(Token token)
        => long.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The I8 value is outside its signed 64-bit range");

    private byte ParseByte(Token token)
        => byte.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The U1 value is outside its unsigned 8-bit range");

    private ushort ParseUInt16(Token token)
        => ushort.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The U2 value is outside its unsigned 16-bit range");

    private uint ParseUInt32(Token token)
        => uint.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The U4 value is outside its unsigned 32-bit range");

    private ulong ParseUInt64(Token token)
        => ulong.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The U8 value is outside its unsigned 64-bit range");

    private float ParseSingle(Token token)
        => float.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The F4 value is not a valid invariant single-precision number");

    private double ParseDouble(Token token)
        => double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ErrorAt(token, "The F8 value is not a valid invariant double-precision number");

    private SecsItemFormat ParseFormat(Token token)
        => token.Value switch
        {
            "L" => SecsItemFormat.List,
            "B" => SecsItemFormat.Binary,
            "Boolean" => SecsItemFormat.Boolean,
            "A" => SecsItemFormat.Ascii,
            "J" => SecsItemFormat.Jis8,
            "I8" => SecsItemFormat.I8,
            "I1" => SecsItemFormat.I1,
            "I2" => SecsItemFormat.I2,
            "I4" => SecsItemFormat.I4,
            "F8" => SecsItemFormat.F8,
            "F4" => SecsItemFormat.F4,
            "U8" => SecsItemFormat.U8,
            "U1" => SecsItemFormat.U1,
            "U2" => SecsItemFormat.U2,
            "U4" => SecsItemFormat.U4,
            _ => throw ErrorAt(token, $"Unknown Item format {token.Value}"),
        };

    private int ReadDecimal(string description, int maximum)
    {
        SkipWhitespace();
        var start = CapturePosition();
        var value = 0;
        var digitCount = 0;
        while (!IsEnd && Current >= '0' && Current <= '9')
        {
            var digit = Current - '0';
            if (value > (maximum - digit) / 10)
                throw ErrorAt(start, $"{description} exceeds {maximum}");
            value = (value * 10) + digit;
            digitCount++;
            Advance();
        }

        if (digitCount == 0)
            throw ErrorAt(start, $"Expected {description}");
        return value;
    }

    private Token ReadToken(string description, bool stopAtOpenBracket)
    {
        var start = CapturePosition();
        while (!IsEnd && !char.IsWhiteSpace(Current) && Current != '>' && (!stopAtOpenBracket || Current != '['))
            Advance();
        if (_index == start.Offset)
            throw ErrorAt(start, $"Expected {description}");
        return new Token(_text.Substring(start.Offset, _index - start.Offset), start.Offset, start.Line, start.Column);
    }

    private int ReadHexDigit(string description)
    {
        if (IsEnd)
            throw Error($"Expected {description}");
        var value = ParseHexDigit(Current);
        if (value < 0)
            throw Error($"Expected {description}");
        Advance();
        return value;
    }

    private void Expect(char expected, string description)
    {
        if (IsEnd || Current != expected)
            throw Error($"Expected {description}");
        Advance();
    }

    private void SkipWhitespace()
    {
        while (!IsEnd && char.IsWhiteSpace(Current))
            Advance();
    }

    private void Advance()
    {
        if (Current == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        _index++;
    }

    private Position CapturePosition()
        => new(_index, _line, _column);

    private SmlParseException Error(string message)
        => new(message, _index, _line, _column);

    private static SmlParseException ErrorAt(Position position, string message)
        => new(message, position.Offset, position.Line, position.Column);

    private static SmlParseException ErrorAt(Token token, string message)
        => new(message, token.Offset, token.Line, token.Column);

    private bool IsAt(char value)
        => !IsEnd && Current == value;

    private bool IsEnd => _index >= _text.Length;

    private char Current => _text[_index];

    private static int ParseHexDigit(char value)
    {
        if (value >= '0' && value <= '9')
            return value - '0';
        if (value >= 'A' && value <= 'F')
            return value - 'A' + 10;
        return -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Position
    {
        public Position(int offset, int line, int column)
        {
            Offset = offset;
            Line = line;
            Column = column;
        }

        public int Offset { get; }

        public int Line { get; }

        public int Column { get; }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct Token
    {
        public Token(string value, int offset, int line, int column)
        {
            Value = value;
            Offset = offset;
            Line = line;
            Column = column;
        }

        public string Value { get; }

        public int Offset { get; }

        public int Line { get; }

        public int Column { get; }
    }
}
