# 动态 Primary 路由

## 目标

<code>HsmsPrimaryRouter</code> 按运行期注册的 Stream/Function 处理动态
<code>SecsMessage</code>。它不要求预生成消息类型、完整消息目录、报告定义
或事件定义；应用可以从配置、设备字典或启动代码逐步注册自己实际使用的
消息。

路由器不创建后台读取任务，也不抢占
<code>HsmsConnection.GetEventsAsync</code> 的单消费者。应用在已有事件循环
中显式调用 <code>TryDispatchAsync</code>，未匹配事件返回
<code>false</code>，仍可由应用按原方式处理。

## 注册与回复

~~~csharp
var router = new HsmsPrimaryRouter(connection);
using var s6f11 = router.Register(
    stream: 6,
    function: 11,
    static (context, cancellationToken) =>
    {
        var eventData = context.Message.RootItem;
        ProcessCollectionEvent(eventData);

        return new ValueTask<SecsMessage?>(
            new SecsMessage(
                stream: 6,
                function: 12,
                rootItem: SecsItem.Boolean(true)));
    });

await foreach (var connectionEvent in connection.GetEventsAsync(cancellationToken))
{
    if (await router.TryDispatchAsync(connectionEvent, cancellationToken))
        continue;

    await HandleUnmatchedEventAsync(connectionEvent, cancellationToken);
}
~~~

处理器返回 Secondary 时，路由器调用现有
<code>HsmsConnection.ReplyAsync</code>，因此回复自动继承原协议 Session ID、
System Bytes 和运输会话绑定，并继续受一次性回复约束。返回
<code>null</code> 表示有意不回复。无 W-Bit 的消息返回 Secondary 会立即
失败；返回消息自身设置 W-Bit 也会由连接的 Secondary 校验拒绝。

<code>HsmsPrimaryContext</code> 提供动态消息、协议 Session ID、System
Bytes、W-Bit 和原始一次性入站令牌。处理器异常与调用方取消直接传播给
事件循环，库不隐藏、记录或转换业务异常。

## 动态更新与并发

每个精确 Stream/Function 同一时刻只允许一个注册。重复注册会失败；释放
<code>HsmsPrimaryRouteRegistration</code> 后可以注册替代处理器。分派在锁内
只取得处理器快照，实际业务代码在锁外执行，所以运行中的处理器可以与其它
路由注册、移除并发。释放只影响后续分派，已经取得快照的调用会完成本次
处理。

路由器把所有尚未被事务管理器消费、且与注册键匹配的数据消息视为候选。
HSMS 头本身没有一个可供通用层可靠判断“这是未匹配 Secondary 还是无
W-Bit Primary”的额外标志，因此应用只应注册自己拥有的 Primary
Stream/Function。路由器不根据 Function 奇偶或设备角色做隐式推断。

## 验证边界

真实 TCP 测试证明运行期处理器能读取动态嵌套 Item，并在原事务上自动
回复；另有测试覆盖未匹配事件、重复/替代注册、异常、取消和无 W-Bit
错误。该路由机制不定义任何 GEM 消息含义，也不据此声明 E30 合规。
