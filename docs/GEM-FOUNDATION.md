# GEM 基础行为

## 模块与范围

<code>SecsFrame.Gem</code> 是只依赖 <code>SecsFrame</code> 的独立程序集。
当前切片提供 Host/Equipment 两侧的通讯建立及应用接受策略、在线/离线及
应用转换策略、状态变量读取、设备常量读取、应用托管时钟、报告定义、事件
链接和带应用发送策略的 Collection Event，以及报警目录查询、单报警发送
启停、最小报警通知、远程命令链路与应用状态接受策略，不把 GEM 状态或消息
目录放入 HSMS 状态机。

<code>GemHostServices</code> 依赖 <code>SecsHost</code>；
<code>GemEquipmentServices</code> 依赖 <code>SecsEquipment</code>。服务不拥有
角色端点，释放服务只移除它注册的 Primary 路由，端点仍由应用释放。

## 工程基线配置

<code>GemMessageProfile</code> 显式配置每个 Primary/Secondary 对、成功和
失败应答值、时钟文本 codec 以及报警发送启停控制码 codec。
<code>CreateEngineeringBaseline()</code> 当前返回以下常见工程配置：

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
| 报告定义 | S2F33 | S2F34 |
| 事件链接 | S2F35 | S2F36 |
| Collection Event | S6F11 | S6F12 |
| 报警通知 | S5F1 | S5F2 |
| 报警发送启停 | S5F3 | S5F4 |
| 报警目录查询 | S5F5 | S5F6 |
| 远程命令 | S2F41 | S2F42 |

这张表描述仓库自己的默认配置，不是复制的 SEMI 规范表，也不表示已经
核对 E30 的必选能力、状态条件、数据项定义、报告生命周期或应答枚举。
默认成功应答为 Binary 0，失败应答为 Binary 1；报警发送允许/禁止码分别
为 Binary <code>0x80</code>/<code>0x00</code>；时钟使用 UTC 的
<code>yyyyMMddHHmmssff</code> ASCII 工程格式。这些值是非规范性工程
基线，仍需使用授权 E30-0526 与目标设备核对。现场差异应使用独立 profile
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

Host 与 Equipment 都可使用
<code>RegisterCommunicationEstablishmentHandler</code> 注册单一可释放
策略。每个结构有效的对端通讯建立请求都会把请求方
<code>GemIdentity</code> 交给处理器。返回 <code>false</code> 时服务发送
profile 的失败应答和空身份 List，发起方抛出包含操作和原始应答字节的
<code>GemRequestRejectedException</code>。双方均不更新
<code>CommunicationState</code> 或 <code>PeerIdentity</code>；如果请求是
已通讯状态下的重新建立，既有状态和身份也会保留。

返回 <code>true</code> 时服务先完整写出成功 Secondary，再记录请求方身份
并进入 <code>Communicating</code>。没有注册策略时继续自动接受，保持此前
行为。应用可以改变策略结果并在同一 Selected 会话再次调用
<code>EstablishCommunicationAsync</code>；库不自动重试、不创建计时器或
后台恢复循环。

Equipment 可使用 <code>RegisterOnlineStateTransitionHandler</code> 注册
单一可释放策略。每个结构有效的 Host 在线/离线请求都会把当前状态和目标
状态交给处理器；返回 <code>false</code> 时 Equipment 发送 profile 的失败
应答且保持当前状态，Host 抛出包含操作和原始应答字节的
<code>GemRequestRejectedException</code>，不会更新观察状态。返回
<code>true</code> 时仍先完整写出成功 Secondary，再更新 Equipment 状态。

这两类策略没有注册处理器时都继续自动接受。处理器在服务锁外执行；释放
注册只影响后续请求，已经取得的处理器快照会完成。处理器异常和取消继续
传播给事件循环，不会被折叠为失败应答。同状态在线请求也会交给处理器。
应用应遵守角色端点的单消费者事件循环约束，使决策和状态转换按请求顺序
执行。

除前述通讯建立/在线转换策略外，后文还为远程命令、Collection Event 和
报警通知提供显式应用策略。当前仍不把变量读取、常量、时钟等其他操作硬性
门控在某个 GEM 状态上，也不自动重新建立通讯。这样可在尚未获得授权 E30
状态表前避免把未经核对的状态条件固化为公共行为。

## 动态数据与时钟

Equipment 可按任意不可变 <code>SecsItem</code> 标识符运行期注册状态变量
和设备常量提供器。请求可以混用 U2、U4、ASCII 或厂商自定义标识类型，
响应按请求顺序返回提供器生成的动态 Item，值同样可以是嵌套 List。注册
对象可释放并替换，不需要预先生成完整设备消息、报告或事件目录。

