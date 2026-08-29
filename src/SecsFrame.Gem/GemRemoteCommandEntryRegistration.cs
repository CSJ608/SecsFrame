namespace SecsFrame.Gem;

/// <summary>Owns one exact Equipment remote-command directory entry.</summary>
public sealed class GemRemoteCommandEntryRegistration : IDisposable
{
    private Action<GemRemoteCommandEntryRegistration>? _unregister;
    private int _executionEnabled = 1;

    internal GemRemoteCommandEntryRegistration(
        SecsItem command,
        GemRemoteCommandHandler handler,
        Action<GemRemoteCommandEntryRegistration> unregister)
    {
        Command = command;
        Handler = handler;
        _unregister = unregister;
    }

    /// <summary>Gets the exact dynamic command identifier.</summary>
    public SecsItem Command { get; }

    /// <summary>Gets whether this command may currently execute.</summary>
    public bool IsExecutionEnabled => Volatile.Read(ref _executionEnabled) != 0;

    /// <summary>Changes whether this command may execute.</summary>
    public void SetExecutionEnabled(bool enabled)
        => Volatile.Write(ref _executionEnabled, enabled ? 1 : 0);

    internal GemRemoteCommandHandler Handler { get; }

    /// <summary>Removes this exact directory entry. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
