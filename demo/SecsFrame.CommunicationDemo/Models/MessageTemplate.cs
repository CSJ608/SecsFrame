namespace SecsFrame.CommunicationDemo.Models;

internal sealed record MessageTemplate(
    string Id,
    string Name,
    string Sml)
{
    public static IReadOnlyList<MessageTemplate> All { get; } =
        new[]
        {
            new MessageTemplate(
                "empty-w",
                "空 Body / 等待回复",
                "'S1F1'W\n.\n"),
            new MessageTemplate(
                "nested-w",
                "嵌套 Item / 等待回复",
                "'S6F11'W\n" +
                "<L [2]\n" +
                "    <A [8] 'DEMO-001'>\n" +
                "    <L [2]\n" +
                "        <U4 [1] 1001>\n" +
                "        <Boolean [1] True>\n" +
                "    >\n" +
                ">\n" +
                ".\n"),
            new MessageTemplate(
                "fire-forget",
                "无回复消息",
                "'S2F17'\n" +
                "<A [12] 'LOCAL-SAMPLE'>\n" +
                ".\n"),
        };
}
