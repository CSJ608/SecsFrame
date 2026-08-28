namespace SecsFrame;

/// <summary>A Host-role SECS endpoint independent of HSMS connection mode.</summary>
public sealed class SecsHost : SecsEndpoint
{
    /// <summary>Creates a Host endpoint that owns a new HSMS connection.</summary>
    public SecsHost(HsmsConnectionOptions options)
        : base(SecsEndpointRole.Host, options)
    {
    }
}
