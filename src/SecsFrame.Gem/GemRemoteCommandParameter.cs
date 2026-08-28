namespace SecsFrame.Gem;

/// <summary>Contains one dynamic remote-command parameter.</summary>
public sealed class GemRemoteCommandParameter
{
    /// <summary>Creates a remote-command parameter.</summary>
    public GemRemoteCommandParameter(SecsItem name, SecsItem value)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the exact dynamic parameter name.</summary>
    public SecsItem Name { get; }

    /// <summary>Gets the exact dynamic parameter value.</summary>
    public SecsItem Value { get; }
}