<code>IGemClock</code> 把读时钟和设时钟交给应用。库不会修改操作系统时钟；
应用可以拒绝设置请求，Host 会收到包含操作和原始应答字节的
<code>GemRequestRejectedException</code>。

## 报告与 Collection Event

Host 使用 <code>DefineReportsAsync</code> 和
<code>LinkEventReportsAsync</code> 下发完整配置集。报告由动态 RPTID 和
有序状态变量标识组成；事件链接由动态 CEID 和有序 RPTID 组成。标识继续
使用不可变 <code>SecsItem</code>，不要求预生成消息或事件目录。

当前工程语义是完整集替换：

- 空报告集清除全部报告；替换报告集同时移除引用已删除报告的旧事件链接；
- 空链接集清除全部链接；空 RPTID 列表保留一个不携带报告的事件；
- 报告只引用当前已注册的状态变量，链接只引用当前已定义的报告；
- 未知变量或报告返回失败应答且不提交；畸形或重复标识作为协议异常上报；
- 校验成功的配置在发送成功应答前于 Equipment 锁内原子提交，因此 Host
  收到成功应答时配置已经可见。

<code>SendCollectionEventAsync</code> 先在锁内快照事件链接、报告定义和
状态变量提供器，再在锁外按链接/定义顺序异步采值。并发替换配置或释放
注册不会改变正在生成的这一条事件；提供器仍可能按应用取消或失败。值保持
任意动态 Item，包括嵌套 List 和空报告。

Equipment 可另用 <code>RegisterCollectionEventSendPolicyHandler</code>
注册单一可释放发送策略。配置有效的每次发送会把当前
<code>CommunicationState</code>、<code>OnlineState</code>、DATAID 和
CEID 交给策略。返回 <code>false</code> 时
<code>SendCollectionEventAsync</code> 抛出本地
<code>InvalidOperationException</code>；状态变量提供器不会执行，也不会
创建 S6F11 事务，因此 Host 不会收到消息。

没有注册策略时继续采值并发送，保持此前行为。策略、状态和报告执行计划在
Equipment 锁内一起快照，策略在锁外执行；释放注册只影响后续发送，已取得
的快照会完成。策略异常和取消直接传播给调用方。应用可以改变策略结果并在
同一 Selected 会话再次显式调用发送；库不自动重试，也不内置未经授权
E30-0526 核对的通讯/在线状态矩阵。

Host 使用 <code>RegisterCollectionEventHandler</code> 注册单一可释放处理器。
处理器返回 <code>true</code> 发送成功应答，返回 <code>false</code> 发送失败
应答；没有处理器时明确失败。释放注册只影响后续事件，已经取得处理器快照
的分派会完成。应用处理器异常和取消继续传播给事件循环，不被库隐藏。

## 报警通知与目录

Equipment 使用 <code>GemAlarmNotification</code> 提供一个精确 Binary 代码
字节、动态 <code>SecsItem</code> 报警标识和七位 ASCII 文本，再通过
<code>SendAlarmNotificationAsync</code> 发送。库原样保留代码字节，不解释
其中的 set/clear、类别或厂商位；这些语义在取得授权 E30 副本并完成设备
互操作核对前由应用负责。

Host 使用 <code>RegisterAlarmNotificationHandler</code> 注册单一可释放
处理器。处理器返回 <code>true</code> 发送成功应答，返回
<code>false</code> 或没有处理器时发送失败应答。注册快照、释放、异常和
取消语义与 Collection Event 处理器一致。本切片不解释报警条件状态，也
不包含历史或持久化。Equipment 发送策略只向应用暴露当前状态，不内置
基于 GEM 在线状态的矩阵。

Equipment 使用 <code>RegisterAlarm</code> 注册不可变
<code>GemAlarmDefinition</code>。报警定义保留原始单字节代码、动态报警
标识和七位 ASCII 文本；标识必须唯一。注册对象默认允许发送，并通过
<code>IsSendEnabled</code> 暴露当前发送许可。注册对象可精确释放并重新
注册，全量目录按当前注册顺序返回。

Host 使用 <code>ListAlarmsAsync</code> 查询目录。省略标识或传入空列表会
取得全量快照；传入唯一标识列表时按请求顺序返回已注册的匹配项，未知标识
被忽略。查询回复不携带额外失败应答，因此未知标识不会被转换为 T3 超时。
请求和回复都是一次事务快照；并发注册或释放只影响后续查询。

