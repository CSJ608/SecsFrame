# 动态 SECS-II 消息

## 模型边界

<code>SecsMessage</code> 动态表达：

- Stream：0 至 127；
- Function：0 至 255；
- W-Bit：是否请求 secondary reply；
- RootItem：零个或一个根 Item。

它不要求预先注册 SxFy 模板，也不携带连接、会话或事务生命周期。
<code>HsmsDataMessage</code> 在外层增加 Session ID 和 System Bytes，
使同一个 <code>SecsMessage</code> 可以由后续事务层分配标识后发送。

没有 Body 使用 <code>RootItem = null</code>。编码后的零元素 List
<code>SecsItem.List()</code> 是一个实际存在的两字节 Item，不能当作
无 Body。

## HSMS Payload 编解码

~~~csharp
var message = new SecsMessage(
    stream: 1,
    function: 1,
    replyExpected: true,
    rootItem: SecsItem.List(
        SecsItem.Ascii("MDLN"),
        SecsItem.Ascii("SOFTREV")));

var envelope = new HsmsDataMessage(
    sessionId: 1,
    systemBytes: 0x01020304,
    message);

var codec = new HsmsDataMessageCodec();
var frame = codec.EncodeFrame(envelope);
~~~

<code>HsmsDataMessageCodec</code> 实现 StreamFrame 的
<code>ICodec&lt;HsmsDataMessage&gt;</code>，其输入输出是 HSMS Payload：
十字节消息头加可选的 SECS-II Item。四字节大端长度前缀属于
<code>HsmsFramer</code>。
<code>EncodeFrame</code> 与接收 <code>HsmsFrame</code> 的
<code>Decode</code> 重载供内部会话/事务路径复用相同严格 Item 规则，
同样不包含四字节长度前缀。

解码默认：

- 要求至少存在完整十字节头；
- 只接受 Data Message 的 SType 0；
- 只接受当前 SECS-II 映射使用的 PType 0；
- Body 非空时要求恰好一个完整根 Item；
- 沿用 <code>SecsItemCodec</code> 的深度和节点数上限；
- 支持跨任意 <code>ReadOnlySequence&lt;byte&gt;</code> 分段解码。

需要更小资源上限时，将配置后的 Item 编解码器注入消息编解码器：

~~~csharp
var codec = new HsmsDataMessageCodec(
    new SecsItemCodec(maxNestingDepth: 32, maxItemCount: 100_000));
~~~

## 事务集成

本层不判断某个 SxFy 是否由标准或设备支持，也不实现 primary/secondary
匹配、T3、System Bytes 分配、会话状态或重试。这些生命周期由独立的
内部 <code>HsmsDataTransactionManager</code> 组合，不进入不可变消息
模型或编解码器。事务层仍不要求预注册 SxFy，边界见
[HSMS-DATA-TRANSACTIONS.md](HSMS-DATA-TRANSACTIONS.md)。

W-Bit 与 Function 的完整 E5 业务约束、SxF0/S9Fx 和重试策略尚未实现；
Select 等控制消息继续由 <code>HsmsFrameCodec</code> 和 HSMS 会话状态机
处理。

实现追踪 SEMI E5-0725、E37-0222 和 E37.1-0819，但尚未使用完整标准和
一致性测试验证，因此不声明完整合规。标准版本与版权边界见
[STANDARDS.md](STANDARDS.md)。
