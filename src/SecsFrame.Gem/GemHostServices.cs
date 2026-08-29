namespace SecsFrame.Gem;

/// <summary>Provides foundational, profile-driven GEM operations for a Host endpoint.</summary>
public sealed class GemHostServices : IDisposable
{
    private readonly GemEndpointServices _services;
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private GemCollectionEventRegistration? _collectionEventHandler;
    private GemAlarmNotificationRegistration? _alarmNotificationHandler;
    private int _disposed;

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
        try
        {
            _services.AddRoute(
                _services.Profile.CollectionEvent,
                HandleCollectionEventAsync);
            _services.AddRoute(
                _services.Profile.AlarmNotification,
                HandleAlarmNotificationAsync);
        }
        catch
        {
            _services.Dispose();
            throw;
        }
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

    /// <summary>Atomically replaces the Equipment report-definition set.</summary>
    public async Task DefineReportsAsync(
        SecsItem dataId,
        IEnumerable<GemReportDefinition> reports,
        CancellationToken cancellationToken = default)
    {
        var response = await _services.SendRequestAsync(
            Profile.DefineReports,
            GemOperation.DefineReports,
            GemMessageCodec.EncodeReportDefinitions(dataId, reports),
            cancellationToken).ConfigureAwait(false);
        _services.RequireAccepted(
            GemOperation.DefineReports,
            GemMessageCodec.DecodeAcknowledgement(
                response.Message.RootItem,
                "report-definition reply"));
    }

    /// <summary>Atomically replaces the Equipment event-to-report link set.</summary>
    public async Task LinkEventReportsAsync(
        SecsItem dataId,
        IEnumerable<GemEventReportLink> links,
        CancellationToken cancellationToken = default)
    {
        var response = await _services.SendRequestAsync(
            Profile.LinkEventReports,
            GemOperation.LinkEventReports,
            GemMessageCodec.EncodeEventReportLinks(dataId, links),
            cancellationToken).ConfigureAwait(false);
        _services.RequireAccepted(
            GemOperation.LinkEventReports,
            GemMessageCodec.DecodeAcknowledgement(
                response.Message.RootItem,
                "event-report-link reply"));
    }

    /// <summary>Sends a remote command and returns its uninterpreted result.</summary>
    public async Task<GemRemoteCommandResult> ExecuteRemoteCommandAsync(
        GemRemoteCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        var response = await _services.SendRequestAsync(
            Profile.RemoteCommand,
            GemOperation.RemoteCommand,
            GemMessageCodec.EncodeRemoteCommand(command),
            cancellationToken).ConfigureAwait(false);
        return GemMessageCodec.DecodeRemoteCommandResult(
            response.Message.RootItem);
    }

    /// <summary>Lists all or selected Equipment alarm definitions.</summary>
    public async Task<IReadOnlyList<GemAlarmDefinition>> ListAlarmsAsync(
        IEnumerable<SecsItem>? alarmIds = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _services.SendRequestAsync(
            Profile.ListAlarms,
            GemOperation.ListAlarms,
            GemMessageCodec.EncodeAlarmIdentifiers(
                alarmIds ?? Array.Empty<SecsItem>()),
            cancellationToken).ConfigureAwait(false);
        return GemMessageCodec.DecodeAlarmDefinitions(
            response.Message.RootItem);
    }

    /// <summary>Enables or disables sending for one registered Equipment alarm.</summary>
    public async Task SetAlarmSendEnabledAsync(
        SecsItem alarmId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var response = await _services.SendRequestAsync(
            Profile.AlarmSendControl,
            GemOperation.SetAlarmSendEnabled,
            GemMessageCodec.EncodeAlarmSendControl(
                alarmId,
                enabled,
                Profile.AlarmControlCodec),
            cancellationToken).ConfigureAwait(false);
        _services.RequireAccepted(
            GemOperation.SetAlarmSendEnabled,
            GemMessageCodec.DecodeAcknowledgement(
                response.Message.RootItem,
                "alarm-send control reply"));
    }

    /// <summary>Registers the single application Collection Event handler.</summary>
    public GemCollectionEventRegistration RegisterCollectionEventHandler(
        GemCollectionEventHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_collectionEventHandler is not null)
            {
                throw new InvalidOperationException(
                    "A Collection Event handler is already registered.");
            }

            var registration = new GemCollectionEventRegistration(
                handler,
                UnregisterCollectionEventHandler);
            _collectionEventHandler = registration;
            return registration;
        }
    }

    /// <summary>Registers the single application alarm-notification handler.</summary>
    public GemAlarmNotificationRegistration RegisterAlarmNotificationHandler(
        GemAlarmNotificationHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_alarmNotificationHandler is not null)
            {
                throw new InvalidOperationException(
                    "An alarm-notification handler is already registered.");
            }

            var registration = new GemAlarmNotificationRegistration(
                handler,
                UnregisterAlarmNotificationHandler);
            _alarmNotificationHandler = registration;
            return registration;
        }
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
            _collectionEventHandler = null;
            _alarmNotificationHandler = null;
        }
        _services.Dispose();
    }

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

    private async ValueTask<SecsMessage?> HandleCollectionEventAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, "Collection Event");
        var collectionEvent = GemMessageCodec.DecodeCollectionEvent(
            context.Message.RootItem);
        GemCollectionEventHandler? handler;
        lock (_gate)
            handler = _collectionEventHandler?.Handler;

        var accepted = handler is not null &&
            await handler(collectionEvent, cancellationToken).ConfigureAwait(false);
        return GemEndpointServices.CreateSecondary(
            Profile.CollectionEvent,
            GemMessageCodec.EncodeAcknowledgement(
                accepted
                    ? Profile.AcceptedAcknowledgement
                    : Profile.FailedAcknowledgement));
    }

    private void UnregisterCollectionEventHandler(
        GemCollectionEventRegistration registration)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_collectionEventHandler, registration))
                _collectionEventHandler = null;
        }
    }

    private async ValueTask<SecsMessage?> HandleAlarmNotificationAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, "alarm notification");
        var notification = GemMessageCodec.DecodeAlarmNotification(
            context.Message.RootItem);
        GemAlarmNotificationHandler? handler;
        lock (_gate)
            handler = _alarmNotificationHandler?.Handler;

        var accepted = handler is not null &&
            await handler(notification, cancellationToken).ConfigureAwait(false);
        return GemEndpointServices.CreateSecondary(
            Profile.AlarmNotification,
            GemMessageCodec.EncodeAcknowledgement(
                accepted
                    ? Profile.AcceptedAcknowledgement
                    : Profile.FailedAcknowledgement));
    }

    private void UnregisterAlarmNotificationHandler(
        GemAlarmNotificationRegistration registration)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_alarmNotificationHandler, registration))
                _alarmNotificationHandler = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(GemHostServices));
    }
}
