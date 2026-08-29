# SecsFrame

基于 [StreamFrame](https://github.com/CSJ608/StreamFrame) 的 .NET SECS-II / HSMS-SS / GEM 通讯组件。

> 当前处于早期开发阶段，尚未声明通过 SEMI 一致性认证，也不建议用于生产设备。

## 设计目标

- 基础层动态表达任意 SECS-II 消息，不要求预先声明所有 SxFy 模板。
- HSMS 会话、SECS-II 事务和 GEM 能力分层实现。
- 同时支持 Host 与 Equipment 角色，不把连接主动/被动模式与业务角色绑定。
- 标准严格模式为默认行为；现场设备兼容选项显式启用并有回归测试。
- 支持 SML 调试格式，并逐步支持 SEMI E173 SMN 与 E172 SEDD。

## 包边界

| 包 | 职责 |
|---|---|
| `SecsFrame` | SECS-II 数据模型、HSMS-SS 会话与事务基础 |
| `SecsFrame.Gem` | GEM 通用状态模型与能力服务 |
| `SecsFrame.Smn` | E173 SMN 日志、文档与消息表示 |
| `SecsFrame.Sedd` | E172 SEDD 设备接口数据字典 |

当前代码覆盖第一阶段的 HSMS 线上帧基础、完整的 SECS-II Item
动态数据模型，以及动态消息到 HSMS Data Message Payload 的严格二进制
编解码。内部 StreamFrame 适配器还提供会话绑定、整帧实际写出确认和
显式 T5 重连节流、按字节进展重置且跨会话隔离的 T8。Active/Passive
共用的内部 HSMS-SS 状态机已经覆盖 TCP 会话、Select、Linktest、Reject、
Deselect、Separate、T6/T7 和 Selected 数据门控。内部数据事务 actor
进一步提供实际写出后启动的 T3、复合事务键关联、入站回复和会话失效
隔离。公共 <code>HsmsConnection</code> 将这些内部层组合成显式计时
配置、动态消息收发、状态等待和单消费者事件流。
独立 <code>SecsFrame.Gem</code> 已提供可配置 GEM 通用行为，包含双向通讯
建立及应用接受策略、同会话显式重试恢复、上下线与应用转换策略、动态变量/
常量、应用托管时钟、报告定义、事件链接和带 Equipment 应用发送策略的
Collection Event、报警目录查询、单报警发送启停，以及最小报警通知、
远程命令链路和应用状态接受策略。
Item 用法与边界见
[docs/SECS-II-ITEMS.md](docs/SECS-II-ITEMS.md)，消息集成见
[docs/SECS-MESSAGES.md](docs/SECS-MESSAGES.md)，传输适配边界见
[docs/STREAMFRAME-ADAPTER.md](docs/STREAMFRAME-ADAPTER.md)，状态机边界见
[docs/HSMS-SESSION-STATE-MACHINE.md](docs/HSMS-SESSION-STATE-MACHINE.md)，
数据事务边界见
[docs/HSMS-DATA-TRANSACTIONS.md](docs/HSMS-DATA-TRANSACTIONS.md)，
公共连接用法见
[docs/HSMS-CONNECTION.md](docs/HSMS-CONNECTION.md)，
结构化故障分类见
[docs/HSMS-DIAGNOSTICS.md](docs/HSMS-DIAGNOSTICS.md)，
运行期消息处理见
[docs/HSMS-PRIMARY-ROUTING.md](docs/HSMS-PRIMARY-ROUTING.md)，
Host/Equipment 端点见
[docs/SECS-ENDPOINT-ROLES.md](docs/SECS-ENDPOINT-ROLES.md)，
独立 GEM 基础行为见
[docs/GEM-FOUNDATION.md](docs/GEM-FOUNDATION.md)，
官方 secs4net 跨实现证据见
[docs/SECS4NET-INTEROP.md](docs/SECS4NET-INTEROP.md)，
整体路线图见
[docs/ROADMAP.md](docs/ROADMAP.md)。

## 动态 Item

无需预先声明消息模板即可组合任意 Item 树：

~~~csharp
var body = SecsItem.List(
    SecsItem.Ascii("LOT-001"),
    SecsItem.U4(1001),
    SecsItem.List(
        SecsItem.Boolean(true),
        SecsItem.F8(23.5)));

var message = new SecsMessage(
    stream: 6,
    function: 11,
    replyExpected: true,
    rootItem: body);

var hsmsMessage = new HsmsDataMessage(
    sessionId: 1,
    systemBytes: 0x01020304,
    message);

var codec = new HsmsDataMessageCodec();
~~~

<code>SecsItemCodec</code> 默认严格拒绝非法格式、截断、数值宽度错位、
非 ASCII 字节、尾随数据和超过资源上限的树。JIS-8 在核心层以原始编码
字节表示，不猜测现场代码页。

<code>HsmsDataMessageCodec</code> 编解码四字节长度前缀之后的完整 HSMS
数据 Payload。没有 Body 使用 <code>null</code>，空 List 使用
<code>SecsItem.List()</code>，两者不会混淆。长度前缀继续由
<code>HsmsFramer</code> 负责。

## 构建

```bash
dotnet build SecsFrame.slnx -c Release
dotnet test SecsFrame.slnx -c Release --no-build
```

## 许可证

[MIT](LICENSE)

实现所跟踪的 SEMI 版本与版权边界见 [docs/STANDARDS.md](docs/STANDARDS.md)。
