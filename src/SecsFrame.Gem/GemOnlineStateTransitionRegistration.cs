namespace SecsFrame.Gem;

/// <summary>Owns one Equipment online-state transition handler registration.</summary>
public sealed class GemOnlineStateTransitionRegistration : IDisposable
{
    private Action<GemOnlineStateTransitionRegistration>? _unregister;

    internal GemOnlineStateTransitionRegistration(
        GemOnlineStateTransitionHandler handler,
        Action<GemOnlineStateTransitionRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemOnlineStateTransitionHandler Handler { get; }

    /// <summary>Removes this exact handler registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
