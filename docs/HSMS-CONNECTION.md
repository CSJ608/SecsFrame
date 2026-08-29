# 公共 HSMS 连接 API

## 范围

<code>HsmsConnection</code> 是当前基础层的最小公共入口。它在内部组合
StreamFrame 运输适配、HSMS-SS 会话 actor 和数据事务 actor，调用方不需要
接触 transport Session generation、发送确认回调或内部 channel。

当前公共能力包括：

- Active 或 Passive TCP 连接以及 Select 会话建立；
- 等待 Selected 状态和读取当前状态；
- 发送任意动态 <code>SecsMessage</code>，无需消息目录或预生成类型；
- W-Bit Primary 的 T3 关联与 Secondary 返回；
- 接收入站 Primary 或未匹配消息，并使用不可伪造令牌回复一次；
- Linktest、Deselect 和 Separate 控制命令；
- 状态、数据、未消费控制消息和解码失败的单消费者异步事件流；
- 默认关闭、与业务事件独立的完整控制消息头元数据观测流；
- 默认关闭、容量有界的四类运输故障前缀快照观测流。

Host/Equipment 是后续业务能力角色，不由 Active/Passive 推断。
需要按运行期 Stream/Function 处理 Primary 时，可在本事件流上组合
<code>HsmsPrimaryRouter</code>；它不接管事件消费者，未匹配事件仍由应用
处理。见 [HSMS-PRIMARY-ROUTING.md](HSMS-PRIMARY-ROUTING.md)。

## 显式配置

<code>HsmsConnectionOptions</code> 构造时必须给出：

| 配置 | 当前用途 |
|---|---|
| IP 地址、端口 | Active 的远端地址或 Passive 的本地监听地址 |
| ConnectionMode | 只表示哪一端建立 TCP 连接 |
| SessionId | 出站 Data Message 使用的协议 Session ID |
| T3 | W-Bit Primary 写完后等待 Secondary |
| T5 | Active TCP 连接失败后的固定重试间隔 |
| T6 | Select、Linktest、Deselect 控制响应等待 |
| T7 | TCP 建立后等待进入 Selected |
| T8 | 未完成消息接收进展等待 |
| EnableControlMessageObservation | 是否创建完整控制消息元数据观测流；默认 false |
| EnableTransportFaultObservation | 是否创建运输故障快照观测流；默认 false |
| TransportFaultObservationCapacity | 故障队列容量；满时丢弃最旧项，默认 16 |

仓库没有内置或暗示标准默认值。T5 必须能无损转换成 StreamFrame 使用的
整毫秒范围；Passive 监听重试不被解释成 T5。每个取值应依据团队合法获得
的 E37/E37.1 版本、设备通信手册和互操作证据确定。

## 生命周期

~~~csharp
var options = new HsmsConnectionOptions(
    IPAddress.Parse("192.0.2.10"),
    port: 5000,
    HsmsConnectionMode.Active,
    sessionId: 0,
    t3: TimeSpan.FromSeconds(45),
    t5: TimeSpan.FromSeconds(10),
    t6: TimeSpan.FromSeconds(5),
    t7: TimeSpan.FromSeconds(10),
    t8: TimeSpan.FromSeconds(5),
    enableControlMessageObservation: true);

await using var connection = new HsmsConnection(options);
connection.Start();
await connection.WaitUntilSelectedAsync(cancellationToken);

var secondary = await connection.SendAsync(
    new SecsMessage(
        stream: 1,
        function: 1,
        replyExpected: true),
    cancellationToken);
~~~

以上计时值只演示 API 形状，不是标准推荐值。

<code>Start</code> 只能调用一次，连接活动期由
<code>DisposeAsync</code> 结束。<code>WaitUntilSelectedAsync</code>
不消费业务事件；取消它只取消本次等待，不停止连接。

<code>GetEventsAsync</code> 同一时刻只允许一个消费者；枚举器结束或释放
后可以重新获取。连接内部先独占消费事务 actor 的事件，再写入公共事件
流，因此 Selected 等待不会和业务接收争抢消息。调用方通常应使用一个
长期事件循环处理：

- <code>StateChanged</code>：状态转换和可选错误；
- <code>DataMessageReceived</code>：携带
  <code>HsmsIncomingDataMessage</code>；
- <code>ControlMessageReceived</code>：未被当前控制或数据事务认领的帧；
- <code>DataMessageDecodeFailed</code>：原始帧和严格解码错误。

<code>DataMessageDecodeFailed</code> 的帧可能包含设备或工艺数据。默认应只
使用结构化诊断；需要保留已成帧互操作样本时，可使用 Trace 包的显式分级
故障样本信封，见 [TRACE.md](TRACE.md)。它不会自动订阅本事件流。

