namespace SecsFrame.DemoLauncher;

internal sealed record DemoProcessSpec(
    string Name,
    string HealthName,
    Uri Uri,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments)
{
    public static IReadOnlyList<DemoProcessSpec> Resolve(
        LauncherOptions options,
        string launcherBaseDirectory,
        string currentDirectory)
    {
        var published = ResolvePublished(
            options,
            launcherBaseDirectory);
        if (published is not null)
            return published;

        var repositoryRoot = FindRepositoryRoot(currentDirectory) ??
            FindRepositoryRoot(launcherBaseDirectory) ??
            throw new InvalidOperationException(
                "找不到发布包内容或 SecsFrame.slnx，无法定位 Demo。");

        return
        [
            CreateSource(
                "通讯测试工具",
                "SecsFrame.CommunicationDemo",
                options.CommunicationUri,
                repositoryRoot,
                Path.Combine(
                    repositoryRoot,
                    "demo",
                    "SecsFrame.CommunicationDemo",
                    "SecsFrame.CommunicationDemo.csproj")),
            CreateSource(
                "分步成果演示",
                "SecsFrame.GuidedDemo",
                options.GuidedUri,
                repositoryRoot,
                Path.Combine(
                    repositoryRoot,
                    "demo",
                    "SecsFrame.GuidedDemo",
                    "SecsFrame.GuidedDemo.csproj")),
        ];
    }

    private static IReadOnlyList<DemoProcessSpec>? ResolvePublished(
        LauncherOptions options,
        string launcherBaseDirectory)
    {
        var packageRoot = Directory.GetParent(
            Path.TrimEndingDirectorySeparator(launcherBaseDirectory));
        if (packageRoot is null)
            return null;

        var communicationAssembly = Path.Combine(
            packageRoot.FullName,
            "communication",
            "SecsFrame.CommunicationDemo.dll");
        var guidedAssembly = Path.Combine(
            packageRoot.FullName,
            "guided",
            "SecsFrame.GuidedDemo.dll");
        if (!File.Exists(communicationAssembly) ||
            !File.Exists(guidedAssembly))
        {
            return null;
        }

        return
        [
            CreatePublished(
                "通讯测试工具",
                "SecsFrame.CommunicationDemo",
                options.CommunicationUri,
                communicationAssembly),
            CreatePublished(
                "分步成果演示",
                "SecsFrame.GuidedDemo",
                options.GuidedUri,
                guidedAssembly),
        ];
    }

    private static DemoProcessSpec CreatePublished(
        string name,
        string healthName,
        Uri uri,
        string assemblyPath)
        => new(
            name,
            healthName,
            uri,
            Path.GetDirectoryName(assemblyPath) ??
                throw new InvalidOperationException(
                    $"无法确定 {assemblyPath} 的发布目录。"),
            [assemblyPath, "--urls", uri.AbsoluteUri]);

    private static DemoProcessSpec CreateSource(
        string name,
        string healthName,
        Uri uri,
        string repositoryRoot,
        string projectPath)
        => new(
            name,
            healthName,
            uri,
            repositoryRoot,
            [
                "run",
                "--project",
                projectPath,
                "--no-launch-profile",
                "--",
                "--urls",
                uri.AbsoluteUri,
            ]);

    private static string? FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SecsFrame.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }
}
