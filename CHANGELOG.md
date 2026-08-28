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

[Unreleased]: https://github.com/CSJ608/SecsFrame/compare/main...HEAD
