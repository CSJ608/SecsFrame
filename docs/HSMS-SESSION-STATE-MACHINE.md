# HSMS-SS 会话状态机

## 当前范围

内部 <code>HsmsSessionStateMachine</code> 在
<code>IHsmsTransport</code> 之上提供第一个完整会话切片：

- TCP Session 打开和关闭映射为 Disconnected、Connected、Selecting、
  Selected；
- Active 模式发起 Select Request，Passive 模式响应 Select Request；
- Select Response 使用等待中的 System Bytes 关联；
- T6 从 Select Request 的完整线上帧实际写出后开始；
- T7 从 TCP Session 打开后开始，并在进入 Selected 时取消；
- Active 运输使用显式 T5 固定连接重试，未完成接收使用显式 T8；两者
  属于 StreamFrame 运输适配边界，不由会话 actor 重复计时；
- 本地 Linktest 与 Deselect 命令按 System Bytes 等待响应，T6 从完整
  请求实际写出后开始；
- 对端 Linktest 被应答；Deselect 成功后回到 Connected 并重新启动 T7；
- 对可安全回应的无效消息生成 Reject Request；
- 本地和远端 Separate 都终止当前协议会话；
- 只有 Selected 状态的数据消息才向上游事件流转发；
- 本地数据发送同样进入 actor 做 Selected 与当前 Session ID 校验，任务
  在 <code>IHsmsTransport</code> 确认完整写出后完成。

连接主动/被动模式不表示 Host/Equipment 业务角色。状态机没有
Host/Equipment 分支，后续角色 API 可以独立组合。

## 串行化与会话隔离

传输事件、控制帧发送完成、计时器回调和本地命令写入单读取者队列。
异步 Socket 发送不阻塞状态机，但发送结果会带回原 Session ID、用途和
System Bytes。旧会话的帧、发送完成和计时器回调不会推进替换会话。
本地主动 Linktest、Deselect 和 Separate 命令单飞，避免共享 T6 被并发
命令覆盖。数据发送可以并发等待底层串行写出，但每次写入仍绑定接受命令
时的 transport Session ID；会话关闭立即结束等待且不会在替换会话重放。

所有协议主动关闭都调用 <code>IHsmsTransport.TryCloseSession</code>，
只有 Session ID 仍匹配当前 TCP 连接时才执行。关闭原因随后通过
<code>SessionClosed</code> 回到状态机并随 Disconnected 事件报告。

## 严格行为

当前工程基线执行以下严格检查：

- 控制消息必须使用 Session ID <code>0xFFFF</code>、零 PType 和零
  Header Byte 2；Reject Request 的 Header Byte 2 例外，用于携带被拒绝
  消息的 SType；
- Select、Linktest、Deselect Request 与 Separate Request 的保留状态
  字节必须为零，Linktest Response 同样必须为零；
- Select Response 必须匹配正在等待的 System Bytes；
- Linktest/Deselect Response 必须同时匹配等待中的类型与 System Bytes；
- 已 Selected 时收到新的 Select Request，响应内部
  <code>AlreadySelected</code> 状态并保持 Selected；
- 选择拒绝返回 Connected，由仍在运行的 T7 限制未选中会话；
- Unsupported SType、Unsupported PType、没有打开的控制事务和 Selected
  前的数据分别生成内部 Reject 原因；不能安全回应的畸形控制头关闭会话；
- 匹配会话控制事务的 Reject 结束对应等待，未匹配 Reject 向上转发给后续
  数据事务层。

以上是自主实现与测试形成的工程行为摘要，不是 SEMI 标准状态表的复制，
也不构成完整 E37/E37.1 合规声明。

## 测试边界

手动传输和手动计时器测试覆盖：

- 主动和被动 Select、匹配 System Bytes 与完整控制头字段；
- 写入确认前不启动 T6、T6/T7 到期及选择拒绝；
- 同时 Select、重复 Select 和旧计时器回调隔离；
- Selected 前拒绝数据、Selected 后转发数据；
- Linktest/Deselect 的写出确认、T6、响应匹配、拒绝和并发互斥；
- Reject 黄金向量、四类生成路径、未消费 Reject 转发；
- 非法控制头、本地及远端 Separate；
- Selected 数据发送门控、完整写出确认、会话关闭和替换会话不重放。

另有真实 TCP 回环测试，让 Active 与 Passive
<code>StreamFrameHsmsTransport</code> 完成 Select、Linktest 和 Deselect
流程；其上的事务回环还完成带嵌套 Item 的 Primary/Secondary 往返。数据
事务细节见 [HSMS-DATA-TRANSACTIONS.md](HSMS-DATA-TRANSACTIONS.md)。
公共 <code>HsmsConnection</code> 另行验证同一路径的外部组合契约，见
[HSMS-CONNECTION.md](HSMS-CONNECTION.md)。
独立夹具还与 nuget.org 官方 Secs4Net 3.1.0 在双方 Active/Passive 模式下
完成 Select、双方 Linktest 和双方 Primary/Secondary，见
[SECS4NET-INTEROP.md](SECS4NET-INTEROP.md)。测试证明当前实现路径可互操作，
不替代认证工具或真实设备证据。

## 尚未实现

- 授权标准核对后的 T5/T8 默认值和完整标准语义；
- 自动周期 Linktest 调度；
- Host/Equipment 能力 API；
- 与真实设备、认证工具和更多实现版本的互操作矩阵。

实现这些行为前，需要使用团队合法获得的 SEMI E37-0222 与 E37.1-0819
副本核对精确状态转换、控制字段、状态/原因值和计时边界。仓库不得提交
标准正文、表格、图或由付费材料机械转换的测试 Schema。
