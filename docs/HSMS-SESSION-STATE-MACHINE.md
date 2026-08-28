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
- 本地和远端 Separate 都终止当前协议会话；
- 只有 Selected 状态的数据消息才向上游事件流转发。

连接主动/被动模式不表示 Host/Equipment 业务角色。状态机没有
Host/Equipment 分支，后续角色 API 可以独立组合。

## 串行化与会话隔离

传输事件、控制帧发送完成、计时器回调和本地命令写入单读取者队列。
异步 Socket 发送不阻塞状态机，但发送结果会带回原 Session ID、用途和
System Bytes。旧会话的帧、发送完成和计时器回调不会推进替换会话。

所有协议主动关闭都调用 <code>IHsmsTransport.TryCloseSession</code>，
只有 Session ID 仍匹配当前 TCP 连接时才执行。关闭原因随后通过
<code>SessionClosed</code> 回到状态机并随 Disconnected 事件报告。

## 严格行为

当前工程基线执行以下严格检查：

- 控制消息必须使用 Session ID <code>0xFFFF</code>、零 PType 和零
  Header Byte 2；
- Select Request 与 Separate Request 的状态字节必须为零；
- Select Response 必须匹配正在等待的 System Bytes；
- 已 Selected 时收到新的 Select Request，响应内部
  <code>AlreadySelected</code> 状态并保持 Selected；
- 选择拒绝返回 Connected，由仍在运行的 T7 限制未选中会话；
- Selected 前的数据、意外控制消息和非法控制头关闭当前会话。

以上是自主实现与测试形成的工程行为摘要，不是 SEMI 标准状态表的复制，
也不构成完整 E37/E37.1 合规声明。

## 测试边界

手动传输和手动计时器测试覆盖：

- 主动和被动 Select、匹配 System Bytes 与完整控制头字段；
- 写入确认前不启动 T6、T6/T7 到期及选择拒绝；
- 同时 Select、重复 Select 和旧计时器回调隔离；
- Selected 前拒绝数据、Selected 后转发数据；
- 非法控制头、意外响应、本地及远端 Separate。

另有真实 TCP 回环测试，让 Active 与 Passive
<code>StreamFrameHsmsTransport</code> 完成 Select 握手。测试证明当前
实现路径可互操作，不替代认证工具或第三方设备互操作证据。

## 尚未实现

- Linktest Request/Response；
- Reject Request 的生成与接收策略；
- Deselect Request/Response；
- T3 数据事务、T5 重连节流和完整 T8 标准语义；
- 公共会话 API、Host/Equipment 能力 API；
- 与 secs4net 或真实设备的跨实现互操作矩阵。

实现这些行为前，需要使用团队合法获得的 SEMI E37-0222 与 E37.1-0819
副本核对精确状态转换、控制字段、状态/原因值和计时边界。仓库不得提交
标准正文、表格、图或由付费材料机械转换的测试 Schema。
