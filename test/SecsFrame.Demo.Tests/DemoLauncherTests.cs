using SecsFrame.DemoLauncher;

namespace SecsFrame.Demo.Tests;

public sealed class DemoLauncherTests
{
    [Fact]
    public void Launcher_options_default_to_distinct_loopback_endpoints()
    {
        var options = LauncherOptions.Parse([]);

        Assert.Equal(
            "http://127.0.0.1:5080/",
            options.CommunicationUri.AbsoluteUri);
        Assert.Equal(
            "http://127.0.0.1:5081/",
            options.GuidedUri.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(30), options.StartupTimeout);
        Assert.False(options.NoOpen);
        Assert.False(options.VerifyStartup);
    }

    [Fact]
    public void Startup_verification_disables_browser_opening()
    {
        var options = LauncherOptions.Parse(
        [
            "--verify-startup",
            "--startup-timeout-seconds",
            "45",
        ]);

        Assert.True(options.NoOpen);
        Assert.True(options.VerifyStartup);
        Assert.Equal(TimeSpan.FromSeconds(45), options.StartupTimeout);
    }

    [Theory]
    [InlineData("--communication-url", "http://0.0.0.0:5080")]
    [InlineData("--communication-url", "https://127.0.0.1:5080")]
    [InlineData("--guided-url", "http://example.com:5081")]
    [InlineData("--guided-url", "http://127.0.0.1:5081/demo")]
    public void Launcher_rejects_non_loopback_or_non_http_bindings(
        string option,
        string value)
    {
        Assert.Throws<LauncherOptionsException>(
            () => LauncherOptions.Parse([option, value]));
    }

    [Fact]
    public void Launcher_rejects_duplicate_endpoints()
    {
        Assert.Throws<LauncherOptionsException>(
            () => LauncherOptions.Parse(
            [
                "--communication-url",
                "http://localhost:5090",
                "--guided-url",
                "http://localhost:5090",
            ]));
    }

    [Fact]
    public void Published_demos_use_their_own_content_root()
    {
        var packageRoot = Path.Combine(
            Path.GetTempPath(),
            $"secsframe-demo-package-{Guid.NewGuid():N}");
        var launcherDirectory = Path.Combine(packageRoot, "launcher");
        var communicationDirectory = Path.Combine(
            packageRoot,
            "communication");
        var guidedDirectory = Path.Combine(packageRoot, "guided");
        Directory.CreateDirectory(launcherDirectory);
        Directory.CreateDirectory(communicationDirectory);
        Directory.CreateDirectory(guidedDirectory);
        File.WriteAllText(
            Path.Combine(
                communicationDirectory,
                "SecsFrame.CommunicationDemo.dll"),
            string.Empty);
        File.WriteAllText(
            Path.Combine(guidedDirectory, "SecsFrame.GuidedDemo.dll"),
            string.Empty);

        try
        {
            var specs = DemoProcessSpec.Resolve(
                LauncherOptions.Parse([]),
                launcherDirectory,
                packageRoot);

            Assert.Equal(
                communicationDirectory,
                specs[0].WorkingDirectory);
            Assert.Equal(guidedDirectory, specs[1].WorkingDirectory);
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
        }
    }
}
