using System.IO;

namespace SecsFrame;

internal sealed class HsmsTransportSessionExpiredException : IOException
{
    public HsmsTransportSessionExpiredException(
        HsmsTransportSessionId sessionId,
        string? message = null)
        : base(message ?? $"Transport session {sessionId.Value} is no longer active.")
    {
        SessionId = sessionId;
    }

    public HsmsTransportSessionId SessionId { get; }
}
