namespace SecsFrame.Gem;

/// <summary>Owns one Equipment alarm-notification send-policy registration.</summary>
public sealed class GemAlarmNotificationSendPolicyRegistration : IDisposable
{
    private Action<GemAlarmNotificationSendPolicyRegistration>? _unregister;

    internal GemAlarmNotificationSendPolicyRegistration(
        GemAlarmNotificationSendPolicyHandler handler,
        Action<GemAlarmNotificationSendPolicyRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemAlarmNotificationSendPolicyHandler Handler { get; }

    /// <summary>Removes this exact policy registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
