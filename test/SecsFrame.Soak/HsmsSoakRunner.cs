using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SecsFrame.Soak;

internal sealed class HsmsSoakRunner
{
    private static readonly SoakFaultMode[] FaultModes =
        Enum.GetValues<SoakFaultMode>();
    private static readonly TimeSpan CycleTimeout = TimeSpan.FromSeconds(20);
    private readonly SoakOptions _options;
    private readonly SoakReportWriter _report;
    private readonly Random _random;
    private readonly SoakFaultMode[] _faultOrder =
        Enum.GetValues<SoakFaultMode>();
    private readonly Dictionary<SoakFaultMode, int> _faultCounts =
        FaultModes.ToDictionary(static item => item, static _ => 0);
    private int _faultIndex = FaultModes.Length;

    public HsmsSoakRunner(SoakOptions options, SoakReportWriter report)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _random = new Random(options.Seed);
    }

    public async Task RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var progress = new RunProgress();
        await WriteStartedAsync().ConfigureAwait(false);
        Console.WriteLine(
            $"SecsFrame session soak seed={_options.Seed} " +
            $"duration={_options.Duration} report={_report.OutputPath}");

        using var lifetime = new CancellationTokenSource(_options.Duration);
        try
        {
            await ExecuteCyclesAsync(stopwatch, progress, lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await WriteFailureAsync(progress, ex).ConfigureAwait(false);
            throw;
        }

        if (progress.CompletedCycles == 0)
        {
            var error = new InvalidOperationException(
                "The soak duration elapsed before any fault cycle completed.");
            await WriteFailureAsync(progress, error).ConfigureAwait(false);
            throw error;
        }

        await WriteCompletedAsync(progress, stopwatch).ConfigureAwait(false);
    }

    private async Task ExecuteCyclesAsync(
        Stopwatch stopwatch,
        RunProgress progress,
        CancellationToken cancellationToken)
    {
        var pair = await HsmsConnectionPair
            .CreateAsync(cancellationToken).ConfigureAwait(false);
        await using var pairScope = pair.ConfigureAwait(false);
        for (; progress.CompletedCycles < _options.MaxCycles;
            progress.CompletedCycles++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.CurrentCycle = progress.CompletedCycles;
            progress.CurrentFault = NextFaultMode();
            await RunCycleAsync(
                pair,
                progress.CurrentCycle,
                progress.CurrentFault.Value,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask WriteFailureAsync(RunProgress progress, Exception error)
        => _report.WriteAsync(new SoakReportWriter.RunFailed(
            "runFailed",
            DateTimeOffset.UtcNow,
            _options.Seed,
            progress.CurrentCycle,
            progress.CurrentFault,
            error.ToString()));

    private async Task WriteCompletedAsync(
        RunProgress progress,
        Stopwatch stopwatch)
    {
        var reason = progress.CompletedCycles >= _options.MaxCycles
            ? "maxCyclesReached"
            : "durationElapsed";
        await _report.WriteAsync(new SoakReportWriter.RunCompleted(
            "runCompleted",
            DateTimeOffset.UtcNow,
            _options.Seed,
            progress.CompletedCycles,
            reason,
            stopwatch.ElapsedMilliseconds,
            _faultCounts)).ConfigureAwait(false);
        Console.WriteLine(
            $"Completed {progress.CompletedCycles} cycles in {stopwatch.Elapsed}; reason={reason}.");
    }

    private async Task RunCycleAsync(
        HsmsConnectionPair pair,
        int cycle,
        SoakFaultMode faultMode,
        Stopwatch runStopwatch,
        CancellationToken lifetimeToken)
    {
        await _report.WriteAsync(new SoakReportWriter.CycleStarted(
            "cycleStarted",
            DateTimeOffset.UtcNow,
            cycle,
            faultMode)).ConfigureAwait(false);
        using var cycleCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(lifetimeToken);
        cycleCancellation.CancelAfter(CycleTimeout);
        var startedAt = runStopwatch.ElapsedMilliseconds;
        var result = await pair.RunCycleAsync(
            cycle,
            faultMode,
            cycleCancellation.Token).ConfigureAwait(false);
        _faultCounts[faultMode]++;
        await _report.WriteAsync(new SoakReportWriter.CycleCompleted(
            "cycleCompleted",
            DateTimeOffset.UtcNow,
            cycle,
            faultMode,
            result.InterruptedSystemBytes,
            result.InterruptionException,
            result.RecoveredSystemBytes,
            runStopwatch.ElapsedMilliseconds - startedAt)).ConfigureAwait(false);

        if (cycle % 25 == 0)
            Console.WriteLine($"cycle={cycle} fault={faultMode}");
    }

    private ValueTask WriteStartedAsync()
        => _report.WriteAsync(new SoakReportWriter.RunStarted(
            "runStarted",
            DateTimeOffset.UtcNow,
            _options.Seed,
            checked((int)_options.Duration.TotalSeconds),
            _options.MaxCycles,
            Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription));

    private SoakFaultMode NextFaultMode()
    {
        if (_faultIndex >= _faultOrder.Length)
        {
            for (var index = _faultOrder.Length - 1; index > 0; index--)
            {
                var target = _random.Next(index + 1);
                (_faultOrder[index], _faultOrder[target]) =
                    (_faultOrder[target], _faultOrder[index]);
            }
            _faultIndex = 0;
        }

        return _faultOrder[_faultIndex++];
    }

    private sealed class RunProgress
    {
        public int CompletedCycles { get; set; }

        public int CurrentCycle { get; set; } = -1;

        public SoakFaultMode? CurrentFault { get; set; }
    }
}
