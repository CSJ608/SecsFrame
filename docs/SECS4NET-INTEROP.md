# 官方 secs4net 互操作证据

## 依赖身份

互操作项目 <code>SecsFrame.Secs4NetInterop.Tests</code> 只引用 nuget.org
发布的官方包：

| 字段 | 值 |
|---|---|
| Package ID | <code>Secs4Net</code> |
| 固定版本 | <code>3.1.0</code> |
| NuGet 源 | <code>https://api.nuget.org/v3/index.json</code> |
| 作者 / 许可 | <code>mkjeff</code> / MIT |
| 包内仓库提交 | <code>6f540a7540fc7a531d1d526c04375f4b05a7641f</code> |
| 测试框架 | <code>net8.0</code>、<code>net10.0</code> |

版本由根目录 <code>Directory.Packages.props</code> 中央管理。仓库
<code>NuGet.Config</code> 先清除继承源，再把所有包映射到 nuget.org。
测试项目没有指向 secs4net 源码的 <code>ProjectReference</code>、本地 feed
或程序集路径；本地源码 checkout 不参与还原、编译或运行。

Secs4Net 3.1.0 的正式包只提供 net8.0 和 net10.0 资产，所以互操作项目
只运行这两个目标。SecsFrame 自身的 net48 构建和主测试矩阵继续保留，
但不伪造官方包未提供的 net48 跨实现结果。

## 真实 TCP 矩阵

每个目标框架都在 IPv4 loopback 上运行以下两种独立拓扑：

| SecsFrame | 官方 Secs4Net | Select 发起方 | 已验证行为 |
|---|---|---|---|
| Active | Passive | SecsFrame | Select、双方 Linktest、双方 Primary/Secondary |
| Passive | Active | Secs4Net | Select、双方 Linktest、双方 Primary/Secondary |

“双方 Linktest”包括 SecsFrame 主动请求并等待官方实现响应，以及启用官方
实现的周期 Linktest 后确认它收到 SecsFrame 的响应。测试中的短周期只用于
确定性验证，不是标准默认值或部署建议。

## 消息向量

SecsFrame 到 Secs4Net 使用带 W-Bit 的 S6F11，根 Item 包含嵌套 List、
ASCII、U1 的 0/最大值、I4 的最小/最大值和 Boolean；官方实现读取每个值
后以 S6F12 Boolean 回复。反向使用带 W-Bit 的 S1F1，包含 ASCII、嵌套
List、U2 的 0/最大值和 Boolean；SecsFrame 严格解码后以 S1F2 Binary
回复。两边都验证 Stream、Function、协议 Session/Device ID 和 Item 值。

每个目标框架包含两个测试用例，对应上述两种连接拓扑。验证命令：

~~~bash
dotnet test test/SecsFrame.Secs4NetInterop.Tests/SecsFrame.Secs4NetInterop.Tests.csproj -c Release -f net8.0
dotnet test test/SecsFrame.Secs4NetInterop.Tests/SecsFrame.Secs4NetInterop.Tests.csproj -c Release -f net10.0
~~~

## 结论边界

该矩阵证明 SecsFrame 当前工程实现能与指定官方软件版本完成所列 HSMS-SS
控制流程和动态 SECS-II 消息往返。它没有覆盖真实设备、网络故障注入、
所有 Item 组合、全部控制状态或长期运行，也不替代 SEMI 标准核对、认证
工具或一致性测试，不据此声明完整 E5/E37/E37.1 合规。
