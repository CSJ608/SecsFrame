# GEM 基础行为

## 模块与范围

<code>SecsFrame.Gem</code> 是只依赖 <code>SecsFrame</code> 的独立程序集。
本切片提供 Host/Equipment 两侧的通讯建立、在线/离线、状态变量读取、
设备常量读取和应用托管时钟，不把 GEM 状态或消息目录放入 HSMS 状态机。

<code>GemHostServices</code> 依赖 <code>SecsHost</code>；
<code>GemEquipmentServices</code> 依赖 <code>SecsEquipment</code>。服务不拥有
角色端点，释放服务只移除它注册的 Primary 路由，端点仍由应用释放。

## 工程基线配置

<code>GemMessageProfile</code> 显式配置每个 Primary/Secondary 对、成功和
失败应答值以及时钟文本 codec。<code>CreateEngineeringBaseline()</code>
当前返回以下常见工程配置：

| 操作 | Primary | Secondary |
|---|---:|---:|
| 通讯建立 | S1F13 | S1F14 |
| 在线查询 | S1F1 | S1F2 |
| 请求在线 | S1F17 | S1F18 |
| 请求离线 | S1F15 | S1F16 |
| 状态变量读取 | S1F3 | S1F4 |
| 设备常量读取 | S2F13 | S2F14 |
| 时钟读取 | S2F17 | S2F18 |
| 时钟设置 | S2F31 | S2F32 |

这张表描述仓库自己的默认配置，不是复制的 SEMI 规范表，也不表示已经
核对 E30 的必选能力、状态条件、数据项定义或应答枚举。默认成功应答为
Binary 0，失败应答为 Binary 1；时钟使用 UTC 的
<code>yyyyMMddHHmmssff</code> ASCII 工程格式。现场差异应使用独立 profile
和 codec，不应修改严格基线或根据 Function 奇偶推断消息含义。

## 事件循环与状态

GEM 服务不会创建第二个事件消费者。应用必须把角色端点单消费者事件流的
每个事件交给服务；未匹配事件仍返回给应用处理：

~~~csharp
await foreach (var connectionEvent in equipment.GetEventsAsync(cancellationToken))
{
    if (!await gem.TryDispatchAsync(connectionEvent, cancellationToken))
    {
        await HandleApplicationEventAsync(connectionEvent, cancellationToken);
    }
}
~~~

完成或接受通讯建立后，<code>CommunicationState</code> 变为
<code>Communicating</code> 并记录 <code>PeerIdentity</code>。在线/离线请求
成功后，两侧分别更新本地或已观察的远端 <code>OnlineState</code>。收到
非 Selected 的状态事件后，这些状态会重置；因此只分派数据事件会遗漏
断线重置。

第一切片没有把变量、常量和时钟处理硬性门控在某个 GEM 状态上。这样可在
尚未获得授权 E30 状态表前避免把未经核对的状态条件固化为公共行为。

## 动态数据与时钟

Equipment 可按任意不可变 <code>SecsItem</code> 标识符运行期注册状态变量
和设备常量提供器。请求可以混用 U2、U4、ASCII 或厂商自定义标识类型，
响应按请求顺序返回提供器生成的动态 Item，值同样可以是嵌套 List。注册
对象可释放并替换，不需要预先生成完整设备消息、报告或事件目录。

<code>IGemClock</code> 把读时钟和设时钟交给应用。库不会修改操作系统时钟；
应用可以拒绝设置请求，Host 会收到包含操作和原始应答字节的
<code>GemRequestRejectedException</code>。

## 严格失败边界

当前实现严格要求：

- 基础 Primary 设置 W-Bit，请求体符合 profile 对应的空 Body、List、
  ASCII 或 Identity 结构；
- Secondary 的 Stream、Function 和 W-Bit 与配置完全匹配；
- 应答是单个 Binary 字节，接受的通讯建立应答包含两项 ASCII Identity；
- 状态变量和设备常量请求只引用已注册标识，提供器不得返回 null；
- 时钟字符串由显式 codec 完整解析。

畸形输入、未知标识和提供器失败会作为异常返回应用事件循环。本切片尚未
实现自动 S9Fx、错误 Secondary、通讯建立策略拒绝、状态切换策略或完整
GEM 错误恢复，这些行为不能从当前 API 推断为标准合规。

## 标准与验证边界

公开 SEMI 目录当前标识 GEM 基线为 E30-0526。仍需使用团队合法获得的
副本核对消息条件、COMMACK/ONLACK/OFLACK/TIACK 语义、MDLN 条件结构、
空 SVID/ECID 列表行为、时间格式选择、状态转换和错误响应。不得把标准
正文、消息表、状态图或 Schema 提交仓库。完整版本和版权边界见
[STANDARDS.md](STANDARDS.md)。

当前证据包括三目标框架的严格输入测试，以及 Host Active / Equipment
Passive 真实 TCP 下的双向通讯建立、上下线、异构动态标识、嵌套值、时钟
读写、拒绝应答和 Linktest。它们是独立工程验证，不是 SEMI 一致性认证。
