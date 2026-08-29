namespace SecsFrame.Gem;

/// <summary>Owns one exact Equipment remote-command directory entry.</summary>
public sealed class GemRemoteCommandEntryRegistration : IDisposable
{
    private Action<GemRemoteCommandEntryRegistration>? _unregister;

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

    internal GemRemoteCommandHandler Handler { get; }

    /// <summary>Removes this exact directory entry. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
