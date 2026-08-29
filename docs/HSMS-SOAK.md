# HSMS 会话故障 Soak

## 范围

<code>SecsFrame.Soak</code> 是独立的真实 TCP 工程故障注入程序。它加入
solution 以便普通构建持续检查，但不是测试项目，因此
<code>dotnet test SecsFrame.slnx</code> 不会执行长时负载。

每个周期先从 Active 端发送带 W-Bit 的 Primary，并在 Passive 端收到但尚未
回复时，按显式随机 seed 选择一种故障：

- Active 发送 Separate；
- Passive 发送 Separate；
- 销毁并重建 Active 公共连接；
- 销毁并重建 Passive 公共连接。

原事务必须以运输/会话中断结束。双方重新进入 Selected 后，程序立即执行
新的 Primary/Secondary 往返并核对动态 Body。程序按 seed 对每组四种故障
洗牌，因此每四个完整周期覆盖全部模式且顺序可复现；程序不使用设备输入、
厂商兼容开关或新的协议默认值。

## 运行边界

独立 [session-soak 工作流](../.github/workflows/session-soak.yml) 支持：

- 每周三 UTC 02:43 定时运行 15 分钟；
- 手动选择 1、5、15 或 20 分钟；
- 手动指定正 32 位 seed，<code>0</code> 从 GitHub Run ID 确定性派生；
- 程序最长 20 分钟、单周期最长 20 秒、作业最长 25 分钟，并以 100000
  个周期作为 duration 之外的次级上限；
- 同一时间只运行一个 soak 作业，后续触发排队而不取消已有运行。

普通 PR/push CI 不触发该工作流。它仍会通过 solution 的 Release 构建检查
harness 是否可编译；协议向量、状态机测试和 secs4net 互操作门禁保持不变。

## 本地复现

~~~bash
dotnet build test/SecsFrame.Soak/SecsFrame.Soak.csproj -c Release
dotnet run --project test/SecsFrame.Soak/SecsFrame.Soak.csproj \
  -c Release --no-build -- \
  --seed 12345 \
  --duration-seconds 300 \
  --max-cycles 100000 \
  --output artifacts/soak/session-soak.jsonl
~~~

<code>--seed</code> 必须显式提供；duration 接受 1 到 1200 秒，max-cycles
接受 1 到 100000。达到任一上限即成功停止。若总时限在周期中到达，程序
取消该周期、释放双方连接并写出 <code>durationElapsed</code> 完成记录；若
时限内没有任何周期完成则失败，避免初始化停滞被误记为成功。

## 产物与复现

JSONL 在启动、每个周期开始、每个周期完成、失败和正常结束时立即刷新。
启动记录包含 Git commit、.NET 运行时、OS、seed、duration 与 max-cycles；
周期记录包含故障类型、中断异常类型、原事务和恢复事务的 System Bytes 及
耗时。失败记录包含 seed、周期、故障类型和异常文本。

工作流无论成功失败都上传该文件，保留 14 天。复现时应检出启动记录中的
commit，使用相同运行时、OS、seed、duration 和 max-cycles；最后一个
<code>cycleStarted</code> 给出失败前选择的确切故障。报告只包含程序生成的
周期标记和协议/运行期元数据，不捕获设备 Body、原始 TCP 字节或异常帧。

## 证据边界

固定 seed 的本地短时验证必须至少覆盖四种故障并确认 duration 上限正常
收口。定时 soak 用于发现长期资源、监听恢复和竞态问题，不替代普通 CI、
确定性状态机/协议向量或跨实现测试，也不构成 SEMI 一致性或生产稳定性声明。
