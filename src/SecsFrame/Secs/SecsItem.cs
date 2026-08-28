using System.Collections.ObjectModel;

namespace SecsFrame;

/// <summary>
/// An immutable, dynamically shaped SECS-II Item. Messages can compose arbitrary
/// Item trees without declaring an SxFy schema in advance.
/// </summary>
public sealed class SecsItem : IEquatable<SecsItem>
{
    /// <summary>The largest value byte length or List element count representable by an Item header.</summary>
    public const int MaxEncodedLength = 0xFF_FF_FF;

    private readonly ReadOnlyCollection<SecsItem>? _items;
    private readonly object _value;

    private SecsItem(SecsItemFormat format, object value)
    {
        Format = format;
        _value = value;
        if (value is SecsItem[] items)
            _items = Array.AsReadOnly(items);
    }

    /// <summary>Gets the Item format.</summary>
    public SecsItemFormat Format { get; }

    /// <summary>Gets the List element count, character count, or primitive value count.</summary>
    public int Count => Format switch
    {
        SecsItemFormat.List => ((SecsItem[])_value).Length,
        SecsItemFormat.Ascii => ((string)_value).Length,
        _ => ((Array)_value).Length,
    };

    /// <summary>Gets the nested Items.</summary>
    /// <exception cref="InvalidOperationException">This Item is not a List.</exception>
    public IReadOnlyList<SecsItem> Items
        => _items ?? throw new InvalidOperationException($"An Item with format {Format} does not contain nested Items.");

    /// <summary>Gets a nested Item by index.</summary>
    /// <param name="index">The zero-based child index.</param>
    /// <exception cref="InvalidOperationException">This Item is not a List.</exception>
    public SecsItem this[int index] => Items[index];

    /// <summary>Creates a List Item.</summary>
    /// <param name="items">The nested Items.</param>
    public static SecsItem List(params SecsItem[] items)
    {
        if (items is null)
            throw new ArgumentNullException(nameof(items));
        if (items.Length > MaxEncodedLength)
            throw LengthOutOfRange(nameof(items), items.Length);

        var copy = (SecsItem[])items.Clone();
        for (var index = 0; index < copy.Length; index++)
        {
            if (copy[index] is null)
                throw new ArgumentException($"List element {index} is null.", nameof(items));
        }

        return new SecsItem(SecsItemFormat.List, copy);
    }

    /// <summary>Creates a List Item from a sequence.</summary>
    /// <param name="items">The nested Items.</param>
    public static SecsItem List(IEnumerable<SecsItem> items)
    {
        if (items is null)
            throw new ArgumentNullException(nameof(items));

        return List(items.ToArray());
    }

    /// <summary>Creates a Binary Item.</summary>
    public static SecsItem Binary(params byte[] values) => CreateValues(SecsItemFormat.Binary, values, sizeof(byte));

    /// <summary>Creates a Boolean Item.</summary>
    public static SecsItem Boolean(params bool[] values) => CreateValues(SecsItemFormat.Boolean, values, sizeof(byte));

