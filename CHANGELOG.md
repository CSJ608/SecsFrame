# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 和 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### 修复

- 升级官方 StreamFrame 2.4.0，采用 Connected 发布点的会话 epoch 收口，
  并获得 Passive 接受重试出锁与监听端口立即重绑加固。
- 升级官方 StreamFrame 2.3.1，修复迟到旧会话故障污染活会话状态、旧会话
  绑定消息跨 Socket 错发，以及会话拆除时发送失败类型不稳定的问题。
- 串行化适配器的当前会话关闭与 transport 释放，避免已入队的迟到
  Separate 在销毁竞态中调用已经释放的 StreamFrame 生命周期。
- 增加关闭/释放并发状态测试和真实 TCP 会话替换回归测试，验证排队发送
  以会话失效结束且不会在新会话重放。

### 新增

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
- 增加定义、链接、S6F11 动态 Item 向量和真实 TCP 垂直测试，覆盖嵌套值、
  空报告、配置拒绝、触发前快照与 Host 拒绝。

[Unreleased]: https://github.com/CSJ608/SecsFrame/compare/main...HEAD
