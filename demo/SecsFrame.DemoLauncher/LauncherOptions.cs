using System.Globalization;

namespace SecsFrame.DemoLauncher;

internal sealed record LauncherOptions(
    Uri CommunicationUri,
    Uri GuidedUri,
    TimeSpan StartupTimeout,
    bool NoOpen,
    bool VerifyStartup,
    bool ShowHelp)
{
    public const string Usage = """
        SecsFrame Demo 启动器

        用法:
          SecsFrame.DemoLauncher [选项]

        选项:
          --communication-url <url>       通讯工具地址，默认 http://127.0.0.1:5080
          --guided-url <url>              分步演示地址，默认 http://127.0.0.1:5081
          --startup-timeout-seconds <n>   启动超时，默认 30 秒，范围 1-300
          --no-open                       就绪后不打开浏览器
          --verify-startup                两个 Demo 就绪后立即退出，用于冒烟验证
          --help                          显示帮助

        地址仅允许使用本机回环 HTTP 端点。
        """;

    public static LauncherOptions Parse(IReadOnlyList<string> args)
    {
        var communicationUri = new Uri("http://127.0.0.1:5080");
        var guidedUri = new Uri("http://127.0.0.1:5081");
        var startupTimeout = TimeSpan.FromSeconds(30);
        var noOpen = false;
        var verifyStartup = false;
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--communication-url":
                    communicationUri = ParseLoopbackUri(
                        ReadValue(args, ref index, argument),
                        argument);
                    break;
                case "--guided-url":
                    guidedUri = ParseLoopbackUri(
                        ReadValue(args, ref index, argument),
                        argument);
                    break;
                case "--startup-timeout-seconds":
                    startupTimeout = ParseTimeout(
                        ReadValue(args, ref index, argument));
                    break;
                case "--no-open":
                    noOpen = true;
                    break;
                case "--verify-startup":
                    verifyStartup = true;
                    noOpen = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                default:
                    throw new LauncherOptionsException(
                        $"未知选项: {argument}");
            }
        }

        if (communicationUri == guidedUri)
        {
            throw new LauncherOptionsException(
                "通讯工具与分步演示不能使用同一地址。");
        }

        return new LauncherOptions(
            communicationUri,
            guidedUri,
            startupTimeout,
            noOpen,
            verifyStartup,
            showHelp);
    }

    private static string ReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        index++;
        if (index >= args.Count ||
            args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new LauncherOptionsException($"选项 {option} 缺少值。");
        }
        return args[index];
    }

    private static Uri ParseLoopbackUri(string value, string option)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.Ordinal) ||
            !uri.IsLoopback ||
            !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new LauncherOptionsException(
                $"选项 {option} 必须是无路径的本机回环 HTTP 地址。");
        }

        return uri;
    }

    private static TimeSpan ParseTimeout(string value)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds) ||
            seconds is < 1 or > 300)
        {
            throw new LauncherOptionsException(
                "启动超时必须是 1 到 300 之间的整数秒数。");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
