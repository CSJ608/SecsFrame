namespace SecsFrame;

/// <summary>
/// Routes unconsumed HSMS data messages to runtime Stream/Function handlers.
/// </summary>
/// <remarks>
/// This type does not consume <see cref="HsmsConnection.GetEventsAsync"/> itself.
/// Call <see cref="TryDispatchAsync(HsmsConnectionEvent, CancellationToken)"/>
/// from the application's single event loop so unmatched events remain visible.
/// </remarks>
public sealed class HsmsPrimaryRouter
{
    private readonly HsmsConnection _connection;
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly Dictionary<ushort, HsmsPrimaryRouteRegistration> _routes = new();

    /// <summary>Creates a router that sends replies through the given connection.</summary>
    public HsmsPrimaryRouter(HsmsConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>Registers one exact Stream/Function handler.</summary>
    /// <exception cref="InvalidOperationException">
    /// The same route already has an active registration.
    /// </exception>
    public HsmsPrimaryRouteRegistration Register(
        byte stream,
        byte function,
        HsmsPrimaryHandler handler)
    {
        if (stream > 0x7F)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stream),
                stream,
                "The stream number must be between 0 and 127.");
        }

        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        var key = GetKey(stream, function);
        lock (_gate)
        {
            if (_routes.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"A handler is already registered for S{stream}F{function}.");
            }

            var registration = new HsmsPrimaryRouteRegistration(
                this,
                stream,
                function,
                handler);
            _routes.Add(key, registration);
            return registration;
        }
    }

    /// <summary>
    /// Dispatches a data-message event when an exact route is registered.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a handler was invoked; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public ValueTask<bool> TryDispatchAsync(
        HsmsConnectionEvent connectionEvent,
        CancellationToken cancellationToken = default)
    {
        if (connectionEvent is null)
            throw new ArgumentNullException(nameof(connectionEvent));

        return connectionEvent.Kind == HsmsConnectionEventKind.DataMessageReceived
            ? TryDispatchAsync(
                connectionEvent.IncomingMessage!,
                cancellationToken)
            : new ValueTask<bool>(false);
    }

    /// <summary>
    /// Dispatches an incoming data message when an exact route is registered.
    /// </summary>
    /// <remarks>
    /// The router treats any unconsumed data message matching the registered
    /// Stream/Function as a route candidate. Applications should register only
    /// the Primary functions they own.
    /// </remarks>
    public ValueTask<bool> TryDispatchAsync(
        HsmsIncomingDataMessage incomingMessage,
        CancellationToken cancellationToken = default)
    {
        if (incomingMessage is null)
            throw new ArgumentNullException(nameof(incomingMessage));

        var message = incomingMessage.DataMessage.Message;
        HsmsPrimaryHandler? handler;
        lock (_gate)
        {
            handler = _routes.TryGetValue(
                GetKey(message.Stream, message.Function),
                out var registration)
                ? registration.Handler
                : null;
        }

        return handler is null
            ? new ValueTask<bool>(false)
            : DispatchAsync(
                incomingMessage,
                handler,
                cancellationToken);
    }

    internal void Unregister(HsmsPrimaryRouteRegistration registration)
    {
        var key = GetKey(registration.Stream, registration.Function);
        lock (_gate)
        {
            if (_routes.TryGetValue(key, out var current) &&
                ReferenceEquals(current, registration))
            {
                _routes.Remove(key);
            }
        }
    }

    private async ValueTask<bool> DispatchAsync(
        HsmsIncomingDataMessage incomingMessage,
        HsmsPrimaryHandler handler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var secondary = await handler(
            new HsmsPrimaryContext(incomingMessage),
            cancellationToken).ConfigureAwait(false);
        if (secondary is not null)
        {
            if (!incomingMessage.ReplyExpected)
            {
                throw new InvalidOperationException(
                    "The routed data message does not request a secondary reply.");
            }

            await _connection.ReplyAsync(
                incomingMessage,
                secondary,
                cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private static ushort GetKey(byte stream, byte function)
        => (ushort)((stream << 8) | function);
}
