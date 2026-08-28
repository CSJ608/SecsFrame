using StreamFrame;

namespace SecsFrame;

internal static class HsmsStreamConnectionOptionsAdapter
{
    public static StreamConnectionOptions Create(
        bool isActive,
        HsmsTransportOptions hsmsOptions,
        StreamConnectionOptions? source = null)
    {
        if (hsmsOptions is null)
            throw new ArgumentNullException(nameof(hsmsOptions));

        source ??= new StreamConnectionOptions();
        var adapted = new StreamConnectionOptions
        {
            ConnectRetryDelayMs = source.ConnectRetryDelayMs,
            AcceptRetryDelayMs = source.AcceptRetryDelayMs,
            MaxRetryDelayMs = source.MaxRetryDelayMs,
            SocketReceiveBufferSize = source.SocketReceiveBufferSize,
            SendQueueCapacity = source.SendQueueCapacity,
            EncodeBufferInitialSize = source.EncodeBufferInitialSize,
            UseStreamingEncode = source.UseStreamingEncode,
            AcceptFirstClientOnly = source.AcceptFirstClientOnly,
            DecodeErrorPolicy = source.DecodeErrorPolicy,
            MaxIncompleteFrameBufferBytes =
                source.MaxIncompleteFrameBufferBytes,
            TcpKeepAlive = source.TcpKeepAlive,
            KeepAliveTimeMs = source.KeepAliveTimeMs,
            KeepAliveIntervalMs = source.KeepAliveIntervalMs,
            ReceiveQueueCapacity = source.ReceiveQueueCapacity,
            ReceiveIdleTimeoutMs = source.ReceiveIdleTimeoutMs,
        };

        if (isActive)
        {
            adapted.ConnectRetryDelayMs = hsmsOptions.T5Milliseconds;
            adapted.MaxRetryDelayMs = 0;
        }

        return adapted;
    }
}
