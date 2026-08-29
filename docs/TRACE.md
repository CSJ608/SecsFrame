# Trace 导出、脱敏与重放

## 范围

<code>SecsFrame.Trace</code> 为已经解码的 SECS-II Data Message 提供：

- 确定性的 <code>SecsFrame-Trace/1</code> 文本导出和严格读取；
- 按 Stream、Function 与 Item List 路径执行的结构化替换脱敏；
- 只选择显式允许的本地发送记录，并通过正常公共发送 API 串行重放；
- 可选按缩放且封顶的源时间间隔执行受控时序重放；
- 不包含异常和原始帧的结构化 HSMS 诊断快照导出与严格读取；
- 显式启用的完整控制观测或公共未认领控制事件的十字节头元数据导出与
  严格读取。

当前切片不是原始网络抓包器，不记录 TCP 字节、transport Session generation、
控制帧的线上原始字节或异常对象，也不会接管 <code>HsmsConnection</code> 的
单消费者事件流。
应用应在自己的事件循环中从 <code>HsmsIncomingDataMessage</code> 创建接收记录，
并在调用发送 API 前显式创建发送记录。

## 记录与导出

<code>SecsTraceRecord</code> 保存 UTC 时间、本地方向、动态
<code>SecsMessage</code>，以及可选的协议 Session ID 和 System Bytes。
发送前通常尚不知道新事务的 System Bytes，因此这些 HSMS 字段允许缺省：

~~~csharp
using SecsFrame.Trace;

var sent = SecsTraceRecord.CreateSent(
    DateTimeOffset.UtcNow,
    primary,
    sessionId: connection.Options.SessionId);

var received = SecsTraceRecord.CreateReceived(
    DateTimeOffset.UtcNow,
    connectionEvent.IncomingMessage!);

var codec = new SecsTraceCodec();
var text = codec.Encode(new[] { sent, received });
var records = codec.Decode(text);
~~~

文件信封固定使用 LF，并在每条记录前写出随后 SML 块的字符长度：

~~~text
SecsFrame-Trace/1
Record 2026-08-29T05:00:00.0000000Z Sent 10 - 10
'S1F1'W
.
~~~

字符长度使 ASCII 转义、句点和换行不会被误识别成记录边界。Trace codec
限制总字符数与记录数；每个消息继续受 <code>SmlMessageCodec</code> 的深度、
Item、值和文本长度限制。解析错误使用包含记录索引与字符偏移的
<code>SecsTraceParseException</code>。

该格式是 SecsFrame 的版本化诊断信封，不是 SEMI 标准、PCAP 或原始 HSMS
wire trace。协议标识只用于问题关联，不能作为新连接上的重放标识。

## 结构化诊断信封

<code>SecsTraceDiagnosticRecord</code> 从公共 <code>HsmsDiagnostic</code>
创建只包含稳定标量的受限字段快照：

~~~csharp
if (connectionEvent.Diagnostic is { } diagnostic)
{
    var record = SecsTraceDiagnosticRecord.Create(
        DateTimeOffset.UtcNow,
        diagnostic);
    var text = new SecsTraceDiagnosticCodec().Encode(new[] { record });
}
~~~

独立信封固定使用 LF 与 11 个单空格分隔字段：

~~~text
SecsFrame-DiagnosticTrace/1
Diagnostic 2026-08-29T05:00:00.0000000Z T3Timeout Transaction WaitForSecondary Selected T3 10 0x10203040 - -
~~~

字段依次为 UTC 时间、诊断代码、层级、操作、会话状态、可选计时器、协议
Session ID、System Bytes、远端状态字节和被拒绝的 SType。枚举只接受当前
已定义的精确名称；协议字节使用固定宽度大写十六进制。codec 同样限制记录数
和总文本长度，并通过 <code>SecsTraceParseException</code> 报告记录索引与
字符偏移。

快照 API 不提供 <code>Error</code> 或 <code>Frame</code> 字段，创建快照时也
不会读取或格式化它们，因此异常消息和未解码帧负载不会进入导出文本。该格式
与 <code>SecsFrame-Trace/1</code> 消息信封相互独立，不能交给重放器。协议
标识和状态字节仍是运维元数据；存储和共享文件时仍需执行访问控制与保留策略。

## 控制消息元数据

推荐在连接选项中显式启用完整控制观测，并由应用持续消费独立流后创建只
包含头字段的快照：

~~~csharp
await foreach (var observation in connection
    .GetControlMessageObservationsAsync(cancellationToken))
{
    var record = SecsTraceControlRecord.Create(
        DateTimeOffset.UtcNow,
        observation);
    var text = new SecsTraceControlCodec().Encode(new[] { record });
}
~~~

独立信封固定使用 LF 与 10 个单空格分隔字段：

~~~text
SecsFrame-ControlTrace/1
Control 2026-08-29T05:00:00.0000000Z Received Selected 65535 0x05 0x03 0x00 0x07 0x10203040
~~~

字段依次为 UTC 时间、本地方向、观察到的会话状态，以及原十字节 HSMS 头的
Session ID、Header Byte 2、Header Byte 3、PType、SType 和 System Bytes。
字节字段使用固定宽度大写十六进制。SType 以原始字节保存：<code>0x00</code>
因表示 Data Message 而拒绝，未知非零值允许严格往返，便于保留互操作证据。

