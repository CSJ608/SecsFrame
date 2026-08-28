namespace SecsFrame;

/// <summary>
/// Owns one role-specific SECS endpoint composed from an HSMS connection and
/// runtime Primary router.
/// </summary>
public abstract class SecsEndpoint : IAsyncDisposable
{
    private readonly HsmsConnection _connection;
    private readonly HsmsPrimaryRouter _router;

    private protected SecsEndpoint(
        SecsEndpointRole role,
        HsmsConnectionOptions options)
    {
        Role = role;
        _connection = new HsmsConnection(
            options ?? throw new ArgumentNullException(nameof(options)));
        _router = new HsmsPrimaryRouter(_connection);
    }

    /// <summary>Gets the fixed SECS application role.</summary>
    public SecsEndpointRole Role { get; }

    /// <summary>Gets the immutable HSMS connection settings.</summary>
    public HsmsConnectionOptions Options => _connection.Options;

    /// <summary>Gets the latest observed HSMS session state.</summary>
    public HsmsSessionState State => _connection.State;

    /// <summary>Starts the owned connection.</summary>
    public void Start()
        => _connection.Start();

    /// <summary>Waits until the owned connection next reaches Selected.</summary>
    public Task WaitUntilSelectedAsync(
        CancellationToken cancellationToken = default)
        => _connection.WaitUntilSelectedAsync(cancellationToken);

    /// <summary>Gets the owned connection's single-consumer event stream.</summary>
    public IAsyncEnumerable<HsmsConnectionEvent> GetEventsAsync(
        CancellationToken cancellationToken = default)
        => _connection.GetEventsAsync(cancellationToken);

    /// <summary>Sends a dynamic Primary through the owned connection.</summary>
    public Task<HsmsDataMessage?> SendAsync(
        SecsMessage primary,
        CancellationToken cancellationToken = default)
        => _connection.SendAsync(primary, cancellationToken);

    /// <summary>Replies once to an unhandled incoming Primary.</summary>
    public Task ReplyAsync(
        HsmsIncomingDataMessage incoming,
        SecsMessage secondary,
        CancellationToken cancellationToken = default)
        => _connection.ReplyAsync(
            incoming,
            secondary,
            cancellationToken);

    /// <summary>Registers one runtime Primary handler for this endpoint.</summary>
    public HsmsPrimaryRouteRegistration RegisterPrimaryHandler(
        byte stream,
        byte function,
        HsmsPrimaryHandler handler)
        => _router.Register(stream, function, handler);

    /// <summary>Attempts to dispatch a connection event to a registered handler.</summary>
    public ValueTask<bool> TryDispatchAsync(
        HsmsConnectionEvent connectionEvent,
        CancellationToken cancellationToken = default)
        => _router.TryDispatchAsync(connectionEvent, cancellationToken);

    /// <summary>Sends Linktest Request and waits for its matching response.</summary>
    public Task LinktestAsync(CancellationToken cancellationToken = default)
        => _connection.LinktestAsync(cancellationToken);

    /// <summary>Sends Deselect Request and waits for its matching response.</summary>
    public Task DeselectAsync(CancellationToken cancellationToken = default)
        => _connection.DeselectAsync(cancellationToken);

    /// <summary>Sends Separate Request and closes the selected TCP session.</summary>
    public Task SeparateAsync(CancellationToken cancellationToken = default)
        => _connection.SeparateAsync(cancellationToken);

    /// <summary>Stops and disposes the owned connection.</summary>
    public ValueTask DisposeAsync()
        => _connection.DisposeAsync();
}
