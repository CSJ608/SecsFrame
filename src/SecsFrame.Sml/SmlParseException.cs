namespace SecsFrame.Sml;

/// <summary>Reports a strict SML parsing failure with source coordinates.</summary>
public sealed class SmlParseException : FormatException
{
    internal SmlParseException(string message, int offset, int line, int column)
        : base($"{message} (line {line}, column {column}, offset {offset}).")
    {
        Offset = offset;
        Line = line;
        Column = column;
    }

    /// <summary>Gets the zero-based character offset.</summary>
    public int Offset { get; }

    /// <summary>Gets the one-based line number.</summary>
    public int Line { get; }

    /// <summary>Gets the one-based column number.</summary>
    public int Column { get; }
}
