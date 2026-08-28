namespace SecsFrame.Gem;

/// <summary>
/// Provides foundational GEM responders and dynamic data for an Equipment endpoint.
/// </summary>
public sealed class GemEquipmentServices : IDisposable
{
    private readonly GemEndpointServices _services;
    private readonly IGemClock _clock;
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly Dictionary<SecsItem, GemValueRegistration> _statusVariables = new();
    private readonly Dictionary<SecsItem, GemValueRegistration> _equipmentConstants = new();
    private int _disposed;

    /// <summary>Creates Equipment GEM services without taking ownership of the endpoint.</summary>
    public GemEquipmentServices(
        SecsEquipment equipment,
        GemIdentity identity,
        IGemClock clock,
        GemMessageProfile? profile = null)
    {
        if (equipment is null)
            throw new ArgumentNullException(nameof(equipment));

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _services = new GemEndpointServices(
            equipment,
            identity,
            profile ?? GemMessageProfile.CreateEngineeringBaseline());
        try
        {
            RegisterEquipmentRoutes();
        }
        catch
        {
            _services.Dispose();
            throw;
        }
    }

    /// <summary>Gets the local identity advertised to the Host.</summary>
    public GemIdentity Identity => _services.Identity;

    /// <summary>Gets the most recently accepted peer identity, when known.</summary>
    public GemIdentity? PeerIdentity => _services.PeerIdentity;

    /// <summary>Gets the observed communications state.</summary>
    public GemCommunicationState CommunicationState => _services.CommunicationState;

    /// <summary>Gets the local online state.</summary>
    public GemOnlineState OnlineState => _services.OnlineState;

    /// <summary>Gets the configured interoperability profile.</summary>
    public GemMessageProfile Profile => _services.Profile;

    /// <summary>Establishes communications and returns the Host identity.</summary>
    public Task<GemIdentity> EstablishCommunicationAsync(
        CancellationToken cancellationToken = default)
        => _services.EstablishCommunicationAsync(cancellationToken);

    /// <summary>Queries the Host identity.</summary>
    public Task<GemIdentity> AreYouOnlineAsync(
        CancellationToken cancellationToken = default)
        => _services.AreYouOnlineAsync(cancellationToken);

    /// <summary>Registers an exact runtime status-variable provider.</summary>
    public GemValueRegistration RegisterStatusVariable(
        SecsItem identifier,
        GemValueProvider provider)
        => RegisterValue(
            _statusVariables,
            identifier,
            provider,
            "status variable");

    /// <summary>Registers an exact runtime equipment-constant provider.</summary>
    public GemValueRegistration RegisterEquipmentConstant(
        SecsItem identifier,
        GemValueProvider provider)
        => RegisterValue(
            _equipmentConstants,
            identifier,
            provider,
            "equipment constant");

    /// <summary>
    /// Observes session state and dispatches a registered GEM or application route.
    /// </summary>
    public ValueTask<bool> TryDispatchAsync(
        HsmsConnectionEvent connectionEvent,
        CancellationToken cancellationToken = default)
        => _services.TryDispatchAsync(connectionEvent, cancellationToken);

