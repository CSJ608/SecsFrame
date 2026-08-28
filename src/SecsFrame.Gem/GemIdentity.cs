namespace SecsFrame.Gem;

/// <summary>Provides the model and software revision exchanged during communication.</summary>
public sealed class GemIdentity : IEquatable<GemIdentity>
{
    /// <summary>Creates an endpoint identity.</summary>
    public GemIdentity(string model, string softwareRevision)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        SoftwareRevision = softwareRevision ??
            throw new ArgumentNullException(nameof(softwareRevision));

        _ = SecsItem.Ascii(model);
        _ = SecsItem.Ascii(softwareRevision);
    }

    /// <summary>Gets the seven-bit ASCII model name.</summary>
    public string Model { get; }

    /// <summary>Gets the seven-bit ASCII software revision.</summary>
    public string SoftwareRevision { get; }

    /// <inheritdoc />
    public bool Equals(GemIdentity? other)
        => other is not null &&
            string.Equals(Model, other.Model, StringComparison.Ordinal) &&
            string.Equals(
                SoftwareRevision,
                other.SoftwareRevision,
                StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => Equals(obj as GemIdentity);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(Model) * 397) ^
                StringComparer.Ordinal.GetHashCode(SoftwareRevision);
        }
    }
}
