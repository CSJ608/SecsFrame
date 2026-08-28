# StreamFrame 内部适配

## 背景

SecsFrame 当前依赖 StreamFrame 2.2.0。以下上游能力仍在跟踪：

- [StreamFrame #38](https://github.com/CSJ608/StreamFrame/issues/38)：
  未完成帧超时；
- [StreamFrame #39](https://github.com/CSJ608/StreamFrame/issues/39)：
  会话感知的发送确认与消息上下文。

StreamFrame 现有 <code>SendAsync</code> 在消息进入连接级队列后完成，队列
会跨 TCP 重连保留；<code>GetMessages</code> 也是跨重连的稳定消息流。
这些语义适合通用业务连接，但不足以支撑 HSMS 的严格会话边界。因此
SecsFrame 在内部 <code>IHsmsTransport</code> 后提供临时适配，公共 API
不依赖 StreamFrame 的队列或原始字节回调。

## 会话事件

内部传输输出同一条有序事件流：

- <code>SessionOpened</code>：TCP Connected 后分配单调递增 Session ID；
- <code>FrameReceived</code>：携带 codec 解码时绑定的 Session ID；
- <code>SessionClosed</code>：会话失效，超时时附带原因。

接收帧不是在业务消费时读取“当前会话”，因此旧解码任务的迟到结果仍
保留旧 Session ID。状态机可以按 ID 丢弃已关闭会话的事件。
状态机还可以通过 <code>TryCloseSession</code> 只关闭匹配的当前 Session
ID，并把协议错误或 T6/T7 超时原因带入 <code>SessionClosed</code>。

## 发送确认与不重放

适配器的发送路径一次只允许一个待确认帧：

1. 入队前确认调用方 Session ID 仍是当前会话；
2. 信封进入 StreamFrame codec 时再次确认，关闭会话的信封编码失败；
3. 根据 <code>RawBytesSent</code> 每次实际 Socket 写出的字节数累计；
4. 四字节长度前缀、十字节头和 Body 全部写完后完成发送任务；
5. 会话先关闭时，以明确的会话失效异常完成等待任务。

因此会话层 T6 与事务层 T3 都从发送任务成功完成后启动。调用取消若发生
在进入 StreamFrame 队列之前会取消发送；一旦成功入队，适配器等待明确
的写完或会话失效结果，避免向调用方返回含糊状态。

旧信封即使因极窄竞态留在 StreamFrame 的跨重连队列中，也会在新会话
编码前失败，绝不会写入 Socket。当前 fail-closed 代价是该编码失败会让
StreamFrame 再重建一次连接；上游 #39 提供原生会话队列后可消除此代价。

## 未完成帧超时

适配器直接观察 StreamFrame 的原始接收分片，并独立跟踪 HSMS 四字节
大端长度前缀与剩余 Payload：

- 从未收到字节或完整帧后的空闲期不计时；
- 收到部分长度头或部分 Payload 后启动计时；
- 每次收到后续字节重新计时；
- 当前帧完整且没有下一帧残片时停止计时；
- 超时触发当前连接重建，并用 <code>TimeoutException</code> 关闭会话。

计时器通过内部工厂替换。测试使用手动计时器，不依赖真实等待，并覆盖
部分长度头、部分 Payload、进度重置、完整帧后空闲及完整帧后紧随下一
残片。真实 TCP 回环另行验证 StreamFrame 2.2.0 的回调与收发集成。

这项机制为后续 HSMS T8 提供必要能力，但具体 T8 取值、启动边界和标准
结论仍须依据团队合法获得的 SEMI E37/E37.1 版本及一致性测试核对。

## 替换条件

当 #38/#39 发布稳定 API 后：

1. 运行现有会话、发送确认、迟到消息和超时测试向量验证语义等价；
2. 用原生 Session ID、发送完成任务和未完成帧超时替换内部回调适配；
3. 删除旧信封 fail-closed 路径；
4. 保持 <code>IHsmsTransport</code> 及其上层状态机契约不变。
