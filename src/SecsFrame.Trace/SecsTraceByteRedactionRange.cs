using System.Runtime.InteropServices;

namespace SecsFrame.Trace;

/// <summary>Identifies a zero-based range within a fault-sample byte payload.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SecsTraceByteRedactionRange
{
    /// <summary>Creates a nonempty byte range.</summary>
    public SecsTraceByteRedactionRange(int offset, int length)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "The byte offset cannot be negative.");
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "The byte range length must be positive.");
        if (offset > int.MaxValue - length)
            throw new ArgumentOutOfRangeException(nameof(length), length, "The byte range end exceeds the supported integer range.");

        Offset = offset;
        Length = length;
    }

    /// <summary>Gets the zero-based body offset.</summary>
    public int Offset { get; }

    /// <summary>Gets the number of bytes in the range.</summary>
    public int Length { get; }
}
