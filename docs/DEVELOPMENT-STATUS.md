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

截至 2026-08-29，功能基线为 `main` 的 PR #40（提交 `40743c6`）：

- StreamFrame 固定为官方 NuGet `2.5.0`。
- secs4net 互操作测试固定为 nuget.org 官方 `3.1.0`，不得改用源码引用或
  非官方包。
- SML 调试读写、已解码消息 Trace 导出、结构化脱敏、受控及时序重放、
  诊断/控制元数据 Trace、解码失败样本和 T8 前缀快照均已落地。
- T8 故障观测默认关闭、队列有界，最多保存 StreamFrame 提供的 8 KiB
  前缀；它不是完整 TCP 片段，也不能进入消息重放路径。
- SecsFrame 当前没有开放 Issue。PR #40 合并后的 `ci` 与 `codeql` 均通过。
- 最近一次完整本地验证中，核心测试在 `net48`、`net8.0`、`net10.0` 各
  通过 273 项；官方 secs4net 互操作测试在 `net8.0`、`net10.0` 各通过
  2 项。

所有新提交仍须重新运行：

```bash
dotnet build SecsFrame.slnx -c Release
dotnet test SecsFrame.slnx -c Release --no-build
```

## 当前阻塞

### StreamFrame 错误归属

[StreamFrame #56](https://github.com/CSJ608/StreamFrame/issues/56) 已得到维护者
接受，正在实现。评审建议在 StreamFrame `2.6.0` 提供：

- `FrameError` 的原始 transport Session ID；
- 错误发生时的实际观测字节数；
- `Bytes` 快照是否截断；
- 四种 `FrameErrorKind` 一致的来源会话语义，并保留现有兼容性。

上游发布前，不扩展 `DecodeFailed`、`IncompleteFrameOverflow` 与
`DiscardedByResync` 的公共运输故障观测。不得用回调时读取的
`CurrentSessionId` 冒充错误的原始会话归属。

上游发布后，先核对 Release、NuGet 包和真实 API，再安排一个独立升级切片：

1. 升级 StreamFrame，并确认旧会话迟到错误仍携带旧 Session ID。
2. T8 改用事件自带的来源会话，并扩展其余三种错误类型。
3. 在观测和 Trace 中忠实保留实际字节数与截断状态；需要时升级 Trace
   信封版本，不静默改变现有严格格式。
4. 增加四种错误、跨会话、8 KiB 上下边界、脱敏和严格读取测试。
5. 更新 README、路线图、专题文档与 `CHANGELOG.md`，完成完整验证并通过
   Pull Request 交付。

### 授权标准材料

以下工作在取得合法标准副本并完成核对前保持阻塞：

- E37/E37.1 的 T5/T8 默认值和精确启停边界；
- E30 的报警历史/批量控制、完整命令权限/调度及规范性状态矩阵；
- E172 SEDD 与 E173 SMN 的 Schema、表示形式和验证规则。

不得为了填充路线图而从非授权材料推导规范性消息对、枚举或 Schema。

## 等待期间的下一优先级

不依赖 StreamFrame #56 或新增标准语义的下一项，是补充 `1.0` 发布门槛中的
确定性故障注入证据。

建议的最小垂直切片是“重复会话抖动下的事务恢复”：在真实 TCP
Active/Passive 端点的同一生命周期中，固定次数重复执行连接、Select、
Primary/Secondary、断开和重选，并验证：

- 每一代会话只完成本代事务，旧回复和旧计时器不能污染新会话；
- 断开中的待处理事务以确定异常结束，重选后新事务可以成功；
- 测试使用事件或显式同步点，不依赖任意 `Task.Delay`；
- 测试时间有严格上限，可稳定进入现有普通 CI。

这个切片只增加故障注入测试和必要的测试支撑，不改变协议默认值或公共 API。
长时间 soak、随机网络故障和独立定时工作流应在该确定性基线稳定后另行评估。

## 状态更新规则

- StreamFrame #56 发布后，将其从“当前阻塞”移入完成基线，并记录实际采用的
  StreamFrame 版本和 SecsFrame PR。
- 每个改变路线图优先级或外部阻塞的 PR 都应同步更新本文。
- 已完成能力的长期说明归入对应专题文档；本文只保留继续开发所需的状态，
  避免演变成提交日志或聊天记录。