控制帧按核心模型不能携带 Body，快照类型也没有 Body 字段。完整观测流默认
关闭，启用后覆盖内部 Select、Linktest、Deselect、Reject 与 Separate：
入站记录发生在协议处理前，出站只记录成功写出的帧。Trace 工具不会自行
订阅该单消费者流，也不提供原始线上字节或 transport Session generation。
极快响应与本地发送完成回调可能以不同顺序进入 actor，因此记录顺序不是
PCAP 级线上时序；需要线速证据时不能从本元数据流推断字节先后。

既有 <code>SecsTraceControlRecord.CreateReceived</code> 仍可从业务事件流的
<code>ControlMessageReceived</code> 创建快照，但该事件只转发未被会话或
数据事务认领的控制帧，主要用于未匹配 Reject。两条创建路径生成同一信封；
控制信封不能进入消息重放器，头字段仍应按运维元数据执行访问控制与保留策略。

## 结构化脱敏

脱敏发生在不可变 Item 树上，而不是对 SML 文本做字符串替换：

~~~csharp
var redactor = new SecsTraceRedactor(new[]
{
    new SecsTraceRedactionRule(
        stream: 6,
        function: 11,
        itemPath: new[] { 0 },
        replacement: SecsItem.Ascii("REDACTED")),
});

var safeRecord = redactor.Redact(received);
var safeText = codec.Encode(new[] { safeRecord });
~~~

空路径选择根 Item，后续整数逐层选择 List 子项。匹配消息缺少根 Item、路径
进入非 List 或索引越界时立即失败，避免消息 Schema 漂移后静默漏脱敏。
同一 S/F 上相同或祖先/后代重叠的规则在构造时拒绝；互不重叠的兄弟路径
可以一起替换。原记录和原 Item 树不会被修改。

<code>SecsTraceCodec</code> 本身不会自动脱敏。写入磁盘、日志、Issue 或测试
附件前，应用必须根据自身数据分级先执行规则，并对导出结果使用常规访问控制
和保留策略。

## 受控重放

<code>SecsTraceReplayer</code> 要求调用方提供显式 allowlist predicate：

~~~csharp
var results = await new SecsTraceReplayer().ReplayAsync(
    records,
    connection,
    record => record.Message.Stream == 1 &&
        record.Message.Function == 1,
    cancellationToken);
~~~

重放具有以下固定边界：

- 先完整枚举、验证记录数量并计算 allowlist，再开始第一次发送；
- 只考虑 <code>Sent</code> 记录，<code>Received</code> 永远跳过；
- 按源顺序逐条等待正常 <code>SendAsync</code>，W-Bit 继续使用现有 T3；
- 忽略原时间、Session ID、System Bytes 和回复令牌，由活连接创建新事务；
- 默认的 <code>ReplayAsync</code> 不等待连接进入 Selected、不自动重试，
  也不按原时间间隔暂停；
- 返回每条已发送记录及新事务获得的可选 Secondary。

allowlist 是业务安全边界。库不会依据 Function 奇偶、消息名称或未知设备状态
推断 Primary/Secondary、幂等性或命令权限；应用只能允许已确认可在目标设备
与当前状态执行的消息。

### 可选时序

只有显式调用 <code>ReplayWithTimingAsync</code> 才会启用时序等待：

~~~csharp
var timing = new SecsTraceReplayTimingOptions(
    speedMultiplier: 2.0,
    maxDelay: TimeSpan.FromSeconds(5));

var results = await new SecsTraceReplayer().ReplayWithTimingAsync(
    records,
    connection,
    record => record.Message.Stream == 1 &&
        record.Message.Function == 1,
    timing,
    cancellationToken);
~~~

间隔只在经过方向与 allowlist 筛选的 <code>Sent</code> 记录之间计算；被跳过的
记录不会拉长等待。时间戳必须非递减，库会在第一次发送前完成全部检查。
第一条记录立即发送，后续间隔先除以 <code>SpeedMultiplier</code>，再按
<code>MaxDelay</code> 封顶；相同时间戳不等待。上例把源间隔加速两倍，并把
缩放后的单次等待限制为五秒。

每次等待从上一条消息的发送任务完成后开始，因此该模式保留相邻消息的最小
节奏，不承诺复现原始绝对发送时刻。等待期间取消会阻止下一条发送，已经完成
的发送不会回滚。时序模式同样不等待 Selected、不自动重试，也不会绕过
正常 T3 或连接状态检查。

## 验证边界

测试覆盖确定性信封、多记录往返、畸形头与长度、SML 嵌入错误、记录和文本
资源限制、路径漂移、规则重叠、敏感值不进入导出文本、预验证零发送，以及
时序缩放/封顶、筛选间隔、时间倒退预验证、等待取消和默认零等待。真实 TCP
测试还验证重新分配 Session ID/System Bytes 并获得新 Secondary。它证明
当前工具组合边界，不代表设备命令安全性或 SEMI 一致性认证。诊断信封另有
黄金向量、可选字段往返、严格枚举/十六进制、资源限制及异常/帧内容不泄漏
测试。控制信封另有完整 Select/Linktest 双向观测、未匹配 Reject 黄金向量、
未知 SType 往返、Data Message 拒绝、严格字段与资源限制测试。
