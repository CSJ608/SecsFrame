using System.Diagnostics;
using System.Text.Json;

namespace SecsFrame.DemoLauncher;

internal static class DemoLauncherApplication
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        LauncherOptions options;
        try
        {
            options = LauncherOptions.Parse(args);
        }
        catch (LauncherOptionsException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(LauncherOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(LauncherOptions.Usage);
            return 0;
        }

        IReadOnlyList<DemoProcessSpec> specs;
        try
        {
            specs = DemoProcessSpec.Resolve(
                options,
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory());
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }

        return await RunChildrenAsync(options, specs, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> RunChildrenAsync(
        LauncherOptions options,
        IReadOnlyList<DemoProcessSpec> specs,
        CancellationToken cancellationToken)
    {
        using var shutdown =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        var children = new List<DemoChildProcess>(specs.Count);
        try
        {
            foreach (var spec in specs)
                children.Add(DemoChildProcess.Start(spec));
            return await RunActiveChildrenAsync(
                    options,
                    children,
                    shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Demo 启动失败: {error.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            for (var index = children.Count - 1; index >= 0; index--)
                await children[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> RunActiveChildrenAsync(
        LauncherOptions options,
        IReadOnlyList<DemoChildProcess> children,
        CancellationToken cancellationToken)
    {
        await WaitUntilReadyAsync(
                children,
                options.StartupTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("SecsFrame Demo 已就绪:");
        foreach (var child in children)
            Console.WriteLine($"  {child.Spec.Name}: {child.Spec.Uri}");

        if (options.VerifyStartup)
        {
            Console.WriteLine("启动验证通过。");
            return 0;
        }

        if (!options.NoOpen)
        {
            foreach (var child in children)
                TryOpenBrowser(child.Spec.Uri);
        }

        Console.WriteLine();
        Console.WriteLine("按 Ctrl+C 停止两个 Demo。");
        return await WaitForShutdownAsync(children, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WaitUntilReadyAsync(
        IReadOnlyList<DemoChildProcess> children,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2),
        };

        var pending = new HashSet<DemoChildProcess>(children);
        while (pending.Count > 0)
        {
            foreach (var child in pending.ToArray())
            {
                if (child.HasExited)
                {
                    throw new InvalidOperationException(
                        $"{child.Spec.Name}在就绪前退出，退出码 {child.ExitCode}。");
                }

                if (await IsReadyAsync(
                            client,
                            child.Spec,
                            timeoutSource.Token)
                        .ConfigureAwait(false))
                {
                    pending.Remove(child);
                }
            }

            if (pending.Count > 0)
            {
                try
                {
                    await Task.Delay(150, timeoutSource.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Demo 未能在 {timeout.TotalSeconds:0} 秒内就绪。");
                }
            }
        }
    }

    private static async Task<bool> IsReadyAsync(
        HttpClient client,
        DemoProcessSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            var healthUri = new Uri(spec.Uri, "healthz");
            using var response = await client
                .GetAsync(healthUri, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            using var content = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                    content,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return document.RootElement.TryGetProperty("name", out var name) &&
                string.Equals(
                    name.GetString(),
                    spec.HealthName,
                    StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<int> WaitForShutdownAsync(
        IReadOnlyList<DemoChildProcess> children,
        CancellationToken cancellationToken)
    {
        var shutdownTask = Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken);
        var exitTasks = children
            .Select(child => child.WaitForExitAsync())
            .ToArray();
        var completed = await Task.WhenAny(exitTasks.Append(shutdownTask))
            .ConfigureAwait(false);
        if (completed == shutdownTask)
            return 0;

        var exited = children.First(child => child.HasExited);
        Console.Error.WriteLine(
            $"{exited.Spec.Name}已退出，退出码 {exited.ExitCode}；" +
            "启动器将停止另一 Demo。");
        return exited.ExitCode == 0 ? 1 : exited.ExitCode;
    }

    private static void TryOpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"无法自动打开 {uri}：{error.Message}。请手动访问该地址。");
        }
    }
}
