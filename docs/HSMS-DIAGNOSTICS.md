# HSMS 结构化诊断

## 目标与边界

<code>HsmsDiagnostic</code> 为公共连接事件和操作异常提供稳定、机器可读的
故障上下文。应用可以按 <code>Code</code>、<code>Layer</code>、
<code>Operation</code> 和 <code>Timer</code> 做指标、告警与恢复决策，不需要
解析英文异常消息。

诊断不替换原异常：<code>HsmsConnectionEvent.Error</code> 和调用任务抛出的
异常保持原有类型层级，诊断的 <code>Error</code> 属性指向同一个异常实例。
这使现有按 <code>TimeoutException</code>、<code>IOException</code> 或
<code>OperationCanceledException</code> 处理的代码继续工作。

## 事件诊断

<code>HsmsConnectionEvent.Diagnostic</code> 在以下事件中按当前证据填充：

- 带可分类错误的 <code>StateChanged</code>；
- 所有 <code>DataMessageDecodeFailed</code>。

正常状态转换、业务消息和未消费控制消息的诊断为空。远端正常关闭但没有
运输错误的状态转换也不会被强行解释成故障。

~~~csharp
await foreach (var connectionEvent in connection.GetEventsAsync(cancellationToken))
{
    if (connectionEvent.Diagnostic is not { } diagnostic)
        continue;

    RecordMetric(
        diagnostic.Code,
        diagnostic.Layer,
        diagnostic.Operation,
        diagnostic.Timer);
}
~~~

## 操作异常分类

<code>SendAsync</code>、<code>ReplyAsync</code> 和控制命令的异常可以通过
<code>HsmsDiagnostic.Classify</code> 分类：

~~~csharp
try
{
    await connection.SendAsync(primary, cancellationToken);
}
catch (Exception error)
{
    var diagnostic = HsmsDiagnostic.Classify(error, connection.State);
    if (diagnostic is null)
        throw;

    HandleHsmsFailure(diagnostic);
}
~~~

当前稳定代码覆盖运输失败/会话失效、协议错误、T3/T6/T7/T8、选择与
控制拒绝、数据拒绝、事务中断和数据消息解码失败。T3 诊断携带协议
Session ID 与 System Bytes；拒绝诊断保留远端状态或原因字节。

调用方取消、释放、参数错误、生命周期误用和未知异常返回空诊断。这些结果
不能被监控系统误记为协议或设备故障。T5 当前是 Active 连接重试节流，
不是每次失败都会产生一个独立公共诊断；<code>HsmsTimer.T5</code> 只保留
计时器身份，等待未来有明确、经过验证的可观测契约。

## 数据安全

解码失败诊断的 <code>Frame</code> 与事件中的原始帧相同，可能包含设备或
工艺数据。库不自动格式化、写日志或上传它。应用在记录前应按自己的数据
分类策略做脱敏和长度限制；常规指标优先只使用代码、层级、操作和计时器。

## 标准边界

诊断名称描述 SecsFrame 已实现并测试的工程行为，不新增 SEMI 默认值，也
不改变 E37/E37.1 状态机。诊断覆盖不代表完整标准合规或设备认证结果。
