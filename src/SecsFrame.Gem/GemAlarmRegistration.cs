namespace SecsFrame.Gem;

/// <summary>Owns one exact Equipment alarm-catalog registration.</summary>
public sealed class GemAlarmRegistration : IDisposable
{
    private Action<GemAlarmRegistration>? _unregister;

    internal GemAlarmRegistration(
        GemAlarmDefinition definition,
        Action<GemAlarmRegistration> unregister)
    {
        Definition = definition;
        _unregister = unregister;
    }

    /// <summary>Gets the exact registered alarm identifier.</summary>
    public SecsItem AlarmId => Definition.AlarmId;

    internal GemAlarmDefinition Definition { get; }

    /// <summary>Removes this exact registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
