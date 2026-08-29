# 标准版本与版权边界

## 版本基线

以下版本来自 SEMI 官方目录在 2026-08-28 显示的 Current 版本。每个
协议切片开始前仍需使用团队合法获得的标准副本核对规范性条款和勘误。

| 层 | 标准版本 | 当前状态 |
|---|---|---|
| SECS-II 消息内容 | SEMI E5-0725 | Item、动态消息编解码与内部 T3 数据事务已有工程实现；完整事务规则待核对 |
| HSMS / HSMS-SS | SEMI E37-0222 / E37.1-0819 | 帧、Data Payload、内部传输、T5/T8 工程计时、控制状态机与会话绑定数据发送已有实现；仍待授权标准和一致性测试核对 |
| GEM | SEMI E30-0526 | 通讯建立、双向应用接受策略与断线重选后的显式单次恢复、上下线、动态变量/常量、时钟、报告/事件、报警通知/目录/发送启停，以及带应用状态接受策略、运行期精确分派目录和逐项本地可用性的远程命令已有可替换 profile 的工程实现；规范性条件待核对 |
| SEDD | SEMI E172-0225 | 规划中 |
| SMN | SEMI E173-0721 | 规划中 |

公开目录链接：

- [SEMI E5](https://store-us.semi.org/products/e00500-semi-e5-specification-for-semi-equipment-communications-standard-2-message-content-secs-ii)
- [SEMI E37](https://store-us.semi.org/products/e03700-semi-e37-high-speed-secs-message-services-hsms-generic-services)
- [SEMI E30](https://store-us.semi.org/products/e03000-semi-e30-specification-for-the-generic-model-for-communications-and-control-of-manufacturing-equipment-gem)
- [SEMI E172](https://store-us.semi.org/products/e17200-semi-e172-specification-for-secs-equipment-data-dictionary-sedd)
- [SEMI E173](https://store-us.semi.org/products/e17300-semi-e173-specification-for-xml-secs-ii-message-notation-smn)

## 实现前必须查证

- E5：Item 格式码、长度表示、Boolean 取值、ASCII/JIS-8 字符约束、
  消息头、Primary/Secondary 定义、W-Bit 与 Function 约束、T3 精确
  启停、System Bytes 分配、SxF0/S9Fx 对事务的影响，以及当前版相对
  上一版的技术变更。现有 Item、消息与事务向量是工程验证基线，不替代
  合法标准副本或一致性测试。
- E37/E37.1：Single Selected Session 的状态转换、控制消息字段、拒绝
  原因、并发 Select、T5/T6/T7/T8 的精确启停边界、Separate 后的连接
  处理、数据发送与 Reject/Linktest/Deselect 规则。当前控制平面枚举值、
  非 Selected 下 Linktest 响应、Deselect 状态处理、完整写出后启动 T3
  以及 T5 固定重试和 T8 接收进展计时器都是工程验证基线；连接失败分类、
  每个计时器的具体默认值、精确启停点、状态表和一致性结论仍须依合法
  标准副本核对。
- E30：通讯建立、在线/离线、变量、事件报告、报警、远程命令和时钟能力
  的必选/可选边界；还需核对基础消息条件、COMMACK/ONLACK/OFLACK/TIACK
  取值、MDLN 条件结构、空 SVID/ECID 列表、报告定义与链接生命周期、
  Collection Event 数据结构和应答值、报警通知/目录/发送启停的数据结构与
  控制码、应用通讯建立/在线转换策略、断线重选后通讯恢复的状态与重试
  条件、远程命令执行条件与状态门控、时间格式、状态转换与错误响应。
- E172/E173：允许分发的 Schema/表示形式、版本标识、扩展点和验证规则。

## 版权与合规边界

可以提交：

- 标准编号、版本、公开标题和独立撰写的行为摘要；
- 自主设计的 API、实现和测试；
- 自主构造的最小协议向量与互操作结果；
- 许可证允许的开源参考及其必要归属。

不得提交：

- SEMI 标准正文、表格、图、附录或大段近似改写；
- 从付费标准复制或机械转换的 Schema、消息目录和数据字典；
- 来源不明的抓包、厂商文档或一致性测试材料；
- 未经验证的“完整合规”“通过认证”等声明。

本项目只读参考 StreamFrame 和 MIT 许可的 secs4net 以交叉理解公开协议
实现，没有复制参考仓库代码。该 secs4net 源码 checkout 不参与互操作
构建；互操作夹具只允许使用 nuget.org 发布并固定版本的官方 NuGet 包。
未来如引入实质性开源代码，必须保留对应版权与许可证文本，并在变更说明
中记录来源。

<code>SecsFrame.Sml</code> 的调试 profile 不是规范性标准实现。开发前只读
参考了 MIT 许可的 secs4net 公开 SML 项目以交叉核对常见文本拼写；当前
实现和测试向量均独立编写，JIS-8 原始字节与 ASCII 转义边界见
[SML.md](SML.md)。

与官方 Secs4Net 3.1.0 的 Select、Linktest 和动态消息往返是独立软件实现
之间的工程互操作证据，不代表任何一方通过 SEMI 一致性认证。具体矩阵见
[SECS4NET-INTEROP.md](SECS4NET-INTEROP.md)。
