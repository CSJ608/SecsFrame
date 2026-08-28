namespace SecsFrame.Gem;

internal static class GemCollection
{
    internal static IReadOnlyList<T> Copy<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        if (values is null)
            throw new ArgumentNullException(parameterName);

        var copy = values.ToArray();
        for (var index = 0; index < copy.Length; index++)
        {
            if (copy[index] is null)
            {
                throw new ArgumentException(
                    $"Collection element {index} is null.",
                    parameterName);
            }
        }

        return Array.AsReadOnly(copy);
    }
}
