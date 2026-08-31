using SecsFrame.GuidedDemo.Services;

namespace SecsFrame.Demo.Tests;

public sealed class GuidedDemoTests
{
    [Fact]
    public async Task Nine_step_demo_executes_real_gem_trace_and_diagnostic_actions()
    {
        await using var session = new GuidedDemoSession();

        await session.StartAsync();
        Assert.Null(session.Error);
        for (var index = 1; index < session.Steps.Count; index++)
        {
            await session.NextAsync();
            Assert.Null(session.Error);
        }

        Assert.True(session.IsComplete);
        Assert.Equal(9, session.Results.Count);
        Assert.Equal("GEM 通讯已建立", session.Results[5].Title);
        Assert.Equal("动态变量已读取", session.Results[6].Title);
        Assert.Contains(
            "SecsFrame-Trace/1",
            session.Results[7].Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DEMO-LOT-01",
            session.Results[7].Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "SecsFrame-DiagnosticTrace/1",
            session.Results[8].Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "T3Timeout",
            session.Results[8].Code,
            StringComparison.Ordinal);
    }
}
