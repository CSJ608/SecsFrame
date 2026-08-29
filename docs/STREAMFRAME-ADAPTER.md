# StreamFrame 会话适配

## 上游基线

SecsFrame 固定依赖官方 [StreamFrame 2.6.0](https://www.nuget.org/packages/StreamFrame/2.6.0)。
2.3.0 已经解决此前跟踪的两项能力缺口：

- [#38](https://github.com/CSJ608/StreamFrame/issues/38) / [PR #41](https://github.com/CSJ608/StreamFrame/pull/41)：
  未完成帧超时与 <code>IncompleteFrameTimeout</code> 诊断；
- [#39](https://github.com/CSJ608/StreamFrame/issues/39) / [PR #42](https://github.com/CSJ608/StreamFrame/pull/42)：
  会话编号、会话绑定发送、写出确认和带会话归属的接收消息。

2.3.1 在相同公共 API 上修复两个会话隔离缺陷：过期会话的迟到故障不会
再发布 Retry 或清零活会话编号；发送 worker 会再次校验排队条目的 Session
ID，残留的旧会话绑定消息以 <code>SessionExpiredException</code> 结束且
不会写入新 Socket。停机、Socket 释放及会话故障竞态也统一收敛到该异常
边界。详见 [2.3.1 发布说明](https://github.com/CSJ608/StreamFrame/releases/tag/v2.3.1)。

2.4.0 不移除 SecsFrame 使用的会话感知公共接口。会话 epoch 改为与 Session
ID 一样在 Connected 发布点分配，闭合状态已经对外可见但会话任务尚未创建
期间的旧 epoch 故障窗口；Passive 接受重试延迟移到锁外，并为监听 Socket
设置 <code>SO_REUSEADDR</code>，提高故障重试和立即重绑的跨平台稳定性。
底层连接还会通过 <code>System.Diagnostics.Metrics</code> 的
<code>StreamFrame</code> Meter 发出内置指标，但 SecsFrame 不把这些类型
提升为公共 API。详见
[2.4.0 发布说明](https://github.com/CSJ608/StreamFrame/releases/tag/v2.4.0)。

2.5.0 继续保留上述公共接口和目标框架。发送 worker 会按连接记忆上一帧
编码高水位并自适应选择初始缓冲区，减少尺寸相近的大报文在暖机期跨池扩容；
该缓冲策略不进入 SecsFrame 公共 API，也不改变帧或消息编码。Passive
接受循环新增代次门控，被显式 <code>Reconnect</code> 取代的旧循环不能再
创建、关闭监听器或发布 Connected，修复显式关闭与对端断开触发的自动重连
竞速可能泄漏无人消费监听 Socket 的问题。详见
[2.5.0 发布说明](https://github.com/CSJ608/StreamFrame/releases/tag/v2.5.0)、
[PR #51](https://github.com/CSJ608/StreamFrame/pull/51) 和
[PR #52](https://github.com/CSJ608/StreamFrame/pull/52)。

2.6.0 在兼容既有构造函数的同时，为四种 <code>FrameErrorKind</code> 统一
增加错误来源 <code>SessionId</code>、<code>ObservedByteCount</code> 和
<code>IsTruncated</code>。SecsFrame 因而不再需要在回调时读取当前会话来
推断故障归属，并可安全公开迟到旧会话错误。详见
[2.6.0 发布说明](https://github.com/CSJ608/StreamFrame/releases/tag/v2.6.0)
和 [Issue #56](https://github.com/CSJ608/StreamFrame/issues/56)。

SecsFrame 不再维护基于原始字节回调的 T8 监视器、发送确认计数器、会话
信封 codec 或自建 TCP Session ID。内部 <code>IHsmsTransport</code> 现可在
显式启用时转发四种成帧故障的会话关联前缀快照与完整性元数据；上层 HSMS
状态机仍不直接依赖 StreamFrame 类型。

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
当前会话的 <code>Reconnect</code> 与 transport 释放还通过独立生命周期
边界串行化：释放先阻止新的关闭请求，再等待已经开始的重连结束，避免 actor
清空迟到 Separate 输入时访问已释放的 StreamFrame 生命周期。

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
<code>HsmsT8TimeoutException</code>，不再次请求重连。公共连接另提供默认
关闭的独立运输故障观测流。适配器使用
<code>FrameErrorEventArgs.SessionId</code> 发布 DecodeFailed、
DiscardedByResync、IncompleteFrameOverflow 和 IncompleteFrameTimeout；
会话 actor 过滤旧 Session 并附加观察状态。默认路径不发布该 transport
fault 事件，且只有 IncompleteFrameTimeout 会成为 T8 关闭原因。

观测保留 StreamFrame 报告的实际字节数与截断状态。SecsFrame 对所有错误
类型再统一只复制最多 8 KiB 前缀，因此即使重同步丢弃样本更大也不会形成
无界公共诊断数据；二次截断会反映在 <code>IsTruncated</code> 中。快照可能
为空，也可能包含四字节 HSMS 长度头，必须结合
<code>ObservedByteCount</code> 和 <code>IsTruncated</code> 判断完整性。
观测通道容量显式可配，满时丢弃最旧项，不能反向阻塞协议 actor。

## 验证边界

适配器单元测试覆盖原生 Session ID、迟到消息、发送完成、并发入队、会话
失效、显式关闭原因、关闭/释放竞态、四种故障映射、实际字节数、截断状态、
快照复制、旧会话过滤、8192/8193 字节边界和有界队列。真实 TCP 回环测试
覆盖分片帧收发、实际 T8 到期及前缀快照，以及
会话替换时排队发送的精确失效与新 Socket
内容隔离；Passive 重连竞态测试反复并发对端关闭与本地显式关闭，验证监听
恢复、Session ID 单调递增和原会话精确关闭。完整套件继续覆盖 T3/T6 从
发送完成点启动以及旧会话不重放。

这些测试证明当前工程契约与 StreamFrame 2.6.0 的互操作行为，不构成
SEMI 合规声明。T5/T8 的标准默认值、精确启停边界和异常恢复仍须依据团队
合法获得的 SEMI E37/E37.1 版本及一致性测试核对。仓库不得提交标准正文、
表格或 Schema。
