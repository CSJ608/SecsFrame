namespace SecsFrame;

/// <summary>An Equipment-role SECS endpoint independent of HSMS connection mode.</summary>
public sealed class SecsEquipment : SecsEndpoint
{
    /// <summary>Creates an Equipment endpoint that owns a new HSMS connection.</summary>
    public SecsEquipment(HsmsConnectionOptions options)
        : base(SecsEndpointRole.Equipment, options)
    {
    }
}
