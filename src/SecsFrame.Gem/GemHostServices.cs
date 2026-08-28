namespace SecsFrame.Gem;

/// <summary>Provides foundational, profile-driven GEM operations for a Host endpoint.</summary>
public sealed class GemHostServices : IDisposable
{
    private readonly GemEndpointServices _services;

    /// <summary>Creates Host GEM services without taking ownership of the endpoint.</summary>
    public GemHostServices(
        SecsHost host,
        GemIdentity identity,
        GemMessageProfile? profile = null)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        _services = new GemEndpointServices(
            host,
            identity,
            profile ?? GemMessageProfile.CreateEngineeringBaseline());
    }

    /// <summary>Gets the local identity advertised to the Equipment.</summary>
    public GemIdentity Identity => _services.Identity;

    /// <summary>Gets the most recently accepted peer identity, when known.</summary>
    public GemIdentity? PeerIdentity => _services.PeerIdentity;

    /// <summary>Gets the observed communications state.</summary>
    public GemCommunicationState CommunicationState => _services.CommunicationState;

    /// <summary>Gets the observed remote online state.</summary>
    public GemOnlineState OnlineState => _services.OnlineState;

    /// <summary>Gets the configured interoperability profile.</summary>
    public GemMessageProfile Profile => _services.Profile;

    /// <summary>Establishes communications and returns the Equipment identity.</summary>
    public Task<GemIdentity> EstablishCommunicationAsync(
        CancellationToken cancellationToken = default)
        => _services.EstablishCommunicationAsync(cancellationToken);

    /// <summary>Queries the Equipment identity.</summary>
    public Task<GemIdentity> AreYouOnlineAsync(
        CancellationToken cancellationToken = default)
        => _services.AreYouOnlineAsync(cancellationToken);

    /// <summary>Requests the Equipment to transition online.</summary>
    public Task RequestOnlineAsync(CancellationToken cancellationToken = default)
        => _services.RequestOnlineStateAsync(
            Profile.RequestOnline,
            GemOperation.RequestOnline,
            GemOnlineState.Online,
            cancellationToken);

    /// <summary>Requests the Equipment to transition offline.</summary>
    public Task RequestOfflineAsync(CancellationToken cancellationToken = default)
        => _services.RequestOnlineStateAsync(
            Profile.RequestOffline,
            GemOperation.RequestOffline,
            GemOnlineState.Offline,
            cancellationToken);

    /// <summary>Reads dynamic status-variable values in request order.</summary>
    public Task<IReadOnlyList<SecsItem>> ReadStatusVariablesAsync(
        IEnumerable<SecsItem> identifiers,
        CancellationToken cancellationToken = default)
        => ReadValuesAsync(
            Profile.ReadStatusVariables,
            GemOperation.ReadStatusVariables,
            identifiers,
            cancellationToken);

    /// <summary>Reads dynamic equipment-constant values in request order.</summary>
    public Task<IReadOnlyList<SecsItem>> ReadEquipmentConstantsAsync(
        IEnumerable<SecsItem> identifiers,
        CancellationToken cancellationToken = default)
        => ReadValuesAsync(
            Profile.ReadEquipmentConstants,
            GemOperation.ReadEquipmentConstants,
            identifiers,
            cancellationToken);

    /// <summary>Reads the Equipment clock through the configured codec.</summary>
    public async Task<DateTimeOffset> GetClockAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _services.SendRequestAsync(
            Profile.GetClock,
            GemOperation.GetClock,
            rootItem: null,
            cancellationToken).ConfigureAwait(false);
        return GemMessageCodec.DecodeClock(
            response.Message.RootItem,
            Profile.ClockCodec,
            "clock-read reply");
    }

    /// <summary>Requests the application-owned Equipment clock to be set.</summary>
    public async Task SetClockAsync(
        DateTimeOffset value,
        CancellationToken cancellationToken = default)
    {
        var response = await _services.SendRequestAsync(
            Profile.SetClock,
            GemOperation.SetClock,
            GemMessageCodec.EncodeClock(value, Profile.ClockCodec),
            cancellationToken).ConfigureAwait(false);
        _services.RequireAccepted(
            GemOperation.SetClock,
            GemMessageCodec.DecodeAcknowledgement(
                response.Message.RootItem,
                "clock-set reply"));
    }

    /// <summary>
    /// Observes session state and dispatches a registered GEM or application route.
    /// </summary>
    public ValueTask<bool> TryDispatchAsync(
        HsmsConnectionEvent connectionEvent,
        CancellationToken cancellationToken = default)
        => _services.TryDispatchAsync(connectionEvent, cancellationToken);

    /// <summary>Removes the GEM Primary routes without disposing the endpoint.</summary>
    public void Dispose()
        => _services.Dispose();

    private async Task<IReadOnlyList<SecsItem>> ReadValuesAsync(
        GemMessagePair pair,
        GemOperation operation,
        IEnumerable<SecsItem> identifiers,
        CancellationToken cancellationToken)
    {
        var response = await _services.SendRequestAsync(
            pair,
            operation,
            GemMessageCodec.EncodeIdentifiers(identifiers),
            cancellationToken).ConfigureAwait(false);
        return GemMessageCodec.DecodeList(
            response.Message.RootItem,
            $"{operation} reply");
    }
}
