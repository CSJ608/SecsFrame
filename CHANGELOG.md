# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 和 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### 修复

- 升级官方 StreamFrame 2.5.0，采用自适应发送编码缓冲，并获得 Passive
  接受循环代次门控，避免显式关闭与自动重连竞速泄漏监听器。
- 增加真实 TCP Passive 重连竞态测试，连续验证监听恢复、Session ID 单调
  递增和原会话精确关闭。
- 升级官方 StreamFrame 2.4.0，采用 Connected 发布点的会话 epoch 收口，
  并获得 Passive 接受重试出锁与监听端口立即重绑加固。
- 升级官方 StreamFrame 2.3.1，修复迟到旧会话故障污染活会话状态、旧会话
  绑定消息跨 Socket 错发，以及会话拆除时发送失败类型不稳定的问题。
- 串行化适配器的当前会话关闭与 transport 释放，避免已入队的迟到
  Separate 在销毁竞态中调用已经释放的 StreamFrame 生命周期。
- 增加关闭/释放并发状态测试和真实 TCP 会话替换回归测试，验证排队发送
  以会话失效结束且不会在新会话重放。

### 新增

- 增加独立 <code>SecsFrame-FaultSampleTrace/1</code> 信封，为公共
  <code>DataMessageDecodeFailed</code> 事件提供已成帧 HSMS data body 样本。
- 捕获必须显式选择 MetadataOnly、RedactedPayload 或 RawPayload；默认 codec
  拒绝 payload 导入导出，Redacted 范围固定清零并在严格读取时重新验证。
- 增加 64 KiB 默认 Body 上限、无截断失败、黄金向量、敏感字节移除、不可变
  复制、畸形范围/十六进制和资源限制测试；该信封不进入重放路径。
- 增加默认关闭的完整 HSMS 控制消息元数据观测流，覆盖内部 Select、
  Linktest、Deselect、Reject 与 Separate 的收发，而不改变现有业务事件流。
- 观测只保留方向、actor 观察状态与原十字节头；入站在协议处理前发布，
  出站仅在整帧写出确认后发布，并可直接创建控制 Trace 记录。
- 增加状态机顺序、未知 SType/Reject、消费者替换和 Active/Passive 真实
  TCP Select/Linktest 测试；原始字节、Body、transport generation 与时间戳
  仍不进入观测模型。
- 增加独立 <code>SecsFrame-ControlTrace/1</code> 信封，对公共未认领控制
  事件提供不含 Body 的十字节头元数据导出与严格读取。
- 控制记录保留方向、观察状态和原始头字段，允许未知非零 SType 往返；增加
  未匹配 Reject 黄金向量、Data Message 拒绝、畸形字段与资源限制测试。
- 增加独立 <code>SecsFrame-DiagnosticTrace/1</code> 信封，为公共
  <code>HsmsDiagnostic</code> 提供确定性受限字段快照导出与严格读取。
- 诊断快照只保留代码、层级、操作、状态、计时器和可选协议标量，明确排除
  原异常与未解码帧；增加黄金向量、畸形字段、资源限制和敏感内容不泄漏测试。
- 增加默认关闭的 Trace 时序重放；按 allowlist 筛选后的 Sent 记录时间戳
  计算间隔，支持显式速度倍率和缩放后单次等待封顶。
- 增加时序倒退预验证、筛选间隔、缩放/封顶、等待取消和默认零等待测试；
  时序路径仍通过公共发送 API 创建新事务，不复用旧协议标识。
- 增加独立 <code>SecsFrame.Trace</code> 程序集和版本化
  <code>SecsFrame-Trace/1</code> 信封，为已解码数据消息提供确定性导出与
  严格读取，并保留 UTC 时间、方向和可选 HSMS 诊断标识。
- 增加按 S/F 与 Item List 路径执行的结构化替换脱敏；路径漂移和重叠规则
  明确失败，避免敏感值因字符串替换或 Schema 变化而静默泄漏。
