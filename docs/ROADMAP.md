# 路线图

## 阶段 0：工程基线

- 仓库规范、CI、发布门禁和协议文档。
- HSMS 帧头、严格长度定界和黄金向量测试。
- 与 StreamFrame 之间的内部传输端口。

## 阶段 1：SECS-II 与 HSMS-SS 基础

- [x] 完整 SECS-II Item 类型、嵌套 List 和严格二进制编解码。
- [x] 动态 SecsMessage、可选单根 Item 与 HSMS Data Message Payload 编解码。
- Active/Passive HSMS-SS 状态机和 Select/Linktest/Separate/Reject 控制消息。
- T3/T5/T6/T7/T8、System Bytes 关联和会话失效处理。
- Host、Equipment 双角色回环集成测试，以及与 secs4net 的互操作测试。

阶段 1 的下一最小切片是完善内部 StreamFrame 传输适配器，提供未完成
帧超时、会话绑定发送确认和会话失效通知。它是 HSMS 状态机和事务层的
共同依赖。该切片继续跟踪 StreamFrame #38 与 #39；上游支持前由内部
端口提供等价语义，临时约定不进入公共 API。

## 阶段 2：GEM 通用能力

- 通讯建立、在线/离线状态、变量读取、设备常量和时钟。
- 报告定义、事件链接、Collection Event、报警和远程命令。
- 面向 Host 与 Equipment 的独立能力 API。

## 阶段 3：元数据与工具

- SML 调试读写。
- E173 SMN 日志与消息文档。
- E172 SEDD 设备字典导入、校验与发现。
- Trace 重放、脱敏和互操作诊断工具。

## 发布门槛

- `0.x`：API 和状态机仍可能调整，不声明生产就绪。
- `1.0`：标准版本追踪矩阵完成，协议测试、故障注入、互操作与长期运行测试通过。
