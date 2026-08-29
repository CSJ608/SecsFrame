namespace SecsFrame.Gem;

/// <summary>Owns one exact Equipment alarm-catalog registration.</summary>
public sealed class GemAlarmRegistration : IDisposable
{
    private Action<GemAlarmRegistration>? _unregister;
    private int _sendEnabled = 1;

    internal GemAlarmRegistration(
        GemAlarmDefinition definition,
        Action<GemAlarmRegistration> unregister)
    {
        Definition = definition;
        _unregister = unregister;
    }

    /// <summary>Gets the exact registered alarm identifier.</summary>
    public SecsItem AlarmId => Definition.AlarmId;

    /// <summary>Gets whether notifications for this registration may be sent.</summary>
    public bool IsSendEnabled => Volatile.Read(ref _sendEnabled) != 0;

    internal GemAlarmDefinition Definition { get; }

    internal void SetSendEnabled(bool enabled)
        => Volatile.Write(ref _sendEnabled, enabled ? 1 : 0);

    /// <summary>Removes this exact registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
