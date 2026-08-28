namespace SecsFrame.Gem;

internal sealed class GemEndpointServices : IDisposable
{
    private readonly SecsEndpoint _endpoint;
    private readonly List<HsmsPrimaryRouteRegistration> _routes = new();
    private GemIdentity? _peerIdentity;
    private int _communicationState;
    private int _onlineState;
    private int _disposed;

    internal GemEndpointServices(
        SecsEndpoint endpoint,
        GemIdentity identity,
        GemMessageProfile profile)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        try
        {
            AddRoute(
                profile.EstablishCommunication,
                HandleEstablishCommunicationAsync);
            AddRoute(profile.AreYouOnline, HandleAreYouOnlineAsync);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal GemIdentity Identity { get; }

    internal GemMessageProfile Profile { get; }

    internal GemIdentity? PeerIdentity => Volatile.Read(ref _peerIdentity);

    internal GemCommunicationState CommunicationState
        => (GemCommunicationState)Volatile.Read(ref _communicationState);

    internal GemOnlineState OnlineState
        => (GemOnlineState)Volatile.Read(ref _onlineState);

    internal async Task<GemIdentity> EstablishCommunicationAsync(
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            Profile.EstablishCommunication,
            GemOperation.EstablishCommunication,
            GemMessageCodec.EncodeIdentity(Identity),
            cancellationToken).ConfigureAwait(false);
        var reply = GemMessageCodec.DecodeCommunicationReply(
            response.Message.RootItem);
        RequireAccepted(
            GemOperation.EstablishCommunication,
            reply.Acknowledgement);
        if (reply.Identity is null)
        {
            throw new GemProtocolException(
                "An accepted communication-establishment reply must include identity.");
        }

        SetCommunicating(reply.Identity);
        return reply.Identity;
    }

    internal async Task<GemIdentity> AreYouOnlineAsync(
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            Profile.AreYouOnline,
            GemOperation.AreYouOnline,
            rootItem: null,
            cancellationToken).ConfigureAwait(false);
        var identity = GemMessageCodec.DecodeIdentity(
            response.Message.RootItem,
            "online-query reply");
        Volatile.Write(ref _peerIdentity, identity);
        return identity;
    }

    internal async Task RequestOnlineStateAsync(
        GemMessagePair pair,
        GemOperation operation,
        GemOnlineState state,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            pair,
            operation,
            rootItem: null,
            cancellationToken).ConfigureAwait(false);
        RequireAccepted(
            operation,
            GemMessageCodec.DecodeAcknowledgement(
                response.Message.RootItem,
                $"{operation} reply"));
        Volatile.Write(ref _onlineState, (int)state);
    }

    internal async Task<HsmsDataMessage> SendRequestAsync(
        GemMessagePair pair,
        GemOperation operation,
        SecsItem? rootItem,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var response = await _endpoint.SendAsync(
            new SecsMessage(
                pair.Stream,
                pair.PrimaryFunction,
                replyExpected: true,
                rootItem),
            cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            throw new GemProtocolException(
                $"The {operation} transaction completed without a Secondary.");
        }

        GemMessageCodec.RequireSecondary(response, pair, operation);
        return response;
    }

    internal void AddRoute(
        GemMessagePair pair,
        HsmsPrimaryHandler handler)
    {
        ThrowIfDisposed();
        _routes.Add(_endpoint.RegisterPrimaryHandler(
            pair.Stream,
            pair.PrimaryFunction,
            handler));
    }

    internal ValueTask<bool> TryDispatchAsync(
        HsmsConnectionEvent connectionEvent,
        CancellationToken cancellationToken)
    {
        if (connectionEvent is null)
            throw new ArgumentNullException(nameof(connectionEvent));

        ThrowIfDisposed();
        if (connectionEvent.Kind == HsmsConnectionEventKind.StateChanged &&
            connectionEvent.State != HsmsSessionState.Selected)
        {
            ResetState();
        }

        return _endpoint.TryDispatchAsync(connectionEvent, cancellationToken);
    }

    internal void SetOnlineState(GemOnlineState state)
        => Volatile.Write(ref _onlineState, (int)state);

    internal Task ReplyAsync(
        HsmsPrimaryContext context,
        GemMessagePair pair,
        SecsItem? rootItem,
        CancellationToken cancellationToken)
        => _endpoint.ReplyAsync(
            context.IncomingMessage,
            CreateSecondary(pair, rootItem),
            cancellationToken);

    internal void RequireAccepted(GemOperation operation, byte acknowledgement)
    {
        if (acknowledgement != Profile.AcceptedAcknowledgement)
            throw new GemRequestRejectedException(operation, acknowledgement);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        for (var index = _routes.Count - 1; index >= 0; index--)
            _routes[index].Dispose();
        _routes.Clear();
        ResetState();
    }

    private async ValueTask<SecsMessage?> HandleEstablishCommunicationAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(
            context,
            "communication-establishment request");
        var identity = GemMessageCodec.DecodeIdentity(
            context.Message.RootItem,
            "communication-establishment request");
        await ReplyAsync(
            context,
            Profile.EstablishCommunication,
            GemMessageCodec.EncodeCommunicationReply(
                Profile.AcceptedAcknowledgement,
                Identity),
            cancellationToken).ConfigureAwait(false);
        SetCommunicating(identity);
        return null;
    }

    private ValueTask<SecsMessage?> HandleAreYouOnlineAsync(
        HsmsPrimaryContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GemMessageCodec.RequireReplyExpected(context, "online-query request");
        GemMessageCodec.RequireEmptyBody(
            context.Message.RootItem,
            "online-query request");
        return new ValueTask<SecsMessage?>(
            CreateSecondary(
                Profile.AreYouOnline,
                GemMessageCodec.EncodeIdentity(Identity)));
    }

    internal static SecsMessage CreateSecondary(
        GemMessagePair pair,
        SecsItem? rootItem)
        => new(pair.Stream, pair.SecondaryFunction, rootItem: rootItem);

    private void SetCommunicating(GemIdentity peerIdentity)
    {
        Volatile.Write(ref _peerIdentity, peerIdentity);
        Volatile.Write(
            ref _communicationState,
            (int)GemCommunicationState.Communicating);
    }

    private void ResetState()
    {
        Volatile.Write(ref _peerIdentity, null);
        Volatile.Write(
            ref _communicationState,
            (int)GemCommunicationState.NotCommunicating);
        Volatile.Write(ref _onlineState, (int)GemOnlineState.Offline);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(GemEndpointServices));
    }
}
