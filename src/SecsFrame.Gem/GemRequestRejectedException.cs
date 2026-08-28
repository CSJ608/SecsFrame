using System.IO;

namespace SecsFrame.Gem;

/// <summary>Reports a non-success acknowledgement from a GEM peer.</summary>
public sealed class GemRequestRejectedException : IOException
{
    /// <summary>Creates a rejected-operation error.</summary>
    public GemRequestRejectedException(
        GemOperation operation,
        byte acknowledgement)
        : base(
            $"The GEM {operation} operation was rejected with acknowledgement " +
            $"value {acknowledgement}.")
    {
        Operation = operation;
        Acknowledgement = acknowledgement;
    }

    /// <summary>Gets the rejected operation.</summary>
    public GemOperation Operation { get; }

    /// <summary>Gets the peer-provided acknowledgement byte.</summary>
    public byte Acknowledgement { get; }
}
