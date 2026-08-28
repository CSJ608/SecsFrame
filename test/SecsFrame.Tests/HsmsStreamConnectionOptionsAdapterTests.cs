using StreamFrame;

namespace SecsFrame.Tests;

public sealed class HsmsStreamConnectionOptionsAdapterTests
{
    [Fact]
    public void Active_options_apply_T5_disable_backoff_and_preserve_source()
    {
        var source = CreateSource();
        var hsmsOptions = new HsmsTransportOptions(
            TimeSpan.FromMilliseconds(1250),
            TimeSpan.FromSeconds(5));

        var adapted = HsmsStreamConnectionOptionsAdapter.Create(
            isActive: true,
            hsmsOptions,
            source);

        Assert.NotSame(source, adapted);
        Assert.Equal(1250, adapted.ConnectRetryDelayMs);
        Assert.Equal(0, adapted.MaxRetryDelayMs);
        Assert.Equal(source.AcceptRetryDelayMs, adapted.AcceptRetryDelayMs);
        AssertOtherOptionsEqual(source, adapted);
        Assert.Equal(111, source.ConnectRetryDelayMs);
        Assert.Equal(333, source.MaxRetryDelayMs);
    }

    [Fact]
    public void Passive_options_preserve_StreamFrame_retry_configuration()
    {
        var source = CreateSource();
        var hsmsOptions = new HsmsTransportOptions(
            TimeSpan.FromMilliseconds(1250),
            TimeSpan.FromSeconds(5));

        var adapted = HsmsStreamConnectionOptionsAdapter.Create(
            isActive: false,
            hsmsOptions,
            source);

        Assert.NotSame(source, adapted);
        Assert.Equal(source.ConnectRetryDelayMs, adapted.ConnectRetryDelayMs);
        Assert.Equal(source.AcceptRetryDelayMs, adapted.AcceptRetryDelayMs);
        Assert.Equal(source.MaxRetryDelayMs, adapted.MaxRetryDelayMs);
        AssertOtherOptionsEqual(source, adapted);
    }

    [Fact]
    public void T5_at_StreamFrame_maximum_is_lossless()
    {
        var options = new HsmsTransportOptions(
            TimeSpan.FromMilliseconds(int.MaxValue),
            TimeSpan.FromTicks(1));

        Assert.Equal(TimeSpan.FromMilliseconds(int.MaxValue), options.T5);
        Assert.Equal(int.MaxValue, options.T5Milliseconds);
        Assert.Equal(TimeSpan.FromTicks(1), options.T8);
    }

    [Fact]
    public void Nonpositive_T5_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsTransportOptions(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsTransportOptions(
                TimeSpan.FromTicks(-1),
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Submillisecond_T5_is_rejected_instead_of_rounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsTransportOptions(
                TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond + 1),
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void T5_beyond_StreamFrame_range_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsTransportOptions(
                TimeSpan.FromMilliseconds((long)int.MaxValue + 1),
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Nonpositive_T8_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsTransportOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsTransportOptions(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromTicks(-1)));
    }

    private static StreamConnectionOptions CreateSource()
        => new()
        {
            ConnectRetryDelayMs = 111,
            AcceptRetryDelayMs = 222,
            MaxRetryDelayMs = 333,
            SocketReceiveBufferSize = 4096,
            SendQueueCapacity = 7,
            EncodeBufferInitialSize = 123,
            UseStreamingEncode = true,
            AcceptFirstClientOnly = false,
            MaxIncompleteFrameBufferBytes = 8192,
            TcpKeepAlive = true,
            KeepAliveTimeMs = 444,
            KeepAliveIntervalMs = 555,
            ReceiveQueueCapacity = 6,
            ReceiveIdleTimeoutMs = 777,
        };

    private static void AssertOtherOptionsEqual(
        StreamConnectionOptions expected,
        StreamConnectionOptions actual)
    {
        Assert.Equal(
            expected.SocketReceiveBufferSize,
            actual.SocketReceiveBufferSize);
        Assert.Equal(expected.SendQueueCapacity, actual.SendQueueCapacity);
        Assert.Equal(
            expected.EncodeBufferInitialSize,
            actual.EncodeBufferInitialSize);
        Assert.Equal(expected.UseStreamingEncode, actual.UseStreamingEncode);
        Assert.Equal(
            expected.AcceptFirstClientOnly,
            actual.AcceptFirstClientOnly);
        Assert.Equal(expected.DecodeErrorPolicy, actual.DecodeErrorPolicy);
        Assert.Equal(
            expected.MaxIncompleteFrameBufferBytes,
            actual.MaxIncompleteFrameBufferBytes);
        Assert.Equal(expected.TcpKeepAlive, actual.TcpKeepAlive);
        Assert.Equal(expected.KeepAliveTimeMs, actual.KeepAliveTimeMs);
        Assert.Equal(
            expected.KeepAliveIntervalMs,
            actual.KeepAliveIntervalMs);
        Assert.Equal(expected.ReceiveQueueCapacity, actual.ReceiveQueueCapacity);
        Assert.Equal(
            expected.ReceiveIdleTimeoutMs,
            actual.ReceiveIdleTimeoutMs);
    }
}