    /// <summary>Removes the GEM Primary routes without disposing the endpoint.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_gate)
        {
            _statusVariables.Clear();
            _equipmentConstants.Clear();
        }
        _services.Dispose();
    }

    private void RegisterEquipmentRoutes()
    {
        _services.AddRoute(Profile.RequestOnline, HandleOnlineAsync);
        _services.AddRoute(Profile.RequestOffline, HandleOfflineAsync);
        _services.AddRoute(Profile.ReadStatusVariables, HandleStatusVariablesAsync);
        _services.AddRoute(Profile.ReadEquipmentConstants, HandleEquipmentConstantsAsync);
        _services.AddRoute(Profile.GetClock, HandleGetClockAsync);
        _services.AddRoute(Profile.SetClock, HandleSetClockAsync);
    }

    private ValueTask<SecsMessage?> HandleOnlineAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
        => HandleOnlineStateAsync(
            context,
            Profile.RequestOnline,
            GemOnlineState.Online,
            "online request",
            cancellationToken);

    private ValueTask<SecsMessage?> HandleOfflineAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
        => HandleOnlineStateAsync(
            context,
            Profile.RequestOffline,
            GemOnlineState.Offline,
            "offline request",
            cancellationToken);

    private async ValueTask<SecsMessage?> HandleOnlineStateAsync(
        HsmsPrimaryContext context,
        GemMessagePair pair,
        GemOnlineState state,
        string operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, operation);
        GemMessageCodec.RequireEmptyBody(context.Message.RootItem, operation);
        await _services.ReplyAsync(
            context,
            pair,
            GemMessageCodec.EncodeAcknowledgement(
                Profile.AcceptedAcknowledgement),
            cancellationToken).ConfigureAwait(false);
        _services.SetOnlineState(state);
        return null;
    }

    private async ValueTask<SecsMessage?> HandleStatusVariablesAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        GemMessageCodec.RequireReplyExpected(
            context,
            "status-variable request");
        return GemEndpointServices.CreateSecondary(
            Profile.ReadStatusVariables,
            await ReadValuesAsync(
                _statusVariables,
                context.Message.RootItem,
                "status-variable request",
                cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask<SecsMessage?> HandleEquipmentConstantsAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        GemMessageCodec.RequireReplyExpected(
            context,
            "equipment-constant request");
        return GemEndpointServices.CreateSecondary(
            Profile.ReadEquipmentConstants,
            await ReadValuesAsync(
                _equipmentConstants,
                context.Message.RootItem,
                "equipment-constant request",
                cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask<SecsMessage?> HandleGetClockAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        GemMessageCodec.RequireReplyExpected(context, "clock-read request");
        GemMessageCodec.RequireEmptyBody(
            context.Message.RootItem,
            "clock-read request");
        var value = await _clock.GetCurrentTimeAsync(cancellationToken)
            .ConfigureAwait(false);
        return GemEndpointServices.CreateSecondary(
            Profile.GetClock,
            GemMessageCodec.EncodeClock(value, Profile.ClockCodec));
    }

    private async ValueTask<SecsMessage?> HandleSetClockAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        GemMessageCodec.RequireReplyExpected(context, "clock-set request");
        var value = GemMessageCodec.DecodeClock(
            context.Message.RootItem,
            Profile.ClockCodec,
            "clock-set request");
        var accepted = await _clock.SetCurrentTimeAsync(value, cancellationToken)
            .ConfigureAwait(false);
        return GemEndpointServices.CreateSecondary(
            Profile.SetClock,
            GemMessageCodec.EncodeAcknowledgement(
                accepted
                    ? Profile.AcceptedAcknowledgement
                    : Profile.FailedAcknowledgement));
    }

    private GemValueRegistration RegisterValue(
        Dictionary<SecsItem, GemValueRegistration> registrations,
        SecsItem identifier,
        GemValueProvider provider,
        string kind)
    {
        if (identifier is null)
            throw new ArgumentNullException(nameof(identifier));
        if (provider is null)
            throw new ArgumentNullException(nameof(provider));

        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(GemEquipmentServices));

            if (registrations.ContainsKey(identifier))
            {
                throw new InvalidOperationException(
                    $"A {kind} provider is already registered for {identifier}.");
            }

            var registration = new GemValueRegistration(
                identifier,
                provider,
                current => UnregisterValue(registrations, current));
            registrations.Add(identifier, registration);
            return registration;
        }
    }

    private void UnregisterValue(
        Dictionary<SecsItem, GemValueRegistration> registrations,
        GemValueRegistration registration)
    {
        lock (_gate)
        {
            if (registrations.TryGetValue(registration.Id, out var current) &&
                ReferenceEquals(current, registration))
            {
                registrations.Remove(registration.Id);
            }
        }
    }

    private async ValueTask<SecsItem> ReadValuesAsync(
        Dictionary<SecsItem, GemValueRegistration> registrations,
        SecsItem? root,
        string operation,
        CancellationToken cancellationToken)
    {
        var identifiers = GemMessageCodec.DecodeList(root, operation);
        var providers = GetProviders(registrations, identifiers, operation);
        var values = new SecsItem[providers.Count];
        for (var index = 0; index < providers.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values[index] = await providers[index](cancellationToken)
                .ConfigureAwait(false) ?? throw new GemProtocolException(
                    $"The {operation} provider at index {index} returned null.");
        }

        return SecsItem.List(values);
    }

    private IReadOnlyList<GemValueProvider> GetProviders(
        Dictionary<SecsItem, GemValueRegistration> registrations,
        IReadOnlyList<SecsItem> identifiers,
        string operation)
    {
        var providers = new GemValueProvider[identifiers.Count];
        lock (_gate)
        {
            for (var index = 0; index < identifiers.Count; index++)
            {
                if (!registrations.TryGetValue(identifiers[index], out var registration))
                {
                    throw new GemProtocolException(
                        $"The {operation} contains an unregistered identifier " +
                        $"at index {index}.");
                }

                providers[index] = registration.Provider;
            }
        }

        return providers;
    }
}
