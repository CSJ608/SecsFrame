# 路线图

## 阶段 0：工程基线

- 仓库规范、CI、发布门禁和协议文档。
- HSMS 帧头、严格长度定界和黄金向量测试。
- 与 StreamFrame 之间的内部传输端口。

## 阶段 1：SECS-II 与 HSMS-SS 基础

- [x] 完整 SECS-II Item 类型、嵌套 List 和严格二进制编解码。
- [x] 动态 SecsMessage、可选单根 Item 与 HSMS Data Message Payload 编解码。
- [x] 迁移到 StreamFrame 2.3.0 原生会话绑定、写出确认和未完成帧超时。
- [x] Active/Passive 共用状态机的 Select/Select Response、Separate、
  T6/T7、System Bytes 关联和会话失效处理。
- [x] Linktest、Reject、Deselect 控制消息与对应 T6 分支。
- [x] Selected 数据发送、Primary/Secondary 关联、T3 与入站同事务回复。
- [x] 显式 T5 固定重连节流、原生 T8 映射和跨会话故障隔离。
- [x] 最小公共连接外观、显式 T3/T5/T6/T7/T8 配置、动态消息事件与
  确定的取消/释放语义。
- [x] 使用 nuget.org 官方 Secs4Net 3.1.0 的 Active/Passive、Select、
  双方 Linktest 和双方 Primary/Secondary 跨实现测试。
- [x] 公共结构化诊断模型，覆盖运输、协议、会话、事务和解码故障。
- [x] 运行期 Primary 路由与可释放处理器注册，不依赖预生成消息类型。
- [ ] 使用授权 E37/E37.1 核对 T5/T8 默认值与精确启停边界。
- [x] Host、Equipment 双角色端点及两种 Active/Passive 拓扑的双向回环。

阶段 1 的工程能力已经形成完整基础链路，StreamFrame #38/#39 的正式包
迁移与等价测试已经完成。后续切片继续扩展 GEM 通用行为。任何标准默认值
和完整合规结论都依赖团队合法获得的 E37/E37.1 及相关 GEM 标准副本与
一致性测试。

## 阶段 2：GEM 通用能力

- [x] 独立 <code>SecsFrame.Gem</code> 程序集与可替换消息 profile。
- [x] 通讯建立、在线/离线状态、动态变量读取、设备常量和应用托管时钟。
- [x] 面向 Host 与 Equipment 的独立基础能力 API 和真实 TCP 垂直测试。
- [x] 报告定义、事件链接和 Collection Event。
- [ ] 报警、远程命令和更完整的 GEM 状态/错误恢复。
- [ ] 使用授权 E30-0526 核对基础消息条件、数据项、应答值和状态转换。

当前最小报告切片使用可替换工程 profile、完整配置集原子替换和动态
<code>SecsItem</code> 标识，不宣称已经核对 E30 的报告生命周期或应答
枚举。下一优先级是报警与远程命令，再扩展更完整的 GEM 状态和错误恢复。

## 阶段 3：元数据与工具

- SML 调试读写。
- E173 SMN 日志与消息文档。
- E172 SEDD 设备字典导入、校验与发现。
- Trace 重放、脱敏和互操作诊断工具。

## 发布门槛

- `0.x`：API 和状态机仍可能调整，不声明生产就绪。
- `1.0`：标准版本追踪矩阵完成，协议测试、故障注入、互操作与长期运行测试通过。