- 增加显式 allowlist 的受控重放；只重放 Sent 记录，忽略旧 Session ID、
  System Bytes、时间和回复令牌，通过公共发送 API 创建新事务。
- 增加 Trace 黄金向量、畸形信封、资源限制、脱敏泄漏、预验证和真实 TCP
  新事务测试。
- 增加独立 <code>SecsFrame.Sml</code> 程序集，为动态消息和全部 Item 类型
  提供确定性 SML 调试文本写出与严格读取。
- 增加 ASCII 可逆转义、JIS-8 原始十六进制字节、InvariantCulture 数字、
  无 Body/空 List 区分，以及深度、Item、值和文本长度资源边界。
- 增加 SML 黄金向量、全类型往返、非法语法、源位置和资源限制测试；该
  profile 不声明标准 SML 合规。
- 初始化 SecsFrame 仓库、工程规范和 CI。
- 增加 HSMS 帧头、严格长度定界与编解码基础实现。
- 增加 SEMI E5-0725 的完整 SECS-II Item 动态数据模型与严格二进制编解码。
- 增加全部 Item 格式、长度边界、嵌套 List、分段输入、非法输入和往返协议向量测试。
- 增加动态 SecsMessage、HSMS Data Message 信封与可选单根 Item 的 Payload 编解码。
- 增加完整线上帧、无 Body、空 List、分段输入、非法 SType/PType 和消息往返测试。
- 升级官方 StreamFrame 2.3.0，采用原生单调 Session ID、会话失效通知、
  会话绑定发送和未完成帧超时，删除 #38/#39 对应的临时回调适配。
- 发送任务由 StreamFrame 在整帧写入本机 Socket 后完成，旧会话消息不会
  跨重连重放；T8 FrameError 映射为保留 transport Session ID 的诊断。
- T8 配置改为要求可由正整毫秒精确表示且不超过 <code>int.MaxValue</code>
  毫秒；亚毫秒、截断和溢出输入在公共选项边界立即拒绝。
- 增加发送竞态、迟到接收、原生 T8 映射和 StreamFrame 真实 TCP 回环测试。
- 增加 Active/Passive 共用的内部 HSMS-SS Select/Separate 状态机，
  分离 TCP Connected、Selecting 与 Selected 状态。
- 增加从整帧实际写出后启动的 T6、连接建立后启动的 T7、System Bytes
  匹配、非法状态关闭和 Selected 数据门控。
- 增加并发 Select、选择拒绝、超时、非法控制头、Separate、旧计时器
  隔离及 Active/Passive 真实 TCP 握手测试。
- 增加本地 Linktest 与 Deselect 控制命令，响应使用 System Bytes
  关联且 T6 从整帧实际写出后启动。
- 增加 Linktest/Deselect 对端处理、Deselect 后 Connected/T7 转换和
  控制命令单飞约束。
- 增加 Reject Request 专用头模型，以及 Unsupported SType、Unsupported
  PType、Transaction Not Open 和 Entity Not Selected 的严格生成路径。
- 增加未被会话层消费的 Reject 事件转发、非法控制字段、控制事务中断和
  Active/Passive 真实 TCP 控制平面测试。
- 增加 Selected 门控的数据发送命令，发送任务仅在完整线上帧实际写出后
  完成，会话替换时不会重放。
- 增加内部 HSMS 数据事务 actor，为 W-Bit Primary 分配 System Bytes，
  使用 transport session、HSMS Session ID 与 System Bytes 关联
  Secondary，并从实际写出后启动独立 T3。
- 增加入站同事务回复、无 W-Bit 写出完成、Reject/Deselect/断线/取消/
  畸形 Secondary 处理，以及未匹配数据与解码失败事件。
- 增加并发事务、嵌套 Item 往返、迟到响应、复合键隔离、一次性回复和
  Active/Passive 双端真实 TCP 数据事务测试。
- 增加显式 HSMS T5/T8 运输配置；Active 连接把 T5 无损映射为固定
  StreamFrame 重试间隔并关闭指数退避，Passive 监听重试保持独立。
