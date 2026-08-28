namespace SecsFrame.Gem;

/// <summary>Owns one Host Collection Event handler registration.</summary>
public sealed class GemCollectionEventRegistration : IDisposable
{
    private Action<GemCollectionEventRegistration>? _unregister;

    internal GemCollectionEventRegistration(
        GemCollectionEventHandler handler,
        Action<GemCollectionEventRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemCollectionEventHandler Handler { get; }

    /// <summary>Removes this exact handler registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
