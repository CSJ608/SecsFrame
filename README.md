# SecsFrame

基于 [StreamFrame](https://github.com/CSJ608/StreamFrame) 的 .NET SECS-II / HSMS-SS / GEM 通讯组件。

> 当前处于早期开发阶段，尚未声明通过 SEMI 一致性认证，也不建议用于生产设备。

## 设计目标

- 基础层动态表达任意 SECS-II 消息，不要求预先声明所有 SxFy 模板。
- HSMS 会话、SECS-II 事务和 GEM 能力分层实现。
- 同时支持 Host 与 Equipment 角色，不把连接主动/被动模式与业务角色绑定。
- 标准严格模式为默认行为；现场设备兼容选项显式启用并有回归测试。
- 支持 SML 调试格式，并逐步支持 SEMI E173 SMN 与 E172 SEDD。

## 规划包

| 包 | 职责 |
|---|---|
| `SecsFrame` | SECS-II 数据模型、HSMS-SS 会话与事务基础 |
| `SecsFrame.Gem` | GEM 通用状态模型与能力服务 |
| `SecsFrame.Smn` | E173 SMN 日志、文档与消息表示 |
| `SecsFrame.Sedd` | E172 SEDD 设备接口数据字典 |

当前代码覆盖第一阶段的 HSMS 线上帧基础，以及完整的 SECS-II Item
动态数据模型和严格二进制编解码。Item 用法与边界见
[docs/SECS-II-ITEMS.md](docs/SECS-II-ITEMS.md)，整体路线图见
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

var codec = new SecsItemCodec();
~~~

<code>SecsItemCodec</code> 默认严格拒绝非法格式、截断、数值宽度错位、
非 ASCII 字节、尾随数据和超过资源上限的树。JIS-8 在核心层以原始编码
字节表示，不猜测现场代码页。

## 构建

```bash
dotnet build SecsFrame.slnx -c Release
dotnet test SecsFrame.slnx -c Release --no-build
```

## 许可证

[MIT](LICENSE)

实现所跟踪的 SEMI 版本与版权边界见 [docs/STANDARDS.md](docs/STANDARDS.md)。
