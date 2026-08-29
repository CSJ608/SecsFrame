using System.Collections.ObjectModel;

namespace SecsFrame.Trace;

/// <summary>
/// Replaces one Item selected by Stream, Function, and zero-based List path.
/// </summary>
public sealed class SecsTraceRedactionRule
{
    private readonly ReadOnlyCollection<int> _itemPath;

    /// <summary>Creates one exact structural redaction rule.</summary>
    /// <param name="stream">The matching message stream.</param>
    /// <param name="function">The matching message function.</param>
    /// <param name="itemPath">List indexes from the root Item; empty selects the root.</param>
    /// <param name="replacement">The replacement Item written into the exported copy.</param>
    public SecsTraceRedactionRule(
        byte stream,
        byte function,
        IEnumerable<int> itemPath,
        SecsItem replacement)
    {
        if (stream > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(stream), stream, "The stream number must be between 0 and 127.");
        if (itemPath is null)
            throw new ArgumentNullException(nameof(itemPath));

        var path = itemPath.ToArray();
        for (var index = 0; index < path.Length; index++)
        {
            if (path[index] < 0)
                throw new ArgumentOutOfRangeException(nameof(itemPath), path[index], "Item path indexes cannot be negative.");
        }

        Stream = stream;
        Function = function;
        _itemPath = Array.AsReadOnly(path);
        Replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    /// <summary>Gets the matching message stream.</summary>
    public byte Stream { get; }

    /// <summary>Gets the matching message function.</summary>
    public byte Function { get; }

    /// <summary>Gets the zero-based List path from the root Item.</summary>
    public IReadOnlyList<int> ItemPath => _itemPath;

    /// <summary>Gets the immutable replacement Item.</summary>
    public SecsItem Replacement { get; }
}
