# Trace 导出、脱敏与重放

## 范围

<code>SecsFrame.Trace</code> 为已经解码的 SECS-II Data Message 提供：

- 确定性的 <code>SecsFrame-Trace/1</code> 文本导出和严格读取；
- 按 Stream、Function 与 Item List 路径执行的结构化替换脱敏；
- 只选择显式允许的本地发送记录，并通过正常公共发送 API 串行重放。

当前切片不是原始网络抓包器，不记录 TCP 字节、transport Session generation、
控制帧或异常对象，也不会接管 <code>HsmsConnection</code> 的单消费者事件流。
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
- 不等待连接进入 Selected，不自动重试，也不按原时间间隔暂停；
- 返回每条已发送记录及新事务获得的可选 Secondary。

allowlist 是业务安全边界。库不会依据 Function 奇偶、消息名称或未知设备状态
推断 Primary/Secondary、幂等性或命令权限；应用只能允许已确认可在目标设备
与当前状态执行的消息。

## 验证边界

测试覆盖确定性信封、多记录往返、畸形头与长度、SML 嵌入错误、记录和文本
资源限制、路径漂移、规则重叠、敏感值不进入导出文本、预验证零发送，以及
真实 TCP 上重新分配 Session ID/System Bytes 并获得新 Secondary。它证明
当前工具组合边界，不代表设备命令安全性或 SEMI 一致性认证。