Host 使用 <code>SetAlarmSendEnabledAsync</code> 控制一个已注册报警。
请求是控制码和动态报警标识组成的二元素 List；控制码由
<code>GemAlarmControlCodec</code> 严格映射。Equipment 在构造成功应答前
原子更新注册状态，未知标识使用 profile 的失败应答拒绝。

禁用只阻断当前注册 ID 后续进入
<code>SendAlarmNotificationAsync</code> 的调用，不修改报警定义、目录或
通知代码位，也不改变报警条件。发送许可在调用入口取得一次快照；已经通过
检查的并发发送会继续完成。未注册 ID 的原始通知保持既有发送行为。释放并
重新注册会恢复默认允许状态，HSMS 会话变化不会重置注册状态。

Equipment 可另用
<code>RegisterAlarmNotificationSendPolicyHandler</code> 注册单一可释放
发送策略。每个通过上述单报警发送许可检查的通知会把当前
<code>CommunicationState</code>、<code>OnlineState</code> 和完整
<code>GemAlarmNotification</code> 交给策略。返回 <code>false</code>
时 <code>SendAlarmNotificationAsync</code> 抛出本地
<code>InvalidOperationException</code>，不创建 S5F1 事务，因此 Host
不会收到消息。已禁用的已注册报警在策略之前拒绝，策略不会执行。

没有注册策略时继续发送，保持此前行为。发送许可、策略与状态在 Equipment
锁内按调用时一起快照，策略在锁外执行；释放注册只影响后续发送，已取得的
策略快照会完成。策略异常和取消直接传播给调用方。应用可以改变策略结果并
在同一 Selected 会话显式重试；库不自动重试，也不内置未经授权 E30-0526
核对的通讯/在线状态矩阵。

当前 API 故意不提供“全部报警”启停、批量控制、报警条件屏蔽或历史。
这些语义以及控制码含义仍需依据授权 E30-0526 和目标设备互操作结果核对。

## 远程命令

Host 使用 <code>GemRemoteCommand</code> 携带动态命令标识和有序参数，再
通过 <code>ExecuteRemoteCommandAsync</code> 发起请求。命令、参数名称和
参数值均保持为任意非 null <code>SecsItem</code>；参数名称必须唯一，值可
包含嵌套 List。库不把现场命令限制为预生成枚举或 ASCII 字符串。

Equipment 使用 <code>RegisterRemoteCommand</code> 按不可变
<code>SecsItem</code> 命令标识注册多个精确运行期处理器；返回的
<code>GemRemoteCommandEntryRegistration</code> 暴露精确标识和
<code>IsExecutionEnabled</code>，可用
<code>SetExecutionEnabled(bool)</code> 在运行期启停，也可释放。同一结构
相等标识不能重复注册，新注册项默认允许执行。应用还可使用
<code>RegisterRemoteCommandHandler</code> 注册单一全局回退处理器。
分派优先选择精确处理器，未匹配时才选择全局回退；两者都不存在时 Equipment
使用 profile 的失败应答值和空参数结果立即回复。

已匹配但禁用的精确项表示“已知但当前不可执行”，同样立即返回失败结果；
它不会调用精确处理器、接受策略或全局回退，避免停用命令经回退路径绕过。
应用重新启用后可在同一 Selected 会话显式重试。释放精确项后，该标识才
重新成为未匹配命令并可进入全局回退；重新注册的新项恢复默认启用状态。

选中的处理器返回 <code>GemRemoteCommandResult</code>，其中包含一个原始
整体完成码和有序参数结果；每个参数结果保留动态名称与原始单字节代码。
Host 返回完整结果，不把非零代码折叠成异常，也不解释代码枚举。

Equipment 可另用 <code>RegisterRemoteCommandAcceptanceHandler</code>
注册单一可释放接受策略。存在执行处理器时，每个结构有效的请求先把当前
<code>CommunicationState</code>、<code>OnlineState</code> 和已解码命令
交给策略。返回 <code>false</code> 时 Equipment 使用 profile 的失败应答值
和空参数结果回复，执行处理器不会被调用，双方 GEM 状态不变；Host 仍返回
原始 <code>GemRemoteCommandResult</code>，不会把非零结果转换为异常。

没有注册接受策略时命令直接进入选中的执行处理器，保持此前行为。没有执行
处理器或精确项已禁用时请求立即失败，接受策略不会执行。接受策略、精确项
可用性、精确/回退选择和当前状态在 Equipment 锁内按请求一起快照，并在
锁外按策略、执行顺序调用；启停或释放注册只影响后续请求，已取得的快照会
完成。策略或执行处理器的异常和取消继续传播给事件循环，不会被折叠为失败
应答。