- 增加 StreamFrame 原生接收进展 T8、专用超时异常及关闭原因关联，覆盖
  部分长度头、替换会话和真实 TCP 超时测试。
- 增加公共 <code>HsmsConnection</code> 与不可变
  <code>HsmsConnectionOptions</code>，组合动态消息收发、Selected 等待、
  单消费者事件流、入站一次性回复和控制命令。
- 增加公共 API 选项边界、生命周期、等待/事务取消和 Active/Passive
  双端真实 TCP 往返测试；入站回复令牌绑定原连接与原运输会话。
- 增加独立的官方 <code>Secs4Net 3.1.0</code> NuGet 互操作测试项目，
  由中央包管理固定版本且只从 nuget.org 还原，不引用本地源码 checkout。
- 增加双方 Active/Passive、Select、双方 Linktest、双方
  Primary/Secondary 及嵌套 Item 边界值的真实 TCP 跨实现测试。
- 增加公共 <code>HsmsDiagnostic</code> 结构化诊断模型，在保留原异常的
  同时分类运输、协议、会话、事务、T3/T6/T7/T8 和解码故障。
- 连接状态与解码失败事件公开可选诊断；操作异常可显式分类，调用方取消、
  释放和生命周期误用不会被误报为协议故障。
- 增加运行期 <code>HsmsPrimaryRouter</code>，按 Stream/Function 动态注册
  处理器并在原事务上自动回复，无需预生成消息类型或完整消息目录。
- 未匹配连接事件继续由应用处理；处理器注册可释放替换，异常、取消和
  W-Bit 契约错误明确传播。
- 增加拥有连接与动态路由生命周期的 <code>SecsHost</code> 和
  <code>SecsEquipment</code> 公共端点，角色与 Active/Passive 保持正交。
- 增加 Host Active/Equipment Passive 及反向拓扑的双向 Primary/Secondary
  真实 TCP 回环测试。
- 增加独立 <code>SecsFrame.Gem</code> 程序集、可替换
  <code>GemMessageProfile</code>、Host/Equipment 基础服务和显式 GEM 状态。
- 增加通讯建立、上下线、运行期状态变量/设备常量提供器和应用托管时钟，
  严格校验 W-Bit、消息体、Secondary、应答值、未知标识与时钟输入。
- 增加 Host Active/Equipment Passive 的 GEM 真实 TCP 垂直测试，覆盖双向
  通讯建立、动态嵌套值、时钟读写、拒绝应答和 Linktest。
- 增加可替换的报告定义、事件链接和 Collection Event 消息对，Host 可
  原子替换完整配置，Equipment 可按运行期状态变量快照采集并发送报告。
- 增加不可变动态报告/事件模型和可释放的 Host Collection Event 处理器；
  无处理器或应用拒绝返回显式失败应答。
- 报告配置严格拒绝畸形 Item、重复标识、未知变量和未知报告，成功配置在
  应答写出前原子可见，替换报告集会移除引用已删除报告的旧事件链接。
- 增加可替换的报警通知消息对、不可变动态报警模型、Equipment 发送 API
  和可释放的 Host 单处理器注册；报警代码按原始字节透传，不推断位语义。
- 报警通知严格校验 W-Bit、三字段正文、单字节 Binary 代码、ASCII 文本、
  Secondary 和应答值，并增加协议向量及真实 TCP 接受/拒绝测试。
- 增加可替换的远程命令消息对、不可变动态命令/参数/结果模型、Host 发送
  API 和可释放的 Equipment 单处理器注册；整体与参数结果码原样返回。
- 远程命令严格校验 W-Bit、二字段请求/回复、参数形状、名称唯一性和
  Binary 结果码，并增加动态嵌套 Item、无处理器拒绝及真实 TCP 往返测试。
- 增加可释放的 Equipment 远程命令接受策略；应用可依据当前通讯状态、
  在线状态和已解码命令决定是否交给执行处理器，未注册策略时保持现有行为。
