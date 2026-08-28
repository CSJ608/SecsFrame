namespace SecsFrame.Gem;

/// <summary>Contains one raw remote-command completion result.</summary>
public sealed class GemRemoteCommandResult
{
    /// <summary>Creates a result without interpreting acknowledgement codes.</summary>
    public GemRemoteCommandResult(
        byte acknowledgement,
        IEnumerable<GemRemoteCommandParameterResult> parameterResults)
    {
        Acknowledgement = acknowledgement;
        ParameterResults = GemCollection.Copy(
            parameterResults,
            nameof(parameterResults));
        var names = new HashSet<SecsItem>();
        for (var index = 0; index < ParameterResults.Count; index++)
        {
            if (!names.Add(ParameterResults[index].Name))
            {
                throw new ArgumentException(
                    $"Parameter-result name at index {index} is duplicated.",
                    nameof(parameterResults));
            }
        }
    }

    /// <summary>Gets the exact command acknowledgement byte.</summary>
    public byte Acknowledgement { get; }

    /// <summary>Gets parameter results in reply order.</summary>
    public IReadOnlyList<GemRemoteCommandParameterResult> ParameterResults { get; }
}
