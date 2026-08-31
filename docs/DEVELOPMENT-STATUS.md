# 开发状态与接续

本文记录当前可执行队列、外部阻塞和恢复开发所需的最小上下文，方便在新的
开发会话中继续工作。它不替代 [路线图](ROADMAP.md)、标准追踪或各专题设计
文档；当依赖状态或优先级改变时，应随对应 Pull Request 一起更新本文。

## 接续入口

新的开发会话应先完整阅读以下内容，再检查相关源码、测试、Git 历史和
GitHub Issue/Actions：

1. 根目录 `AGENTS.md` 与 `README.md`。
2. 本文、[路线图](ROADMAP.md) 与 [标准追踪](STANDARDS.md)。
3. 当前任务对应的专题文档；运输故障工作重点阅读
   [StreamFrame 适配边界](STREAMFRAME-ADAPTER.md)、
   [HSMS 诊断](HSMS-DIAGNOSTICS.md) 和 [Trace](TRACE.md)。

开始工作前从最新 `main` 创建符合 `AGENTS.md` 命名规则的主题分支。不要把
本文记录的提交号当成永久分支基线。

## 当前基线

截至 2026-08-31，本切片在 StreamFrame 2.6.0 与独立 session soak 基线上
继续开发，当前基线为：

- StreamFrame 固定为官方 NuGet `2.6.0`。
- secs4net 互操作测试固定为 nuget.org 官方 `3.1.0`，不得改用源码引用或
  非官方包。
- SML 调试读写、已解码消息 Trace 导出、结构化脱敏、受控及时序重放、
  诊断/控制元数据 Trace、解码失败样本和运输故障快照均已落地。
- 运输故障观测覆盖 StreamFrame 的四种 <code>FrameErrorKind</code>，默认
  关闭、队列有界，保留来源 Session、实际字节数和截断状态，并由 SecsFrame
  统一只保存最多 8 KiB 前缀；它不能进入消息重放路径。
- <code>SecsFrame-TransportFaultTrace/2</code> 显式保存实际字节数和
  Complete/Truncated；严格 codec 拒绝 v1，不对旧记录静默推断完整性。
- 真实 TCP Active/Passive 事务测试固定重复三次断开和重选，复用相同
  System Bytes 并强制投递旧 T3 回调，验证旧回复、旧计时器和待处理事务
  不会污染替换会话。
- 独立 <code>SecsFrame.Soak</code> 和 <code>session-soak</code> 工作流提供
  显式 seed、四类真实 TCP 故障、程序/周期/作业三层上限及 14 天 JSONL
  产物；普通 CI 只编译 harness，不运行长时负载。
- 本机 <code>SecsFrame.CommunicationDemo</code> 已形成连接、SML 发送、
  Linktest/Separate 和会话内诊断活动流的首条纵向链路；
  <code>SecsFrame.GuidedDemo</code> 已形成五步真实核心链路演示。两个项目
  的注意力模型、运行边界和跨会话账本见 [Demo 接续](DEMOS.md)。
- 最近一次完整本地验证中，核心测试在 `net48`、`net8.0`、`net10.0` 各
  通过 289 项；官方 secs4net 互操作测试在 `net8.0`、`net10.0` 各通过
  2 项。

所有新提交仍须重新运行：

```bash
dotnet build SecsFrame.slnx -c Release
dotnet test SecsFrame.slnx -c Release --no-build
```

## 已解除的上游依赖

[StreamFrame #56](https://github.com/CSJ608/StreamFrame/issues/56) 已随官方
[2.6.0 发布](https://github.com/CSJ608/StreamFrame/releases/tag/v2.6.0)。
SecsFrame 已核对 Release、NuGet 包和实际 API，并完成以下集成：

- 四种 <code>FrameErrorKind</code> 均使用事件自带的原始 transport
  Session ID，迟到旧会话错误不会归入替换会话；
- 公共观测与 Trace 保留 <code>ObservedByteCount</code> 和
  <code>IsTruncated</code>，SecsFrame 对所有类型再统一封顶 8 KiB；
- Trace 严格格式升级到 v2，v1 明确拒绝而不是推断缺失字段；
- 四种错误、跨会话、8192/8193 字节、真实 TCP T8、脱敏和严格读取均有
  自动化证据。

## 当前阻塞

### 授权标准材料

以下工作在取得合法标准副本并完成核对前保持阻塞：

- E37/E37.1 的 T5/T8 默认值和精确启停边界；
- E30 的报警历史/批量控制、完整命令权限/调度及规范性状态矩阵；
- E172 SEDD 与 E173 SMN 的 Schema、表示形式和验证规则。

不得为了填充路线图而从非授权材料推导规范性消息对、枚举或 Schema。

## 等待期间的下一优先级

不依赖 StreamFrame 新包或新增标准语义的确定性故障注入基线已经补齐：
真实 TCP Active/Passive 端点在同一生命周期中固定重复三次连接、Select、
Primary/Secondary、断开和重选，并验证：

- 每一代会话只完成本代事务，旧回复和旧计时器不能污染新会话；
- 断开中的待处理事务以确定异常结束，重选后新事务可以成功；
- 测试使用事件或显式同步点，不依赖任意 `Task.Delay`；
- 测试时间有严格上限，可稳定进入现有普通 CI。

这个切片只增加故障注入测试和必要的测试支撑，不改变协议默认值或公共 API。
独立定时工作流已经落地。合并后应先以 <code>workflow_dispatch</code> 使用
固定 seed <code>12345</code> 运行 5 分钟，核对四种故障均出现、JSONL 可下载
且失败定位信息完整；随后观察至少三次每周定时运行，再决定是否增加 TCP
half-close、reset 或跨平台矩阵。扩大故障范围前必须保持固定运行上限和可
重现输入，也不能用 soak 替代确定性回归和完整普通 CI。

Demo 工作按 [Demo 体验与迭代接续](DEMOS.md) 独立分轮：当前 D1、D2 已完成，
下一开发会话最多推进 D3、D4。D3 先为通讯工具增加入站回复、消息收藏和日志
筛选/脱敏导出；任何落盘或分享能力必须先明确数据分级。D4 再把既有 GEM
基础与 Trace/诊断能力加入固定演示脚本，不新增未经标准核对的消息语义。

## 状态更新规则

- 每个改变路线图优先级或外部阻塞的 PR 都应同步更新本文。
- 已完成能力的长期说明归入对应专题文档；本文只保留继续开发所需的状态，
  避免演变成提交日志或聊天记录。
