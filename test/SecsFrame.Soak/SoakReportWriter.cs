using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecsFrame.Soak;

internal sealed class SoakReportWriter : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly StreamWriter _writer;

    private SoakReportWriter(string outputPath, StreamWriter writer)
    {
        OutputPath = outputPath;
        _writer = writer;
    }

    public string OutputPath { get; }

    public static SoakReportWriter Create(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(directory))
            throw new SoakConfigurationException("The report path requires a directory.");

        Directory.CreateDirectory(directory);
        var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        return new SoakReportWriter(outputPath, new StreamWriter(stream));
    }

    public async ValueTask WriteAsync<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        await _writer.WriteLineAsync(json).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    internal sealed record RunStarted(
        string Event,
        DateTimeOffset TimestampUtc,
        int Seed,
        int DurationSeconds,
        int MaxCycles,
        string GitCommit,
        string Framework,
        string OperatingSystem);

    internal sealed record CycleStarted(
        string Event,
        DateTimeOffset TimestampUtc,
        int Cycle,
        SoakFaultMode FaultMode);

    internal sealed record CycleCompleted(
        string Event,
        DateTimeOffset TimestampUtc,
        int Cycle,
        SoakFaultMode FaultMode,
        uint InterruptedSystemBytes,
        string InterruptionException,
        uint RecoveredSystemBytes,
        long ElapsedMilliseconds);

    internal sealed record RunFailed(
        string Event,
        DateTimeOffset TimestampUtc,
        int Seed,
        int Cycle,
        SoakFaultMode? FaultMode,
        string Exception);

    internal sealed record RunCompleted(
        string Event,
        DateTimeOffset TimestampUtc,
        int Seed,
        int CompletedCycles,
        string CompletionReason,
        long ElapsedMilliseconds,
        IReadOnlyDictionary<SoakFaultMode, int> FaultCounts);
}
