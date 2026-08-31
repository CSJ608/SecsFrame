using System.Diagnostics;

namespace SecsFrame.DemoLauncher;

internal sealed class DemoChildProcess : IAsyncDisposable
{
    private readonly Process _process;

    private DemoChildProcess(DemoProcessSpec spec, Process process)
    {
        Spec = spec;
        _process = process;
    }

    public DemoProcessSpec Spec { get; }

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public static DemoChildProcess Start(DemoProcessSpec spec)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
                "dotnet",
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in spec.Arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, args) =>
            WriteLine(spec.Name, args.Data);
        process.ErrorDataReceived += (_, args) =>
            WriteLine(spec.Name, args.Data);

        if (!process.Start())
            throw new InvalidOperationException($"无法启动{spec.Name}。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new DemoChildProcess(spec, process);
    }

    public Task WaitForExitAsync(
        CancellationToken cancellationToken = default)
        => _process.WaitForExitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (_process.HasExited)
        {
            // The child exited between the state check and Kill.
        }
        await _process.WaitForExitAsync().ConfigureAwait(false);
        _process.Dispose();
    }

    private static void WriteLine(string name, string? line)
    {
        if (!string.IsNullOrEmpty(line))
            Console.WriteLine($"[{name}] {line}");
    }
}