- 策略拒绝使用 profile 失败应答和空参数结果，且不调用执行处理器；应用
  改变决策后可在同一会话重试。增加注册生命周期和真实 TCP 状态门控测试。
- 增加可释放的 Equipment Collection Event 发送策略；应用可依据当前
  通讯/在线状态、DATAID 和 CEID 决定是否采值并发送，未注册时保持现有行为。
- Collection Event 策略拒绝在状态变量提供器和 S6F11 事务之前短路；应用
  改变决策后可在同一会话显式重试。增加注册生命周期和真实 TCP 状态测试。
- 增加可释放的 Equipment 报警通知发送策略；应用可依据当前通讯/在线状态
  和完整通知决定是否发送，未注册时保持现有行为。
- 已注册报警的发送启停检查优先于应用策略；策略拒绝在 S5F1 事务之前短路，
  改变决策后可在同一会话显式重试。增加注册生命周期和真实 TCP 状态测试。
- 增加 Equipment 运行期远程命令精确目录；应用可按动态
  <code>SecsItem</code> 标识注册多个可释放处理器。
- 命令分派优先使用精确目录项，未匹配时回退现有单一全局处理器；两者都
  不存在时保持失败结果且不调用接受策略。增加注册生命周期和真实 TCP 测试。
- 增加远程命令精确目录项的运行期可用性；新项默认允许执行，应用可原子
  停用或恢复单个动态命令。
- 已匹配但禁用的精确项使用失败结果短路，不调用接受策略、精确处理器或
  全局回退；重新启用后支持同一 Selected 会话显式重试。增加状态机测试。
- 增加可替换的报警目录查询消息对、不可变报警定义、Equipment 运行期注册
  和 Host 全量/按动态标识查询；全量目录保持注册顺序，未知标识被忽略。
- 报警目录严格校验 W-Bit、请求 List、标识唯一性、三字段定义、单字节
  Binary 代码与 ASCII 文本，并增加注册释放快照和真实 TCP 查询测试。
- 增加可替换的单报警发送启停消息对与控制码 codec；Host 可控制已注册报警，
  Equipment 原子更新注册状态，未知标识使用失败应答拒绝。
- 已注册报警默认允许发送；禁用后通知发送入口按调用时快照阻断该标识，目录
  保持可见，释放并重新注册会恢复默认状态。增加严格协议向量和真实 TCP 测试。
- 增加可释放的 Equipment 在线状态转换处理器；应用可依据当前状态和请求
  状态接受或拒绝 Host 在线/离线请求，未注册处理器时保持自动接受。
- 拒绝使用 profile 失败应答且双方状态保持不变；接受仍在 Secondary 完整
  写出后更新 Equipment 状态。增加注册生命周期和真实 TCP 状态测试。
- 增加 Host/Equipment 双向可释放通讯建立策略；应用可依据请求方身份接受
  或拒绝 S1F13，未注册策略时保持自动接受。
- 拒绝使用 profile 失败应答和空身份 List，双方既有通讯状态与对端身份均
  保持不变；应用随后可在同一 Selected 会话显式重试恢复。增加双向注册
  生命周期、首次建立拒绝/恢复和已通讯重建拒绝/恢复的真实 TCP 状态测试。
- 增加 Host/Equipment 对称的显式通讯恢复入口；每次调用等待当前或下一次
  Selected 后只发起一次既有通讯建立事务，不创建后台重试循环。
- 增加真实 TCP 断线恢复测试，覆盖 Separate 后状态/身份清空、等待取消零
  迟到事务、拒绝后显式重试，以及恢复成功不隐式恢复在线状态。
- 增加定义、链接、S6F11 动态 Item 向量和真实 TCP 垂直测试，覆盖嵌套值、
  空报告、配置拒绝、触发前快照与 Host 拒绝。

[Unreleased]: https://github.com/CSJ608/SecsFrame/compare/main...HEAD
