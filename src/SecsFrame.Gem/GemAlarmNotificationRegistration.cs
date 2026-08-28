namespace SecsFrame.Gem;

/// <summary>Owns one Host alarm-notification handler registration.</summary>
public sealed class GemAlarmNotificationRegistration : IDisposable
{
    private Action<GemAlarmNotificationRegistration>? _unregister;

    internal GemAlarmNotificationRegistration(
        GemAlarmNotificationHandler handler,
        Action<GemAlarmNotificationRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemAlarmNotificationHandler Handler { get; }

    /// <summary>Removes this exact handler registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
