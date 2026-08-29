namespace SecsFrame.Trace;

/// <summary>Reports a strict trace envelope parsing failure.</summary>
public sealed class SecsTraceParseException : FormatException
{
    internal SecsTraceParseException(string message, int recordIndex, int offset, Exception? innerException = null)
        : base($"{message} (record {recordIndex}, offset {offset}).", innerException)
    {
        RecordIndex = recordIndex;
        Offset = offset;
    }

    /// <summary>Gets the zero-based record index, or -1 for the file header.</summary>
    public int RecordIndex { get; }

    /// <summary>Gets the zero-based character offset.</summary>
    public int Offset { get; }
}
