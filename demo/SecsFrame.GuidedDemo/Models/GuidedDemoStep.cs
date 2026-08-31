namespace SecsFrame.GuidedDemo.Models;

internal sealed record GuidedDemoStep(
    int Number,
    string Name,
    string Focus)
{
    public static IReadOnlyList<GuidedDemoStep> All { get; } =
        new[]
        {
            new GuidedDemoStep(
                1,
                "建立会话",
                "真实 TCP 与 Selected"),
            new GuidedDemoStep(
                2,
                "构造消息",
                "动态 Item 与 SML"),
            new GuidedDemoStep(
                3,
                "完成事务",
                "Primary 与 Secondary"),
            new GuidedDemoStep(
                4,
                "检查链路",
                "Linktest 控制事务"),
            new GuidedDemoStep(
                5,
                "恢复会话",
                "Separate 与新代次"),
            new GuidedDemoStep(
                6,
                "建立 GEM 通讯",
                "公共服务与双方身份"),
            new GuidedDemoStep(
                7,
                "读取动态变量",
                "运行期 SVID 与实际值"),
            new GuidedDemoStep(
                8,
                "导出脱敏 Trace",
                "结构化规则与严格 codec"),
            new GuidedDemoStep(
                9,
                "捕获 T3 诊断",
                "真实超时与受限字段"),
        };
}
