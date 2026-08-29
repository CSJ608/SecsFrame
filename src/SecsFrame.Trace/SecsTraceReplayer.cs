using System.Collections.ObjectModel;

namespace SecsFrame.Trace;

/// <summary>Replays explicitly allowed sent records through a normal send API.</summary>
public sealed class SecsTraceReplayer
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    /// <summary>The default maximum number of source records in one replay request.</summary>
    public const int DefaultMaxRecordCount = SecsTraceCodec.DefaultMaxRecordCount;

    /// <summary>Creates a controlled replayer with an explicit source-record limit.</summary>
    public SecsTraceReplayer(int maxRecordCount = DefaultMaxRecordCount)
        : this(maxRecordCount, Task.Delay)
    {
    }

    internal SecsTraceReplayer(
        int maxRecordCount,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        if (maxRecordCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRecordCount), maxRecordCount, "The maximum record count must be positive.");
        MaxRecordCount = maxRecordCount;
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
    }

    /// <summary>Gets the maximum number of source records in one replay request.</summary>
    public int MaxRecordCount { get; }

    /// <summary>
    /// Replays allowed sent records through an existing selected connection.
    /// Original Session ID, System Bytes, timestamps, and reply tokens are ignored.
    /// </summary>
    public Task<IReadOnlyList<SecsTraceReplayResult>> ReplayAsync(
        IEnumerable<SecsTraceRecord> records,
        HsmsConnection connection,
        Func<SecsTraceRecord, bool> isAllowed,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
            throw new ArgumentNullException(nameof(connection));

        return ReplayCoreAsync(records, connection.SendAsync, isAllowed, timingOptions: null, cancellationToken);
    }

    /// <summary>
    /// Replays allowed sent records through an existing selected connection and
    /// waits scaled source intervals between completed sends.
    /// </summary>
    public Task<IReadOnlyList<SecsTraceReplayResult>> ReplayWithTimingAsync(
        IEnumerable<SecsTraceRecord> records,
        HsmsConnection connection,
        Func<SecsTraceRecord, bool> isAllowed,
        SecsTraceReplayTimingOptions timingOptions,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
            throw new ArgumentNullException(nameof(connection));
        if (timingOptions is null)
            throw new ArgumentNullException(nameof(timingOptions));

        return ReplayCoreAsync(records, connection.SendAsync, isAllowed, timingOptions, cancellationToken);
    }

    /// <summary>
    /// Replays allowed sent records through a caller-supplied normal send API.
    /// This overload can target a role endpoint or a test boundary.
    /// </summary>
    public Task<IReadOnlyList<SecsTraceReplayResult>> ReplayAsync(
        IEnumerable<SecsTraceRecord> records,
        Func<SecsMessage, CancellationToken, Task<HsmsDataMessage?>> sendAsync,
        Func<SecsTraceRecord, bool> isAllowed,
        CancellationToken cancellationToken = default)
        => ReplayCoreAsync(records, sendAsync, isAllowed, timingOptions: null, cancellationToken);

    /// <summary>
    /// Replays allowed sent records through a caller-supplied normal send API
    /// and waits scaled source intervals between completed sends.
    /// </summary>
    public Task<IReadOnlyList<SecsTraceReplayResult>> ReplayWithTimingAsync(
        IEnumerable<SecsTraceRecord> records,
        Func<SecsMessage, CancellationToken, Task<HsmsDataMessage?>> sendAsync,
        Func<SecsTraceRecord, bool> isAllowed,
        SecsTraceReplayTimingOptions timingOptions,
        CancellationToken cancellationToken = default)
    {
        if (timingOptions is null)
            throw new ArgumentNullException(nameof(timingOptions));

        return ReplayCoreAsync(records, sendAsync, isAllowed, timingOptions, cancellationToken);
    }

    private async Task<IReadOnlyList<SecsTraceReplayResult>> ReplayCoreAsync(
        IEnumerable<SecsTraceRecord> records,
        Func<SecsMessage, CancellationToken, Task<HsmsDataMessage?>> sendAsync,
        Func<SecsTraceRecord, bool> isAllowed,
        SecsTraceReplayTimingOptions? timingOptions,
        CancellationToken cancellationToken)
    {
        if (records is null)
            throw new ArgumentNullException(nameof(records));
        if (sendAsync is null)
            throw new ArgumentNullException(nameof(sendAsync));
        if (isAllowed is null)
            throw new ArgumentNullException(nameof(isAllowed));

        var selected = new List<SecsTraceRecord>();
        var sourceCount = 0;
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record is null)
                throw new ArgumentException("The replay sequence contains a null record.", nameof(records));
            if (++sourceCount > MaxRecordCount)
                throw new InvalidOperationException($"The replay source record count exceeds the configured maximum {MaxRecordCount}.");
            if (record.Direction == SecsTraceDirection.Sent && isAllowed(record))
                selected.Add(record);
        }

        var delays = timingOptions is null
            ? null
            : CalculateDelays(selected, timingOptions);
        var results = new List<SecsTraceReplayResult>(selected.Count);
        for (var index = 0; index < selected.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delays is not null && index != 0 && delays[index - 1] > TimeSpan.Zero)
            {
                var delay = _delayAsync(delays[index - 1], cancellationToken);
                if (delay is null)
                    throw new InvalidOperationException("The replay delay delegate returned a null task.");
                await delay.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var record = selected[index];
            var operation = sendAsync(record.Message, cancellationToken);
            if (operation is null)
                throw new InvalidOperationException("The replay send delegate returned a null task.");
            var secondary = await operation.ConfigureAwait(false);
            results.Add(new SecsTraceReplayResult(record, secondary));
        }

        return new ReadOnlyCollection<SecsTraceReplayResult>(results);
    }

    private static TimeSpan[] CalculateDelays(
        IReadOnlyList<SecsTraceRecord> records,
        SecsTraceReplayTimingOptions timingOptions)
    {
        var delays = new TimeSpan[Math.Max(0, records.Count - 1)];
        for (var index = 1; index < records.Count; index++)
        {
            var sourceInterval = records[index].Timestamp - records[index - 1].Timestamp;
            if (sourceInterval < TimeSpan.Zero)
                throw new InvalidOperationException("Allowed sent trace records must use nondecreasing timestamps for timed replay.");
            delays[index - 1] = timingOptions.Scale(sourceInterval);
        }
        return delays;
    }
}
