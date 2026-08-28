# Host 与 Equipment 端点

## 角色与连接模式正交

<code>SecsHost</code> 和 <code>SecsEquipment</code> 表达 SECS 应用角色；
<code>HsmsConnectionMode.Active</code> 与 <code>Passive</code> 只表达哪一端建立
TCP 连接。库不从其中一个推断另一个。

当前真实 TCP 回环持续验证以下两种常见拓扑：

| Host | Equipment | 已验证方向 |
|---|---|---|
| Active | Passive | Host Primary/Equipment Secondary；Equipment Primary/Host Secondary |
| Passive | Active | Host Primary/Equipment Secondary；Equipment Primary/Host Secondary |

这两种连接方向具有相同的动态消息和事务能力。角色不会改变 Select、T3、
T5、T6、T7、T8、Linktest 或运输重连语义。

## 端点所有权

每个角色端点拥有一个 <code>HsmsConnection</code> 和一个
<code>HsmsPrimaryRouter</code>，并公开以下组合能力：

- Start、等待 Selected、读取状态和单消费者事件流；
- 发送动态 Primary、手工回复未匹配 Primary；
- 运行期注册与分派 Primary 处理器；
- Linktest、Deselect 和 Separate；
- 异步释放整个端点。

释放角色端点会释放其连接。角色在构造后固定，连接方向、地址、协议
Session ID 和计时器仍通过显式 <code>HsmsConnectionOptions</code> 提供。

~~~csharp
await using var equipment = new SecsEquipment(
    new HsmsConnectionOptions(
        IPAddress.Any,
        port: 5000,
        HsmsConnectionMode.Passive,
        sessionId: 0,
        t3,
        t5,
        t6,
        t7,
        t8));

using var route = equipment.RegisterPrimaryHandler(
    stream: 1,
    function: 1,
    HandleAreYouOnlineAsync);

equipment.Start();
await equipment.WaitUntilSelectedAsync(cancellationToken);
~~~

以上只展示 API 组合，消息含义和计时器值不是标准推荐或默认值。

## 能力边界

基础角色端点故意保持对称：Host 和 Equipment 都可能发起 Primary，也都
可能处理对端 Primary。独立 GEM 层通过依赖具体的
<code>SecsHost</code> 或 <code>SecsEquipment</code> 类型提供不同业务能力，
而不是修改 HSMS 状态机或根据 Active/Passive 猜测角色。

当前 GEM 基础切片已实现工程配置下的通讯建立、在线状态、变量、设备常量
和时钟；报告、事件、报警和远程命令仍未实现。相关行为、事件循环要求和
未核对边界见 [GEM-FOUNDATION.md](GEM-FOUNDATION.md)，项目不声明 GEM 合规。
