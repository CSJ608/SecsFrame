# 路线图

## 阶段 0：工程基线

- 仓库规范、CI、发布门禁和协议文档。
- HSMS 帧头、严格长度定界和黄金向量测试。
- 与 StreamFrame 之间的内部传输端口。

## 阶段 1：SECS-II 与 HSMS-SS 基础

- [x] 完整 SECS-II Item 类型、嵌套 List 和严格二进制编解码。
- [x] 动态 SecsMessage、可选单根 Item 与 HSMS Data Message Payload 编解码。
- [x] StreamFrame #38/#39 的内部会话绑定、写出确认和未完成帧超时适配。
- [x] Active/Passive 共用状态机的 Select/Select Response、Separate、
  T6/T7、System Bytes 关联和会话失效处理。
- [x] Linktest、Reject、Deselect 控制消息与对应 T6 分支。
- [x] Selected 数据发送、Primary/Secondary 关联、T3 与入站同事务回复。
- [x] 显式 T5 固定重连节流、T8 接收进展监视和跨会话旧回调隔离。
- [x] 最小公共连接外观、显式 T3/T5/T6/T7/T8 配置、动态消息事件与
  确定的取消/释放语义。
- [ ] 使用授权 E37/E37.1 核对 T5/T8 默认值与精确启停边界。
- Host、Equipment 双角色回环集成测试，以及与 nuget.org 官方 secs4net
  包的互操作测试。

阶段 1 的下一最小切片是跨实现互操作夹具：通过中央包管理固定
nuget.org 官方 secs4net 包版本，覆盖 Select、Linktest 和
Primary/Secondary。不得引用、构建或链接本地 secs4net 源码 checkout。
随后根据互操作结果收敛公共诊断模型。任何标准默认值和完整合规结论都
依赖团队合法获得的 E37/E37.1 副本与一致性测试。

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
