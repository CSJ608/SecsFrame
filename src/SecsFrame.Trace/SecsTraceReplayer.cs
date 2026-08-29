using System.Collections.ObjectModel;

namespace SecsFrame.Trace;

/// <summary>Replays explicitly allowed sent records through a normal send API.</summary>
public sealed class SecsTraceReplayer
{
    /// <summary>The default maximum number of source records in one replay request.</summary>
    public const int DefaultMaxRecordCount = SecsTraceCodec.DefaultMaxRecordCount;

    /// <summary>Creates a controlled replayer with an explicit source-record limit.</summary>
    public SecsTraceReplayer(int maxRecordCount = DefaultMaxRecordCount)
    {
        if (maxRecordCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRecordCount), maxRecordCount, "The maximum record count must be positive.");
        MaxRecordCount = maxRecordCount;
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

        return ReplayAsync(records, connection.SendAsync, isAllowed, cancellationToken);
    }

    /// <summary>
    /// Replays allowed sent records through a caller-supplied normal send API.
    /// This overload can target a role endpoint or a test boundary.
    /// </summary>
    public async Task<IReadOnlyList<SecsTraceReplayResult>> ReplayAsync(
        IEnumerable<SecsTraceRecord> records,
        Func<SecsMessage, CancellationToken, Task<HsmsDataMessage?>> sendAsync,
        Func<SecsTraceRecord, bool> isAllowed,
        CancellationToken cancellationToken = default)
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

        var results = new List<SecsTraceReplayResult>(selected.Count);
        foreach (var record in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = sendAsync(record.Message, cancellationToken);
            if (operation is null)
                throw new InvalidOperationException("The replay send delegate returned a null task.");
            var secondary = await operation.ConfigureAwait(false);
            results.Add(new SecsTraceReplayResult(record, secondary));
        }

        return new ReadOnlyCollection<SecsTraceReplayResult>(results);
    }
}
