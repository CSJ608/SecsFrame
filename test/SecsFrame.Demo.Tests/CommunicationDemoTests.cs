using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SecsFrame.CommunicationDemo.Models;
using SecsFrame.CommunicationDemo.Services;

namespace SecsFrame.Demo.Tests;

public sealed class CommunicationDemoTests
{
    [Fact]
    public void Activity_export_requires_explicit_content_classification()
    {
        var entries = new[]
        {
            new ActivityEntry(
                1,
                new DateTimeOffset(
                    2026,
                    8,
                    31,
                    8,
                    0,
                    0,
                    TimeSpan.Zero),
                ActivityEntryTone.Success,
                "接收",
                "收到 S6F11 W",
                "System Bytes 0x01020304",
                "'S6F11'W\n<A [10] 'LOT-SECRET'>\n.\n",
                ActivityDetailKind.ProtocolMessage),
        };

        var metadata = ActivityExportBuilder.Build(
            entries,
            ActivityExportClassification.MetadataOnly);
        var redacted = ActivityExportBuilder.Build(
            entries,
            ActivityExportClassification.RedactedContent);

        using var metadataJson = JsonDocument.Parse(metadata);
        using var redactedJson = JsonDocument.Parse(redacted);
        Assert.Equal(
            "MetadataOnly",
            metadataJson.RootElement
                .GetProperty("Classification")
                .GetString());
        Assert.DoesNotContain("0x01020304", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("LOT-SECRET", metadata, StringComparison.Ordinal);
        Assert.Equal(
            "RedactedContent",
            redactedJson.RootElement
                .GetProperty("Classification")
                .GetString());
        Assert.Contains("REDACTED", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("LOT-SECRET", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_favorites_are_normalized_and_session_scoped()
    {
        var workspace = new CommunicationWorkspace();

        var favorite = workspace.AddFavorite(
            "状态查询",
            "'S1F1'W\r\n.\r\n");

        Assert.Single(workspace.Favorites);
        Assert.Equal("'S1F1'W\n.\n", favorite.Sml);
        Assert.Throws<InvalidOperationException>(
            () => workspace.AddFavorite("状态查询", favorite.Sml));

        workspace.RemoveFavorite(favorite.Id);

        Assert.Empty(workspace.Favorites);
    }

    [Fact]
    public async Task Loopback_primary_can_be_replied_to_from_the_workspace()
    {
        await using var workspace = new CommunicationWorkspace();
        var draft = new ConnectionDraft
        {
            Port = GetFreePort(),
            T3Seconds = 5,
            T5Seconds = 1,
            T6Seconds = 3,
            T7Seconds = 5,
            T8Seconds = 3,
            UseLoopbackPeer = true,
        };
        await workspace.ConnectAsync(draft);

        var pending = WaitUntilAsync(
            workspace,
            () => workspace.PendingReplies.Count == 1);
        workspace.QueueLoopbackPrimary();
        await pending;

        var reply = Assert.Single(workspace.PendingReplies);
        var completed = WaitUntilAsync(
            workspace,
            () => workspace.Activities.Any(
                item => string.Equals(
                    item.Title,
                    "收到通讯工具回复",
                    StringComparison.Ordinal)));
        await workspace.ReplyAsync(reply.Id, reply.SuggestedSecondarySml);
        await completed;

        Assert.Empty(workspace.PendingReplies);
        Assert.False(workspace.IsLoopbackIncomingPending);
        Assert.Contains(
            workspace.Activities,
            item => string.Equals(
                    item.Category,
                    "回复",
                    StringComparison.Ordinal) &&
                string.Equals(
                    item.Title,
                    "Secondary 写出完成",
                    StringComparison.Ordinal));
    }

    private static async Task WaitUntilAsync(
        CommunicationWorkspace workspace,
        Func<bool> predicate)
    {
        if (predicate())
            return;

        var signal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void HandleChanged(object? sender, EventArgs args)
        {
            if (predicate())
                signal.TrySetResult();
        }

        workspace.Changed += HandleChanged;
        try
        {
            if (predicate())
                signal.TrySetResult();
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
        }
        finally
        {
            workspace.Changed -= HandleChanged;
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