当前切片只要求请求参数名和结果参数名各自唯一，不推断结果必须覆盖哪些
请求参数，也不内置任何通讯/在线状态矩阵。运行期目录只负责应用侧精确
分派和本地可用性，不新增 E30 消息、不提供 Host 目录查询，也不包含参数
Schema、角色/用户权限、排队、自动重试、幂等策略或执行历史。

## 严格失败边界

当前实现严格要求：

- 基础 Primary 设置 W-Bit，请求体符合 profile 对应的空 Body、List、
  ASCII 或 Identity 结构；
- Secondary 的 Stream、Function 和 W-Bit 与配置完全匹配；
- 通讯建立回复是确认字节与身份组成的二元素 List；接受时必须包含两项
  ASCII Identity，策略拒绝时使用 profile 失败确认和空身份 List；
- 在线/离线请求必须设置 W-Bit 且 Body 为空；应用拒绝使用 profile 失败
  应答，接受应答写出完成前不得改变 Equipment 在线状态；
- 状态变量和设备常量请求只引用已注册标识，提供器不得返回 null；
- 时钟字符串由显式 codec 完整解析；
- 报告/链接请求是二元素 List，Collection Event 是三元素 List，各嵌套项
  的标识和值列表形状必须完整且标识不能产生歧义；
- 报警通知是三元素 List，代码必须是恰好一个 Binary 字节，文本必须为
  ASCII，报警标识保持为任意非 null 动态 Item；
- 报警目录请求是唯一动态标识组成的 List，空 List 表示全量查询；回复是
  报警定义 List，每项必须包含单字节 Binary 代码、唯一标识和 ASCII 文本；
- 报警发送启停请求是二元素 List，控制码必须是恰好一个 Binary 字节且为
  profile 配置的允许或禁止码；未知报警标识使用失败应答拒绝；
- 远程命令请求是命令标识和参数列表组成的二元素 List，每个参数是名称与
  值组成的二元素 List；回复是整体单字节代码和参数结果列表组成的二元素
  List，每个参数结果必须包含唯一名称和单字节 Binary 代码；
- 报告配置请求和 Collection Event 必须设置 W-Bit，Secondary 的消息对和
  应答字节必须与 profile 匹配；报警通知遵循相同要求；
- 远程命令必须设置 W-Bit 且 Secondary 消息对完全匹配；整体和参数结果码
  保持原始字节，不限制为 profile 的成功/失败两个值；接受策略拒绝时使用
  profile 失败值和空参数结果，且不得调用执行处理器；已禁用精确项也使用
  相同失败结果，并且不得调用接受策略、精确处理器或全局回退。

畸形输入和提供器失败会作为异常返回应用事件循环；语义有效但引用未知变量
或报告的配置使用失败应答拒绝。本切片尚未实现自动 S9Fx、错误 Secondary、
自动重新建立、报警历史/批量控制、完整命令权限/参数 Schema/调度或完整
GEM 错误恢复，这些行为不能从当前 API 推断为标准合规。

## 标准与验证边界

公开 SEMI 目录当前标识 GEM 基线为 E30-0526。仍需使用团队合法获得的
副本核对消息条件、COMMACK/ONLACK/OFLACK/TIACK 语义、MDLN 条件结构、
空 SVID/ECID 列表行为、报告定义与链接生命周期、Collection Event 数据
结构和应答、报警通知/目录/发送启停结构、代码位与控制码语义、时间格式
选择、状态转换和错误响应，以及远程命令结构、整体/参数结果代码语义。
不得把标准正文、
消息表、状态图或 Schema 提交仓库。完整版本和版权边界见
[STANDARDS.md](STANDARDS.md)。

当前证据包括三目标框架的严格输入测试，以及 Host Active / Equipment
Passive 真实 TCP 下的双向通讯建立、首次建立拒绝后同会话恢复、已通讯
重新建立拒绝时的状态/身份保持与恢复、在线转换接受/拒绝与状态保持、异构
动态标识、报告定义、
事件链接、Collection Event 嵌套值与空报告、应用发送策略拒绝零采值/零
事务和同会话重试、报警通知接受/拒绝、应用发送策略拒绝零事务与同会话
重试、已禁用报警优先拒绝、配置/事件拒绝、报警目录全量/选择查询与释放
快照、单报警发送启停与未知标识拒绝、远程命令动态参数与结果、精确目录
优先/全局回退/释放恢复、精确项禁用无回退绕过和重新启用后的同会话重试、
无处理器拒绝、应用状态拒绝短路与同会话重试、时钟读写和 Linktest。它们
是独立工程验证，不是 SEMI 一致性认证。
