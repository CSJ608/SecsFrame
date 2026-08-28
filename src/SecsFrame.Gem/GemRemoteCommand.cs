namespace SecsFrame.Gem;

/// <summary>Contains one dynamic remote-command request.</summary>
public sealed class GemRemoteCommand
{
    /// <summary>Creates a remote-command request.</summary>
    public GemRemoteCommand(
        SecsItem command,
        IEnumerable<GemRemoteCommandParameter> parameters)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Parameters = GemCollection.Copy(parameters, nameof(parameters));
        var names = new HashSet<SecsItem>();
        for (var index = 0; index < Parameters.Count; index++)
        {
            if (!names.Add(Parameters[index].Name))
            {
                throw new ArgumentException(
                    $"Parameter name at index {index} is duplicated.",
                    nameof(parameters));
            }
        }
    }

    /// <summary>Gets the exact dynamic command identifier.</summary>
    public SecsItem Command { get; }

    /// <summary>Gets parameters in request order.</summary>
    public IReadOnlyList<GemRemoteCommandParameter> Parameters { get; }
}
