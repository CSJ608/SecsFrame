# StreamFrame 会话适配

## 上游基线

SecsFrame 固定依赖官方 [StreamFrame 2.3.0](https://www.nuget.org/packages/StreamFrame/2.3.0)。
该版本已经发布并解决此前跟踪的两项能力缺口：

- [#38](https://github.com/CSJ608/StreamFrame/issues/38) / [PR #41](https://github.com/CSJ608/StreamFrame/pull/41)：
  未完成帧超时与 <code>IncompleteFrameTimeout</code> 诊断；
- [#39](https://github.com/CSJ608/StreamFrame/issues/39) / [PR #42](https://github.com/CSJ608/StreamFrame/pull/42)：
  会话编号、会话绑定发送、写出确认和带会话归属的接收消息。

SecsFrame 不再维护基于原始字节回调的 T8 监视器、发送确认计数器、会话
信封 codec 或自建 TCP Session ID。公共 API 和内部
<code>IHsmsTransport</code> 契约保持不变，上层 HSMS 状态机不直接依赖
StreamFrame 类型。

## 会话边界

<code>StreamFrameHsmsTransport</code> 只消费
<code>ISessionAwareStreamConnection&lt;HsmsFrame&gt;</code>：

- <code>CurrentSessionId</code> 作为 TCP 会话的单调编号；
- <code>GetSessionMessages</code> 保留帧的原始会话归属，迟到消费不会被
  标记为新会话；
- <code>SendInSessionAsync</code> 把帧绑定到指定会话，旧会话帧不会在
  重连后重放；
- 发送任务仅在整帧写入本机 Socket 后完成，T3/T6 因而仍从明确的写出
  完成点启动。

<code>GetMessages</code> 与 <code>GetSessionMessages</code> 是同一接收通道
的竞争消费视图，适配器只使用后者。发送中途失败时，远端是否收到部分字节
仍是未知状态；上层必须依靠事务关联、超时和业务幂等恢复，不能假设远端
一定没有收到。

StreamFrame 的 <code>SessionExpiredException</code> 在内部边界转换为
<code>HsmsTransportSessionExpiredException</code>，并保留原异常。协议
主动关闭或 T8 关闭时，适配器按 TCP Session ID 保留首个关闭原因，使
<code>SessionClosed</code> 与尚未完成的发送观察到同一个协议异常。

## T5 与 T8

内部 <code>HsmsTransportOptions</code> 要求调用方显式给出 T5 与 T8，
不内置未经授权标准核对的默认值。Active 连接把 T5 映射为固定的
<code>ConnectRetryDelayMs</code> 并关闭最大退避；Passive 监听重试配置
保持调用方设置。原 <code>StreamConnectionOptions</code> 始终复制后再
适配，不会被原地修改。

T8 映射为 StreamFrame 的 <code>IncompleteFrameTimeoutMs</code>。因此
T5/T8 都必须为正值、能由整毫秒精确表示且不超过 <code>int.MaxValue</code>
毫秒；不允许截断、舍入或溢出。调用方在源 StreamFrame 选项中设置的
<code>IncompleteFrameTimeoutMs</code> 会被显式 HSMS T8 覆盖。

StreamFrame 只在已有未完成帧时运行该超时：空闲连接和完整帧后的静默不
触发；接收进展重置计时；到期先发布
<code>FrameErrorKind.IncompleteFrameTimeout</code>，再拆除对应会话。
适配器把该诊断转换为携带 transport Session ID 的
<code>HsmsT8TimeoutException</code>，不再次请求重连。

## 验证边界

适配器单元测试覆盖原生 Session ID、迟到消息、发送完成、并发入队、会话
失效、显式关闭原因和 T8 诊断映射。真实 TCP 回环测试覆盖分片帧收发与
实际 T8 到期；完整套件继续覆盖 T3/T6 从发送完成点启动以及旧会话不
重放。

这些测试证明当前工程契约与 StreamFrame 2.3.0 的互操作行为，不构成
SEMI 合规声明。T5/T8 的标准默认值、精确启停边界和异常恢复仍须依据团队
合法获得的 SEMI E37/E37.1 版本及一致性测试核对。仓库不得提交标准正文、
表格或 Schema。