调用方在自己的事件循环中可以使用
<code>SecsTraceControlRecord.CreateReceived</code> 对
<code>ControlMessageReceived</code> 创建不含 Body 的头元数据快照。该事件
不包含已被内部 Select、Linktest、Deselect 或数据事务认领的控制消息，
因此快照不是完整控制面抓包。格式与安全边界见 [TRACE.md](TRACE.md)。

完整控制面诊断必须在构造连接时显式设置
<code>EnableControlMessageObservation</code>，并由应用持续消费独立流：

~~~csharp
await foreach (var observation in connection
    .GetControlMessageObservationsAsync(cancellationToken))
{
    var record = SecsTraceControlRecord.Create(
        DateTimeOffset.UtcNow,
        observation);
}
~~~

该流与 <code>GetEventsAsync</code> 各自只有一个活动消费者，取消或释放旧
枚举器后可重新获取，二者互不争抢。入站控制头在会话 actor 执行协议处理和
状态转换前发布；出站控制头只在整帧写入成功后发布，因此 Select Request
通常观察为 Selecting，Passive 的成功 Select Response 仍观察为 Connected。
极快响应可能先于本地发送完成回调进入 actor，跨方向记录顺序和出站状态应
解释为 actor 实际处理顺序，而不是线上字节的精确时间顺序。
启用后通道会缓存尚未消费的记录，应用必须持续读取；默认关闭时不创建通道，
调用观测 API 会失败。

<code>HsmsControlMessageObservation</code> 只包含本地方向、观察状态和原
十字节 <code>HsmsMessageHeader</code>。它不含 Body、原始 TCP/帧字节、
transport Session generation 或时间戳，也不改变未认领
<code>ControlMessageReceived</code> 业务事件的既有行为。

运输故障原始诊断必须另外显式设置
<code>EnableTransportFaultObservation</code>。该流不占用业务事件消费者：

~~~csharp
await foreach (var fault in connection
    .GetTransportFaultObservationsAsync(cancellationToken))
{
    var record = SecsTraceTransportFaultRecord.Create(
        DateTimeOffset.UtcNow,
        fault,
        SecsTraceTransportFaultCaptureOptions.MetadataOnly());
}
~~~

<code>HsmsTransportFaultObservation</code> 覆盖 DecodeFailed、
DiscardedByResync、IncompleteFrameOverflow 和 IncompleteFrameTimeout，
保留 StreamFrame 报告的原始 TCP Session ID、actor 观察状态、实际字节数、
截断状态和防御性复制的前缀快照。SecsFrame 对所有类型统一最多保留 8 KiB；
快照可能为空，也可能包含四字节 HSMS 长度前缀。队列按配置容量保留最新项，
满时丢弃最旧项而不阻塞协议 actor；应用启用后应持续消费。只有 T8 超时会
影响关闭原因，其余观测不改变连接行为。该观测在对应会话关闭事件处理前
写入，但两个独立异步流之间不承诺消费者可见的全局顺序。

带错误的状态事件和解码失败事件还会提供可选
<code>HsmsDiagnostic</code>，用于按稳定代码、层级、操作与计时器处理，
无需解析异常消息。调用任务抛出的异常可通过
<code>HsmsDiagnostic.Classify</code> 使用同一分类。完整契约和原始帧的
数据安全边界见 [HSMS-DIAGNOSTICS.md](HSMS-DIAGNOSTICS.md)。

入站消息只有 <code>ReplyExpected</code> 为真时可以回复。回复自动复制原
协议 Session ID 与 System Bytes，并绑定原 transport session；开始回复
后令牌不可再次使用，也不会在替换连接上重放。

## 取消与失败

- 出站无 W-Bit 消息在整帧写完后完成；
- 出站 W-Bit Primary 在整帧写完后启动 T3；
- 取消发送会结束本地等待，不会自动关闭 Selected 会话，也不能撤回已经
  上线的字节；迟到 Secondary 作为未匹配入站消息上报；
- Deselect、Separate、断线、替换会话或释放会结束绑定旧会话的等待；
- 事件枚举的取消只结束该消费者；连接仍须显式释放。

具体异常类型继续以 <code>TimeoutException</code>、
<code>IOException</code>、<code>OperationCanceledException</code> 和参数/
状态异常为兼容分类；<code>HsmsDiagnostic</code> 在其上提供更细的机器可读
上下文。调用方取消、释放和生命周期误用不被分类成协议故障。

## 验证边界

真实 TCP 测试使用两个公共连接完成 Select、Linktest、嵌套 Item
Primary/Secondary、重复回复拒绝和取消后保持 Selected。它证明当前实现
各层能够组合工作；另有完整控制观测测试核对双方 Select/Linktest 的方向、
状态和头字段相等。它们不替代 SEMI 一致性认证或第三方设备互操作。