    /// <summary>Creates an ASCII Item.</summary>
    /// <param name="value">A string containing only seven-bit ASCII characters.</param>
    public static SecsItem Ascii(string value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        if (value.Length > MaxEncodedLength)
            throw LengthOutOfRange(nameof(value), value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] > 0x7F)
            {
                throw new ArgumentException(
                    $"The character at index {index} is outside the seven-bit ASCII range.",
                    nameof(value));
            }
        }

        return new SecsItem(SecsItemFormat.Ascii, value);
    }

    /// <summary>Creates a JIS-8 Item from encoded bytes.</summary>
    public static SecsItem Jis8(params byte[] values) => CreateValues(SecsItemFormat.Jis8, values, sizeof(byte));

    /// <summary>Creates an I8 Item.</summary>
    public static SecsItem I8(params long[] values) => CreateValues(SecsItemFormat.I8, values, sizeof(long));

    /// <summary>Creates an I1 Item.</summary>
    public static SecsItem I1(params sbyte[] values) => CreateValues(SecsItemFormat.I1, values, sizeof(sbyte));

    /// <summary>Creates an I2 Item.</summary>
    public static SecsItem I2(params short[] values) => CreateValues(SecsItemFormat.I2, values, sizeof(short));

    /// <summary>Creates an I4 Item.</summary>
    public static SecsItem I4(params int[] values) => CreateValues(SecsItemFormat.I4, values, sizeof(int));

    /// <summary>Creates an F8 Item.</summary>
    public static SecsItem F8(params double[] values) => CreateValues(SecsItemFormat.F8, values, sizeof(double));

    /// <summary>Creates an F4 Item.</summary>
    public static SecsItem F4(params float[] values) => CreateValues(SecsItemFormat.F4, values, sizeof(float));

    /// <summary>Creates a U8 Item.</summary>
    public static SecsItem U8(params ulong[] values) => CreateValues(SecsItemFormat.U8, values, sizeof(ulong));

    /// <summary>Creates a U1 Item.</summary>
    public static SecsItem U1(params byte[] values) => CreateValues(SecsItemFormat.U1, values, sizeof(byte));

    /// <summary>Creates a U2 Item.</summary>
    public static SecsItem U2(params ushort[] values) => CreateValues(SecsItemFormat.U2, values, sizeof(ushort));

    /// <summary>Creates a U4 Item.</summary>
    public static SecsItem U4(params uint[] values) => CreateValues(SecsItemFormat.U4, values, sizeof(uint));

    /// <summary>Gets the ASCII string.</summary>
    /// <exception cref="InvalidOperationException">This Item is not ASCII.</exception>
    public string GetString()
        => Format == SecsItemFormat.Ascii
            ? (string)_value
            : throw new InvalidOperationException($"An Item with format {Format} does not contain an ASCII string.");

    /// <summary>Gets primitive values without allowing callers to mutate the Item.</summary>
    /// <typeparam name="T">The exact primitive type stored by this Item.</typeparam>
    /// <exception cref="InvalidOperationException">The Item does not store values of <typeparamref name="T"/>.</exception>
    public ReadOnlySpan<T> GetValues<T>()
        where T : struct
        => _value is T[] values
            ? values
            : throw new InvalidOperationException(
                $"An Item with format {Format} does not contain {typeof(T).Name} values.");

    /// <inheritdoc />
    public bool Equals(SecsItem? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || Format != other.Format)
            return false;

        return Format switch
        {
            SecsItemFormat.List => ListsEqual((SecsItem[])_value, (SecsItem[])other._value),
            SecsItemFormat.Ascii => string.Equals((string)_value, (string)other._value, StringComparison.Ordinal),
            SecsItemFormat.Binary or SecsItemFormat.Jis8 or SecsItemFormat.U1
                => GetValues<byte>().SequenceEqual(other.GetValues<byte>()),
            SecsItemFormat.Boolean => GetValues<bool>().SequenceEqual(other.GetValues<bool>()),
            SecsItemFormat.I8 => GetValues<long>().SequenceEqual(other.GetValues<long>()),
            SecsItemFormat.I1 => GetValues<sbyte>().SequenceEqual(other.GetValues<sbyte>()),
            SecsItemFormat.I2 => GetValues<short>().SequenceEqual(other.GetValues<short>()),
            SecsItemFormat.I4 => GetValues<int>().SequenceEqual(other.GetValues<int>()),
            SecsItemFormat.F8 => GetValues<double>().SequenceEqual(other.GetValues<double>()),
            SecsItemFormat.F4 => GetValues<float>().SequenceEqual(other.GetValues<float>()),
            SecsItemFormat.U8 => GetValues<ulong>().SequenceEqual(other.GetValues<ulong>()),
            SecsItemFormat.U2 => GetValues<ushort>().SequenceEqual(other.GetValues<ushort>()),
            SecsItemFormat.U4 => GetValues<uint>().SequenceEqual(other.GetValues<uint>()),
            _ => false,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SecsItem);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Format;
            if (_value is string text)
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(text);

            foreach (var value in (Array)_value)
                hash = (hash * 397) ^ (value?.GetHashCode() ?? 0);

            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Format} [{Count}]";

    internal SecsItem[] GetListValues() => (SecsItem[])_value;

    internal static SecsItem FromDecodedList(SecsItem[] items)
        => new(SecsItemFormat.List, items);

    internal static SecsItem FromDecodedAscii(string value)
        => new(SecsItemFormat.Ascii, value);

    internal static SecsItem FromDecodedValues<T>(SecsItemFormat format, T[] values)
        where T : struct
        => new(format, values);

    private static SecsItem CreateValues<T>(SecsItemFormat format, T[] values, int elementSize)
        where T : struct
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));

        var byteLength = (long)values.Length * elementSize;
        if (byteLength > MaxEncodedLength)
            throw LengthOutOfRange(nameof(values), byteLength);

        return new SecsItem(format, (T[])values.Clone());
    }

    private static bool ListsEqual(SecsItem[] left, SecsItem[] right)
    {
        if (left.Length != right.Length)
            return false;

        for (var index = 0; index < left.Length; index++)
        {
            if (!left[index].Equals(right[index]))
                return false;
        }

        return true;
    }

    private static ArgumentOutOfRangeException LengthOutOfRange(string parameterName, long length)
        => new(parameterName, length, $"The encoded Item length cannot exceed {MaxEncodedLength}.");
}
