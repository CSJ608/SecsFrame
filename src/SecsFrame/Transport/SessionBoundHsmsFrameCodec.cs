using System.Buffers;
using StreamFrame;

namespace SecsFrame;

internal sealed class SessionBoundHsmsFrameCodec : ICodec<HsmsTransportFrame>
{
    private readonly HsmsTransportSessionContext _sessionContext;
    private readonly HsmsFrameCodec _frameCodec = new();

    public SessionBoundHsmsFrameCodec(HsmsTransportSessionContext sessionContext)
    {
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
    }

    public HsmsTransportFrame Decode(in ReadOnlySequence<byte> frame, CancellationToken ct = default)
        => new(_sessionContext.GetCurrent(), _frameCodec.Decode(frame, ct));

    public void Encode(
        HsmsTransportFrame message,
        IBufferWriter<byte> writer,
        CancellationToken ct = default)
    {
        if (!_sessionContext.IsCurrent(message.SessionId))
            throw new HsmsTransportSessionExpiredException(message.SessionId);

        _frameCodec.Encode(message.Frame, writer, ct);
    }
}
