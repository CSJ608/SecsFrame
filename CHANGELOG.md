# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 和 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### 新增

- 初始化 SecsFrame 仓库、工程规范和 CI。
- 增加 HSMS 帧头、严格长度定界与编解码基础实现。
- 增加 SEMI E5-0725 的完整 SECS-II Item 动态数据模型与严格二进制编解码。
- 增加全部 Item 格式、长度边界、嵌套 List、分段输入、非法输入和往返协议向量测试。
- 增加动态 SecsMessage、HSMS Data Message 信封与可选单根 Item 的 Payload 编解码。
- 增加完整线上帧、无 Body、空 List、分段输入、非法 SType/PType 和消息往返测试。
- 增加 StreamFrame 内部会话适配器，提供单调 Session ID、会话失效通知和旧会话发送拒绝。
- 增加基于实际 Socket 写出分片的整帧发送确认，以及可替换计时器驱动的未完成帧超时。
- 增加发送竞态、迟到接收、超时重置和 StreamFrame 真实 TCP 回环测试。
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
- 增加按接收进展与会话代次隔离的 T8 监视、专用超时异常，以及部分长度
  头、部分 Payload、陈旧回调、替换会话和真实 TCP 超时测试。
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

[Unreleased]: https://github.com/CSJ608/SecsFrame/compare/main...HEAD
