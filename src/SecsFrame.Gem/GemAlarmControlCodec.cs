namespace SecsFrame.Gem;

/// <summary>Maps alarm-send control states to exact protocol bytes.</summary>
/// <remarks>
/// The mapping is an interoperability configuration and does not interpret
/// alarm notification code bits.
/// </remarks>
public sealed class GemAlarmControlCodec
{
    /// <summary>Creates an unambiguous alarm-send control mapping.</summary>
    public GemAlarmControlCodec(byte enabledCode, byte disabledCode)
    {
        if (enabledCode == disabledCode)
        {
            throw new ArgumentException(
                "Enabled and disabled alarm-send codes must differ.",
                nameof(disabledCode));
        }

        EnabledCode = enabledCode;
        DisabledCode = disabledCode;
    }

    /// <summary>Gets the exact code used to enable alarm sending.</summary>
    public byte EnabledCode { get; }

    /// <summary>Gets the exact code used to disable alarm sending.</summary>
    public byte DisabledCode { get; }

    internal byte Encode(bool enabled)
        => enabled ? EnabledCode : DisabledCode;

    internal bool Decode(byte code)
    {
        if (code == EnabledCode)
            return true;
        if (code == DisabledCode)
            return false;

        throw new GemProtocolException(
            $"The alarm-send control code 0x{code:X2} is not configured.");
    }
}
