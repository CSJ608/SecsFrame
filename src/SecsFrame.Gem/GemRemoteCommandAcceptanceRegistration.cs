namespace SecsFrame.Gem;

/// <summary>Owns one Equipment remote-command acceptance registration.</summary>
public sealed class GemRemoteCommandAcceptanceRegistration : IDisposable
{
    private Action<GemRemoteCommandAcceptanceRegistration>? _unregister;

    internal GemRemoteCommandAcceptanceRegistration(
        GemRemoteCommandAcceptanceHandler handler,
        Action<GemRemoteCommandAcceptanceRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemRemoteCommandAcceptanceHandler Handler { get; }

    /// <summary>Removes this exact handler registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
