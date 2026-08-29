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
    private readonly Dictionary<SecsItem, GemAlarmRegistration> _alarms = new();
    private readonly List<GemAlarmRegistration> _alarmCatalog = new();
    private GemOnlineStateTransitionRegistration? _onlineStateTransitionHandler;
    private GemCollectionEventSendPolicyRegistration?
        _collectionEventSendPolicyHandler;
    private GemRemoteCommandAcceptanceRegistration?
        _remoteCommandAcceptanceHandler;
    private GemRemoteCommandRegistration? _remoteCommandHandler;
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

    /// <summary>
    /// Registers the single application policy for peer-requested communication
    /// establishment.
    /// </summary>
    public GemCommunicationEstablishmentRegistration
        RegisterCommunicationEstablishmentHandler(
            GemCommunicationEstablishmentHandler handler)
        => _services.RegisterCommunicationEstablishmentHandler(handler);

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
    /// Registers the single application policy for Host-requested online-state
    /// transitions.
    /// </summary>
    public GemOnlineStateTransitionRegistration RegisterOnlineStateTransitionHandler(
        GemOnlineStateTransitionHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_onlineStateTransitionHandler is not null)
            {
                throw new InvalidOperationException(
                    "An online-state transition handler is already registered.");
            }

            var registration = new GemOnlineStateTransitionRegistration(
                handler,
                UnregisterOnlineStateTransitionHandler);
            _onlineStateTransitionHandler = registration;
            return registration;
        }
    }

    /// <summary>
    /// Registers the single application policy evaluated before remote-command
    /// execution.
    /// </summary>
    public GemRemoteCommandAcceptanceRegistration
        RegisterRemoteCommandAcceptanceHandler(
            GemRemoteCommandAcceptanceHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_remoteCommandAcceptanceHandler is not null)
            {
                throw new InvalidOperationException(
                    "A remote-command acceptance handler is already registered.");
            }

            var registration = new GemRemoteCommandAcceptanceRegistration(
                handler,
                UnregisterRemoteCommandAcceptanceHandler);
            _remoteCommandAcceptanceHandler = registration;
            return registration;
        }
    }

    /// <summary>
    /// Registers the single application policy evaluated before Collection Event
    /// value collection and sending.
    /// </summary>
    public GemCollectionEventSendPolicyRegistration
        RegisterCollectionEventSendPolicyHandler(
            GemCollectionEventSendPolicyHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_collectionEventSendPolicyHandler is not null)
            {
                throw new InvalidOperationException(
                    "A Collection Event send-policy handler is already registered.");
            }

            var registration = new GemCollectionEventSendPolicyRegistration(
                handler,
                UnregisterCollectionEventSendPolicyHandler);
            _collectionEventSendPolicyHandler = registration;
            return registration;
        }
    }

    /// <summary>Registers the single application remote-command handler.</summary>
    public GemRemoteCommandRegistration RegisterRemoteCommandHandler(
        GemRemoteCommandHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_remoteCommandHandler is not null)
            {
                throw new InvalidOperationException(
                    "A remote-command handler is already registered.");
            }

            var registration = new GemRemoteCommandRegistration(
                handler,
                UnregisterRemoteCommandHandler);
            _remoteCommandHandler = registration;
            return registration;
        }
    }

    /// <summary>Registers one exact runtime alarm definition.</summary>
    public GemAlarmRegistration RegisterAlarm(GemAlarmDefinition definition)
    {
        if (definition is null)
            throw new ArgumentNullException(nameof(definition));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_alarms.ContainsKey(definition.AlarmId))
            {
                throw new InvalidOperationException(
                    $"Alarm {definition.AlarmId} is already registered.");
            }

            var registration = new GemAlarmRegistration(
                definition,
                UnregisterAlarm);
            _alarms.Add(definition.AlarmId, registration);
            _alarmCatalog.Add(registration);
            return registration;
        }
    }

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

        var executions = GetReportExecutions(
            eventId,
            out var sendPolicyHandler,
            out var communicationState,
            out var onlineState);
        if (sendPolicyHandler is not null &&
            !await sendPolicyHandler(
                communicationState,
                onlineState,
                dataId,
                eventId,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"The application policy rejected Collection Event {eventId}.");
        }

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

    /// <summary>Sends one alarm notification to the Host.</summary>
    public async Task SendAlarmNotificationAsync(
        GemAlarmNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (notification is null)
            throw new ArgumentNullException(nameof(notification));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_alarms.TryGetValue(notification.AlarmId, out var registration) &&
                !registration.IsSendEnabled)
            {
                throw new InvalidOperationException(
                    $"Alarm sending is disabled for {notification.AlarmId}.");
            }
        }

        var response = await _services.SendRequestAsync(
            Profile.AlarmNotification,
            GemOperation.AlarmNotification,
            GemMessageCodec.EncodeAlarmNotification(notification),
            cancellationToken).ConfigureAwait(false);
        _services.RequireAccepted(
            GemOperation.AlarmNotification,
            GemMessageCodec.DecodeAcknowledgement(
                response.Message.RootItem,
                "alarm-notification reply"));
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
            _alarms.Clear();
            _alarmCatalog.Clear();
            _onlineStateTransitionHandler = null;
            _collectionEventSendPolicyHandler = null;
            _remoteCommandAcceptanceHandler = null;
            _remoteCommandHandler = null;
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
        _services.AddRoute(Profile.RemoteCommand, HandleRemoteCommandAsync);
        _services.AddRoute(Profile.ListAlarms, HandleListAlarmsAsync);
        _services.AddRoute(Profile.AlarmSendControl, HandleAlarmSendControlAsync);
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
        GemOnlineState currentState;
        GemOnlineStateTransitionHandler? handler;
        lock (_gate)
        {
            ThrowIfDisposed();
            currentState = OnlineState;
            handler = _onlineStateTransitionHandler?.Handler;
        }

        var accepted = handler is null ||
            await handler(currentState, state, cancellationToken).ConfigureAwait(false);
        await _services.ReplyAsync(
            context,
            pair,
            GemMessageCodec.EncodeAcknowledgement(
                accepted
                    ? Profile.AcceptedAcknowledgement
                    : Profile.FailedAcknowledgement),
            cancellationToken).ConfigureAwait(false);
        if (accepted)
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

    private async ValueTask<SecsMessage?> HandleRemoteCommandAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, "remote-command request");
        var command = GemMessageCodec.DecodeRemoteCommand(
            context.Message.RootItem);
        GemRemoteCommandAcceptanceHandler? acceptanceHandler;
        GemRemoteCommandHandler? handler;
        GemCommunicationState communicationState;
        GemOnlineState onlineState;
        lock (_gate)
        {
            acceptanceHandler = _remoteCommandAcceptanceHandler?.Handler;
            handler = _remoteCommandHandler?.Handler;
            communicationState = CommunicationState;
            onlineState = OnlineState;
        }

        GemRemoteCommandResult result;
        if (handler is null ||
            (acceptanceHandler is not null &&
                !await acceptanceHandler(
                    communicationState,
                    onlineState,
                    command,
                    cancellationToken).ConfigureAwait(false)))
        {
            result = new GemRemoteCommandResult(
                Profile.FailedAcknowledgement,
                Array.Empty<GemRemoteCommandParameterResult>());
        }
        else
        {
            result = await handler(command, cancellationToken).ConfigureAwait(false) ??
                throw new GemProtocolException(
                    "The remote-command handler returned null.");
        }

        return GemEndpointServices.CreateSecondary(
            Profile.RemoteCommand,
            GemMessageCodec.EncodeRemoteCommandResult(result));
    }

    private ValueTask<SecsMessage?> HandleListAlarmsAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, "alarm-list request");
        var identifiers = GemMessageCodec.DecodeAlarmIdentifiers(
            context.Message.RootItem);
        IReadOnlyList<GemAlarmDefinition> definitions;
        lock (_gate)
        {
            ThrowIfDisposed();
            var snapshot = new List<GemAlarmDefinition>();
            if (identifiers.Count == 0)
            {
                foreach (var registration in _alarmCatalog)
                    snapshot.Add(registration.Definition);
            }
            else
            {
                foreach (var identifier in identifiers)
                {
                    if (_alarms.TryGetValue(identifier, out var registration))
                        snapshot.Add(registration.Definition);
                }
            }

            definitions = snapshot.AsReadOnly();
        }

        return new ValueTask<SecsMessage?>(
            GemEndpointServices.CreateSecondary(
                Profile.ListAlarms,
                GemMessageCodec.EncodeAlarmDefinitions(definitions)));
    }

    private ValueTask<SecsMessage?> HandleAlarmSendControlAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(
            context,
            "alarm-send control request");
        var control = GemMessageCodec.DecodeAlarmSendControl(
            context.Message.RootItem,
            Profile.AlarmControlCodec);
        var accepted = false;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_alarms.TryGetValue(control.AlarmId, out var registration))
            {
                registration.SetSendEnabled(control.Enabled);
                accepted = true;
            }
        }

        return new ValueTask<SecsMessage?>(
            GemEndpointServices.CreateSecondary(
                Profile.AlarmSendControl,
                GemMessageCodec.EncodeAcknowledgement(
                    accepted
                        ? Profile.AcceptedAcknowledgement
                        : Profile.FailedAcknowledgement)));
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

    private IReadOnlyList<ReportExecution> GetReportExecutions(
        SecsItem eventId,
        out GemCollectionEventSendPolicyHandler? sendPolicyHandler,
        out GemCommunicationState communicationState,
        out GemOnlineState onlineState)
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

            sendPolicyHandler = _collectionEventSendPolicyHandler?.Handler;
            communicationState = CommunicationState;
            onlineState = OnlineState;
            return Array.AsReadOnly(executions);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(GemEquipmentServices));
    }

    private void UnregisterRemoteCommandHandler(
        GemRemoteCommandRegistration registration)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_remoteCommandHandler, registration))
                _remoteCommandHandler = null;
        }
    }

    private void UnregisterRemoteCommandAcceptanceHandler(
        GemRemoteCommandAcceptanceRegistration registration)
    {
        lock (_gate)
        {
            if (ReferenceEquals(
                _remoteCommandAcceptanceHandler,
                registration))
            {
                _remoteCommandAcceptanceHandler = null;
            }
        }
    }

    private void UnregisterCollectionEventSendPolicyHandler(
        GemCollectionEventSendPolicyRegistration registration)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_collectionEventSendPolicyHandler, registration))
                _collectionEventSendPolicyHandler = null;
        }
    }

    private void UnregisterOnlineStateTransitionHandler(
        GemOnlineStateTransitionRegistration registration)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_onlineStateTransitionHandler, registration))
                _onlineStateTransitionHandler = null;
        }
    }

    private void UnregisterAlarm(GemAlarmRegistration registration)
    {
        lock (_gate)
        {
            if (_alarms.TryGetValue(registration.AlarmId, out var current) &&
                ReferenceEquals(current, registration))
            {
                _alarms.Remove(registration.AlarmId);
                _alarmCatalog.Remove(registration);
            }
        }
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
