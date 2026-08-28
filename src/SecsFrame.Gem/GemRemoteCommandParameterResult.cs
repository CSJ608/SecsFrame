namespace SecsFrame.Gem;

/// <summary>Contains one raw remote-command parameter result.</summary>
public sealed class GemRemoteCommandParameterResult
{
    /// <summary>Creates a parameter result without interpreting its code.</summary>
    public GemRemoteCommandParameterResult(
        SecsItem name,
        byte acknowledgement)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Acknowledgement = acknowledgement;
    }

    /// <summary>Gets the exact dynamic parameter name.</summary>
    public SecsItem Name { get; }

    /// <summary>Gets the exact acknowledgement byte.</summary>
    public byte Acknowledgement { get; }
}
