namespace SecsFrame.Sml;

/// <summary>
/// Encodes and decodes the deterministic SecsFrame SML debug profile.
/// </summary>
/// <remarks>
/// This is a non-normative diagnostic representation. It does not claim SEMI
/// SML conformance.
/// </remarks>
public sealed class SmlMessageCodec
{
    /// <summary>The default number of spaces added for each nested Item.</summary>
    public const int DefaultIndentSize = 4;

    /// <summary>The default maximum root-inclusive Item depth.</summary>
    public const int DefaultMaxNestingDepth = SecsItemCodec.DefaultMaxNestingDepth;

    /// <summary>The default maximum number of Items in one message.</summary>
    public const int DefaultMaxItemCount = SecsItemCodec.DefaultMaxItemCount;

    /// <summary>The default maximum primitive values in one Item.</summary>
    public const int DefaultMaxValueCount = 1_000_000;

    /// <summary>The default maximum input or output text length.</summary>
    public const int DefaultMaxTextLength = 16 * 1024 * 1024;

    /// <summary>Creates a deterministic SML debug codec with explicit resource limits.</summary>
    public SmlMessageCodec(
        int indentSize = DefaultIndentSize,
        int maxNestingDepth = DefaultMaxNestingDepth,
        int maxItemCount = DefaultMaxItemCount,
        int maxValueCount = DefaultMaxValueCount,
        int maxTextLength = DefaultMaxTextLength)
    {
        if (indentSize <= 0 || indentSize > 16)
            throw new ArgumentOutOfRangeException(nameof(indentSize), indentSize, "The indent size must be between 1 and 16.");
        if (maxNestingDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxNestingDepth), maxNestingDepth, "The maximum depth must be positive.");
        if (maxItemCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItemCount), maxItemCount, "The maximum Item count must be positive.");
        if (maxValueCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxValueCount), maxValueCount, "The maximum value count must be positive.");
        if (maxTextLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTextLength), maxTextLength, "The maximum text length must be positive.");

        IndentSize = indentSize;
        MaxNestingDepth = maxNestingDepth;
        MaxItemCount = maxItemCount;
        MaxValueCount = maxValueCount;
        MaxTextLength = maxTextLength;
    }

    /// <summary>Gets the number of spaces added for each nested Item.</summary>
    public int IndentSize { get; }

    /// <summary>Gets the maximum root-inclusive Item depth.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets the maximum number of Items in one message.</summary>
    public int MaxItemCount { get; }

    /// <summary>Gets the maximum primitive values in one Item.</summary>
    public int MaxValueCount { get; }

    /// <summary>Gets the maximum accepted or produced text length.</summary>
    public int MaxTextLength { get; }

    /// <summary>Encodes one dynamic message to deterministic LF-terminated text.</summary>
    public string Encode(SecsMessage message)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        return new SmlMessageWriter(this).Write(message);
    }

    /// <summary>Strictly decodes one complete SML message.</summary>
    public SecsMessage Decode(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), text.Length, $"The SML text length cannot exceed {MaxTextLength} characters.");

        return new SmlMessageParser(text, this).Parse();
    }
}
