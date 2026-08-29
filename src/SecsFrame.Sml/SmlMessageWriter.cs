using System.Globalization;
using System.Text;

namespace SecsFrame.Sml;

internal sealed class SmlMessageWriter
{
    private readonly SmlMessageCodec _options;
    private readonly StringBuilder _text = new();
    private int _itemCount;

    public SmlMessageWriter(SmlMessageCodec options)
        => _options = options;

    public string Write(SecsMessage message)
    {
        _text.Append("'S");
        _text.Append(message.Stream.ToString(CultureInfo.InvariantCulture));
        _text.Append('F');
        _text.Append(message.Function.ToString(CultureInfo.InvariantCulture));
        _text.Append('\'');
        if (message.ReplyExpected)
            _text.Append('W');
        _text.Append('\n');

        if (message.RootItem is not null)
            WriteItem(message.RootItem, depth: 1);

        _text.Append(".\n");
        EnsureTextLength();
        return _text.ToString();
    }

    private void WriteItem(SecsItem item, int depth)
    {
        if (depth > _options.MaxNestingDepth)
            throw new InvalidOperationException($"The Item nesting depth exceeds the configured maximum {_options.MaxNestingDepth}.");
        if (++_itemCount > _options.MaxItemCount)
            throw new InvalidOperationException($"The Item count exceeds the configured maximum {_options.MaxItemCount}.");
        if (item.Format != SecsItemFormat.List && item.Count > _options.MaxValueCount)
            throw new InvalidOperationException($"The Item value count exceeds the configured maximum {_options.MaxValueCount}.");

        AppendIndent(depth - 1);
        _text.Append('<');
        _text.Append(GetFormatName(item.Format));
        _text.Append(" [");
        _text.Append(item.Count.ToString(CultureInfo.InvariantCulture));
        _text.Append(']');

        if (item.Format == SecsItemFormat.List)
        {
            _text.Append('\n');
            foreach (var child in item.Items)
                WriteItem(child, depth + 1);
            AppendIndent(depth - 1);
        }
        else if (item.Format == SecsItemFormat.Ascii || item.Count != 0)
        {
            _text.Append(' ');
            WriteValues(item);
        }

        _text.Append(">\n");
        EnsureTextLength();
    }

    private void WriteValues(SecsItem item)
    {
        switch (item.Format)
        {
            case SecsItemFormat.Binary:
            case SecsItemFormat.Jis8:
                WriteHexValues(item.GetValues<byte>());
                break;
            case SecsItemFormat.Boolean:
                WriteBooleanValues(item.GetValues<bool>());
                break;
            case SecsItemFormat.Ascii:
                WriteAscii(item.GetString());
                break;
            case SecsItemFormat.I8:
                WriteIntegerValues(item.GetValues<long>());
                break;
            case SecsItemFormat.I1:
                WriteIntegerValues(item.GetValues<sbyte>());
                break;
            case SecsItemFormat.I2:
                WriteIntegerValues(item.GetValues<short>());
                break;
            case SecsItemFormat.I4:
                WriteIntegerValues(item.GetValues<int>());
                break;
            case SecsItemFormat.F8:
                WriteFloatValues(item.GetValues<double>());
                break;
            case SecsItemFormat.F4:
                WriteFloatValues(item.GetValues<float>());
                break;
            case SecsItemFormat.U8:
                WriteIntegerValues(item.GetValues<ulong>());
                break;
            case SecsItemFormat.U1:
                WriteIntegerValues(item.GetValues<byte>());
                break;
            case SecsItemFormat.U2:
                WriteIntegerValues(item.GetValues<ushort>());
                break;
            case SecsItemFormat.U4:
                WriteIntegerValues(item.GetValues<uint>());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item.Format, "Unknown SECS-II Item format.");
        }
    }

    private void WriteAscii(string value)
    {
        _text.Append('\'');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    _text.Append("\\\\");
                    break;
                case '\'':
                    _text.Append("\\'");
                    break;
                case '\r':
                    _text.Append("\\r");
                    break;
                case '\n':
                    _text.Append("\\n");
                    break;
                case '\t':
                    _text.Append("\\t");
                    break;
                default:
                    if (character >= 0x20 && character <= 0x7E)
                    {
                        _text.Append(character);
                    }
                    else
                    {
                        _text.Append("\\x");
                        AppendHexByte((byte)character);
                    }
                    break;
            }
        }
        _text.Append('\'');
    }

    private void WriteHexValues(ReadOnlySpan<byte> values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
                _text.Append(' ');
            _text.Append("0x");
            AppendHexByte(values[index]);
        }
    }

    private void AppendHexByte(byte value)
    {
        const string hex = "0123456789ABCDEF";
        _text.Append(hex[value >> 4]);
        _text.Append(hex[value & 0x0F]);
    }

    private void WriteBooleanValues(ReadOnlySpan<bool> values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
                _text.Append(' ');
            _text.Append(values[index] ? "True" : "False");
        }
    }

    private void WriteIntegerValues<T>(ReadOnlySpan<T> values)
        where T : struct, IFormattable
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
                _text.Append(' ');
            _text.Append(values[index].ToString(null, CultureInfo.InvariantCulture));
        }
    }

    private void WriteFloatValues(ReadOnlySpan<float> values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
                _text.Append(' ');
            _text.Append(values[index].ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private void WriteFloatValues(ReadOnlySpan<double> values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
                _text.Append(' ');
            _text.Append(values[index].ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private void AppendIndent(int level)
        => _text.Append(' ', checked(level * _options.IndentSize));

    private void EnsureTextLength()
    {
        if (_text.Length > _options.MaxTextLength)
            throw new InvalidOperationException($"The SML text length exceeds the configured maximum {_options.MaxTextLength}.");
    }

    private static string GetFormatName(SecsItemFormat format)
        => format switch
        {
            SecsItemFormat.List => "L",
            SecsItemFormat.Binary => "B",
            SecsItemFormat.Boolean => "Boolean",
            SecsItemFormat.Ascii => "A",
            SecsItemFormat.Jis8 => "J",
            SecsItemFormat.I8 => "I8",
            SecsItemFormat.I1 => "I1",
            SecsItemFormat.I2 => "I2",
            SecsItemFormat.I4 => "I4",
            SecsItemFormat.F8 => "F8",
            SecsItemFormat.F4 => "F4",
            SecsItemFormat.U8 => "U8",
            SecsItemFormat.U1 => "U1",
            SecsItemFormat.U2 => "U2",
            SecsItemFormat.U4 => "U4",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown SECS-II Item format."),
        };
}
