namespace SecsFrame.Gem;

/// <summary>Owns one Equipment remote-command handler registration.</summary>
public sealed class GemRemoteCommandRegistration : IDisposable
{
    private Action<GemRemoteCommandRegistration>? _unregister;

    internal GemRemoteCommandRegistration(
        GemRemoteCommandHandler handler,
        Action<GemRemoteCommandRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemRemoteCommandHandler Handler { get; }

    /// <summary>Removes this exact handler registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
