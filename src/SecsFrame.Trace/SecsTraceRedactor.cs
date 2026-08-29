namespace SecsFrame.Trace;

/// <summary>Applies exact structural replacement rules before trace export.</summary>
public sealed class SecsTraceRedactor
{
    private readonly SecsTraceRedactionRule[] _rules;

    /// <summary>Creates a redactor and rejects ambiguous overlapping rules.</summary>
    public SecsTraceRedactor(IEnumerable<SecsTraceRedactionRule> rules)
    {
        if (rules is null)
            throw new ArgumentNullException(nameof(rules));

        _rules = rules.ToArray();
        for (var index = 0; index < _rules.Length; index++)
        {
            if (_rules[index] is null)
                throw new ArgumentException("The redaction rule sequence contains a null rule.", nameof(rules));
            for (var otherIndex = index + 1; otherIndex < _rules.Length; otherIndex++)
            {
                if (_rules[otherIndex] is null)
                    throw new ArgumentException("The redaction rule sequence contains a null rule.", nameof(rules));
                if (RulesOverlap(_rules[index], _rules[otherIndex]))
                    throw new ArgumentException("Redaction rules for the same message cannot target identical or ancestor paths.", nameof(rules));
            }
        }
    }

    /// <summary>
    /// Returns the original record when no rule matches, otherwise a copy with
    /// replacements applied.
    /// </summary>
    public SecsTraceRecord Redact(SecsTraceRecord record)
    {
        if (record is null)
            throw new ArgumentNullException(nameof(record));

        var rootItem = record.Message.RootItem;
        var changed = false;
        foreach (var rule in _rules)
        {
            if (rule.Stream != record.Message.Stream || rule.Function != record.Message.Function)
                continue;
            if (rootItem is null)
                throw new InvalidOperationException("A matching redaction rule cannot resolve because the message has no root Item.");

            rootItem = ReplaceAtPath(rootItem, rule.ItemPath, pathIndex: 0, rule.Replacement);
            changed = true;
        }

        if (!changed)
            return record;

        var message = new SecsMessage(
            record.Message.Stream,
            record.Message.Function,
            record.Message.ReplyExpected,
            rootItem);
        return new SecsTraceRecord(
            record.Timestamp,
            record.Direction,
            message,
            record.SessionId,
            record.SystemBytes);
    }

    private static SecsItem ReplaceAtPath(
        SecsItem item,
        IReadOnlyList<int> path,
        int pathIndex,
        SecsItem replacement)
    {
        if (pathIndex == path.Count)
            return replacement;
        if (item.Format != SecsItemFormat.List)
            throw new InvalidOperationException($"The redaction path enters a non-List Item at depth {pathIndex}.");

        var childIndex = path[pathIndex];
        if (childIndex >= item.Items.Count)
            throw new InvalidOperationException($"The redaction path index {childIndex} is outside the List at depth {pathIndex}.");

        var items = item.Items.ToArray();
        items[childIndex] = ReplaceAtPath(items[childIndex], path, pathIndex + 1, replacement);
        return SecsItem.List(items);
    }

    private static bool RulesOverlap(SecsTraceRedactionRule left, SecsTraceRedactionRule right)
    {
        if (left.Stream != right.Stream || left.Function != right.Function)
            return false;

        var sharedLength = Math.Min(left.ItemPath.Count, right.ItemPath.Count);
        for (var index = 0; index < sharedLength; index++)
        {
            if (left.ItemPath[index] != right.ItemPath[index])
                return false;
        }
        return true;
    }
}
