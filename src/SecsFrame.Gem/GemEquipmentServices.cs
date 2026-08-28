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
    private readonly Dictionary<SecsItem, GemReportDefinition> _reportDefinitions = new();
    private readonly Dictionary<SecsItem, GemEventReportLink> _eventReportLinks = new();
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

    /// <summary>Collects and sends one linked Collection Event to the Host.</summary>
    public async Task SendCollectionEventAsync(
        SecsItem dataId,
        SecsItem eventId,
        CancellationToken cancellationToken = default)
    {
        if (dataId is null)
            throw new ArgumentNullException(nameof(dataId));
        if (eventId is null)
            throw new ArgumentNullException(nameof(eventId));

        var executions = GetReportExecutions(eventId);
        var reports = new GemCollectedReport[executions.Count];
        for (var reportIndex = 0; reportIndex < executions.Count; reportIndex++)
        {
            var execution = executions[reportIndex];
            var values = new SecsItem[execution.Providers.Count];
            for (var valueIndex = 0; valueIndex < execution.Providers.Count; valueIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                values[valueIndex] = await execution.Providers[valueIndex](
                    cancellationToken).ConfigureAwait(false) ??
                    throw new GemProtocolException(
                        $"The Collection Event provider for report index " +
                        $"{reportIndex}, value index {valueIndex} returned null.");
            }

            reports[reportIndex] = new GemCollectedReport(
                execution.ReportId,
                values);
        }

        var response = await _services.SendRequestAsync(
            Profile.CollectionEvent,
            GemOperation.CollectionEvent,
            GemMessageCodec.EncodeCollectionEvent(
                new GemCollectionEvent(dataId, eventId, reports)),
            cancellationToken).ConfigureAwait(false);
        _services.RequireAccepted(
            GemOperation.CollectionEvent,
            GemMessageCodec.DecodeAcknowledgement(
                response.Message.RootItem,
                "Collection Event reply"));
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
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_gate)
        {
            _statusVariables.Clear();
            _equipmentConstants.Clear();
            _reportDefinitions.Clear();
            _eventReportLinks.Clear();
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
        _services.AddRoute(Profile.DefineReports, HandleDefineReportsAsync);
        _services.AddRoute(Profile.LinkEventReports, HandleLinkEventReportsAsync);
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

    private async ValueTask<SecsMessage?> HandleDefineReportsAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, "report-definition request");
        var request = GemMessageCodec.DecodeReportDefinitions(
            context.Message.RootItem);
        var definitions = TryStageReportDefinitions(request.Reports);
        if (definitions is not null)
            ApplyReportDefinitions(definitions);
        await _services.ReplyAsync(
            context,
            Profile.DefineReports,
            GemMessageCodec.EncodeAcknowledgement(
                definitions is null
                    ? Profile.FailedAcknowledgement
                    : Profile.AcceptedAcknowledgement),
            cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async ValueTask<SecsMessage?> HandleLinkEventReportsAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, "event-report-link request");
        var request = GemMessageCodec.DecodeEventReportLinks(
            context.Message.RootItem);
        var links = TryStageEventReportLinks(request.Links);
        if (links is not null)
            ApplyEventReportLinks(links);
        await _services.ReplyAsync(
            context,
            Profile.LinkEventReports,
            GemMessageCodec.EncodeAcknowledgement(
                links is null
                    ? Profile.FailedAcknowledgement
                    : Profile.AcceptedAcknowledgement),
            cancellationToken).ConfigureAwait(false);
        return null;
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
            ThrowIfDisposed();

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

    private Dictionary<SecsItem, GemReportDefinition>? TryStageReportDefinitions(
        IReadOnlyList<GemReportDefinition> reports)
    {
        var definitions = new Dictionary<SecsItem, GemReportDefinition>();
        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var report in reports)
            {
                foreach (var valueId in report.ValueIds)
                {
                    if (!_statusVariables.ContainsKey(valueId))
                        return null;
                }

                definitions.Add(report.ReportId, report);
            }
        }

        return definitions;
    }

    private Dictionary<SecsItem, GemEventReportLink>? TryStageEventReportLinks(
        IReadOnlyList<GemEventReportLink> links)
    {
        var staged = new Dictionary<SecsItem, GemEventReportLink>();
        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var link in links)
            {
                foreach (var reportId in link.ReportIds)
                {
                    if (!_reportDefinitions.ContainsKey(reportId))
                        return null;
                }

                staged.Add(link.EventId, link);
            }
        }

        return staged;
    }

    private void ApplyReportDefinitions(
        IReadOnlyDictionary<SecsItem, GemReportDefinition> definitions)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _reportDefinitions.Clear();
            foreach (var pair in definitions)
                _reportDefinitions.Add(pair.Key, pair.Value);

            var staleEvents = new List<SecsItem>();
            foreach (var pair in _eventReportLinks)
            {
                foreach (var reportId in pair.Value.ReportIds)
                {
                    if (!_reportDefinitions.ContainsKey(reportId))
                    {
                        staleEvents.Add(pair.Key);
                        break;
                    }
                }
            }

            foreach (var eventId in staleEvents)
                _eventReportLinks.Remove(eventId);
        }
    }

    private void ApplyEventReportLinks(
        IReadOnlyDictionary<SecsItem, GemEventReportLink> links)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _eventReportLinks.Clear();
            foreach (var pair in links)
                _eventReportLinks.Add(pair.Key, pair.Value);
        }
    }

    private IReadOnlyList<ReportExecution> GetReportExecutions(SecsItem eventId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_eventReportLinks.TryGetValue(eventId, out var link))
            {
                throw new InvalidOperationException(
                    $"No report link is configured for Collection Event {eventId}.");
            }

            var executions = new ReportExecution[link.ReportIds.Count];
            for (var reportIndex = 0; reportIndex < link.ReportIds.Count; reportIndex++)
            {
                var reportId = link.ReportIds[reportIndex];
                if (!_reportDefinitions.TryGetValue(reportId, out var definition))
                {
                    throw new InvalidOperationException(
                        $"Collection Event {eventId} references an undefined report.");
                }

                var providers = new GemValueProvider[definition.ValueIds.Count];
                for (var valueIndex = 0; valueIndex < definition.ValueIds.Count; valueIndex++)
                {
                    if (!_statusVariables.TryGetValue(
                        definition.ValueIds[valueIndex],
                        out var registration))
                    {
                        throw new InvalidOperationException(
                            $"Report {reportId} references an unregistered status variable.");
                    }

                    providers[valueIndex] = registration.Provider;
                }

                executions[reportIndex] = new ReportExecution(
                    reportId,
                    Array.AsReadOnly(providers));
            }

            return Array.AsReadOnly(executions);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(GemEquipmentServices));
    }

    private sealed class ReportExecution
    {
        internal ReportExecution(
            SecsItem reportId,
            IReadOnlyList<GemValueProvider> providers)
        {
            ReportId = reportId;
            Providers = providers;
        }

        internal SecsItem ReportId { get; }

        internal IReadOnlyList<GemValueProvider> Providers { get; }
    }
}
