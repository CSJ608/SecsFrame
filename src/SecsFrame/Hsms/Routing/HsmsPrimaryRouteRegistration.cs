namespace SecsFrame;

/// <summary>Owns one runtime Stream/Function handler registration.</summary>
public sealed class HsmsPrimaryRouteRegistration : IDisposable
{
    private HsmsPrimaryRouter? _owner;

    internal HsmsPrimaryRouteRegistration(
        HsmsPrimaryRouter owner,
        byte stream,
        byte function,
        HsmsPrimaryHandler handler)
    {
        _owner = owner;
        Stream = stream;
        Function = function;
        Handler = handler;
    }

    /// <summary>Gets the registered Stream.</summary>
    public byte Stream { get; }

    /// <summary>Gets the registered Function.</summary>
    public byte Function { get; }

    internal HsmsPrimaryHandler Handler { get; }

    /// <summary>Removes this exact registration. Repeated calls have no effect.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _owner, null)?.Unregister(this);
}
