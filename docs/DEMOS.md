# Demo 体验与迭代接续

## 目标与边界

仓库提供两个面向不同意图的本机 Web Demo：

- <code>SecsFrame.CommunicationDemo</code> 面向集成和现场调试。用户配置
  端点、建立连接、选择或编辑 SML、发送数据/控制消息，并在同一工作面查看
  状态、消息与结构化诊断。
- <code>SecsFrame.GuidedDemo</code> 面向成果演示。用户从一个明确的开始
  动作进入固定脚本，每一步只聚焦一个能力、一个实际动作和一组可见证据。

两个 Demo 都直接引用仓库项目，不复制协议实现。它们展示已实现的工程行为，
不新增标准默认值、设备消息 Profile 或 SEMI 合规结论。本机回环响应规则只
用于形成可观察事务，不代表任何设备接口定义。

采用 ASP.NET Core Interactive Server 是为了让浏览器承担界面，同时让本机
服务进程直接使用 <code>HsmsConnection</code> 的 TCP 能力。首轮不引入新的
前端框架或外部运行期资源。

## 注意力模型

### 通讯测试工具

| 阶段 | 用户首要问题 | 主关注区 | 持续证据 |
|---|---|---|---|
| 未连接 | 参数是否正确、能否连上 | 连接配置 | 顶栏状态与连接活动 |
| Selected | 发什么、是否等待回复 | SML 编辑与发送 | Session、System Bytes |
| 操作中 | 动作是否完成 | 当前命令与禁用状态 | 活动时间线 |
| 故障 | 哪一层、哪个操作、哪个计时器 | 最新诊断 | 稳定诊断代码和详细字段 |
| 复盘 | 刚才发生了什么 | 活动与诊断 | 有界、倒序的会话内记录 |

因此界面不是传统仪表盘：连接、消息、日志是一个从左到右的工作流。未连接时
强调第一栏；进入 Selected 后强调第二栏；第三栏始终可见且不抢占当前操作。
窄屏按相同顺序纵向排列，避免改变工作心智。

### 分步演示系统

| 阶段 | 用户首要问题 | 主关注区 | 持续证据 |
|---|---|---|---|
| 开始前 | 这次会看到什么 | 场景标题与开始动作 | 固定步骤总数 |
| 执行中 | 当前系统做了什么 | 当前步骤舞台 | 已完成步骤轨迹 |
| 结果出现 | 如何证明不是静态动画 | 实际结果 | 状态、SML、事务标识 |
| 步骤切换 | 下一步关注什么 | 下一步动作 | 已完成证据保留 |
| 完成 | 当前成果覆盖到哪里 | 运行总结 | 能力/边界清单 |

演示系统不暴露连接表单或自由编辑器。所有输入固定且可复现；每次只执行当前
步骤，用户明确点击后才前进，不使用自动轮播。

## 当前工程

### 统一启动与发布

从源码统一启动两个 Demo：

~~~bash
dotnet run --project demo/SecsFrame.DemoLauncher/SecsFrame.DemoLauncher.csproj
~~~

启动器默认只接受 <code>http://127.0.0.1</code> 回环端点，分别使用 5080
和 5081 端口。它启动两个子进程、等待包含静态资源检查的专用健康端点，随后
打开两个实际体验页；任一子进程提前退出时会停止另一进程。使用
<code>--help</code> 查看改端口、禁止自动打开浏览器及一次性启动验证选项。

生成 framework-dependent .NET 8 发布包：

~~~powershell
pwsh ./eng/publish-demos.ps1
~~~

脚本使用 <code>artifacts/demo-package-build</code> 隔离构建输出，不覆盖正在
运行的项目 <code>bin</code> 目录；它生成可解压后运行的
<code>artifacts/demo-package</code> 和
<code>artifacts/SecsFrame-Demos-net8.0.zip</code>。包内含 Windows 与
Linux/macOS 启动脚本、源提交清单、许可证和边界说明，需要 .NET 8
ASP.NET Core Runtime。清单同时记录工作区 Dirty 标志，避免本地未提交包被
误认为精确对应源提交。手动 <code>demo-package</code> 工作流执行相同打包
和发布后启动验证，并保留 ZIP 产物 14 天。

两个页面现在提供跳到主要内容、明确可见的键盘焦点、区域与忙碌状态语义；
分步演示另提供 progressbar/当前步骤语义，并在每个实际动作完成后把焦点
移动到结果标题。窄屏控件维持至少 44px 的主要触达高度。

### 通讯工具

运行：

~~~bash
dotnet run --project demo/SecsFrame.CommunicationDemo/SecsFrame.CommunicationDemo.csproj
~~~

默认地址为 <http://localhost:5080>。首个纵向切片包括：

- Active/Passive、IP、端口、Session ID 和显式 T3/T5/T6/T7/T8；
- 默认开启的本机真实 TCP Active/Passive 回环；
- 三个可编辑 SML 样例与严格 SML 解析；
- 数据消息发送、Secondary 显示、入站 W-Bit Primary 的显式可编辑回复、
  Linktest 和 Separate；本机回环可按需生成一条入站事务用于自测；
- 严格解析后保存的会话内消息收藏，不写浏览器持久存储或服务器文件；
- 最多 500 条的内存活动流，显示状态、方向、System Bytes、SML 和受限
  结构化诊断，并支持关键词、类别、级别筛选与筛选结果导出。

当前回环端对 W-Bit 消息返回相同 Body，并把 Function 加一。这个规则在界面
日志中明确标为非设备 Profile。

活动导出必须显式选择数据分级：

- <code>MetadataOnly</code> 为默认值，只导出时间、级别、类别和标题；
- <code>RedactedContent</code> 另含摘要和受限详情。协议消息先通过严格 SML
  解析，再保留 S/F/W 并把整个根 Item 替换为 ASCII
  <code>REDACTED</code>；诊断只使用既有稳定标量；
- 不提供 Raw 导出。文件由当前浏览器直接下载，服务端不落盘；筛选只决定
  本次导出的记录集合，不改变内存活动流。

即使是 <code>RedactedContent</code>，端点、Session ID、System Bytes 和
诊断字段仍属于受限运维元数据，分享前仍需遵守项目自身访问控制和保留策略。

### 分步演示

运行：

~~~bash
dotnet run --project demo/SecsFrame.GuidedDemo/SecsFrame.GuidedDemo.csproj
~~~

默认地址为 <http://localhost:5081>。当前固定九步依次演示公共角色端点与
Selected、动态 Item/SML、真实 Primary/Secondary、Linktest、会话恢复、
GEM 工程 profile 通讯建立、运行期状态变量读取、结构化脱敏 Trace，以及
真实未回复事务形成的 T3 受限诊断 Trace。运输故障样本仍留给后续脚本。

GEM 步骤直接使用 <code>GemHostServices</code>、
<code>GemEquipmentServices</code> 和运行期变量提供器；Trace 步骤直接使用
<code>SecsTraceRedactor</code>、<code>SecsTraceCodec</code> 与
<code>SecsTraceDiagnosticCodec</code>。固定身份、SVID、值、消息对和故障
输入都只是可复现工程样本，不新增设备 Profile 或未经核对的 E30 语义。

## 迭代账本

每个开发会话最多完成两轮迭代。完成一轮后立即更新本节；达到两轮后停止新增
范围，把下一轮留给新会话。

| 轮次 | 状态 | 范围 | 验收 |
|---|---|---|---|
| D1 | 已完成 | 通讯工具首条连接/消息/日志纵向链路 | Release 编译；真实回环发送与控制命令；1440px/390px 无横向溢出 |
| D2 | 已完成 | 分步演示首条核心 HSMS 导览 | 五步实际动作与证据；Release 编译；1440px/390px 无横向溢出 |
| D3 | 已完成 | 通讯工具入站回复、会话内收藏、日志筛选/脱敏导出 | 两级数据分级且无 Raw；真实回环入站回复；Demo 自动化测试 |
| D4 | 已完成 | 分步演示 GEM 基础与 Trace/诊断场景 | 九步真实动作；公共 GEM/Trace API；真实 T3；Demo 自动化测试 |
| D5 | 进行中 | 启动器、发布打包、可访问性与用户试用修正 | ZIP 发布与双应用启动验证已通过；SSR/静态资源已核对；390px 截图和真实 Tab/焦点浏览器验收待完成 |

## 跨会话接续

新会话按以下顺序恢复：

1. 阅读根目录 <code>AGENTS.md</code>、<code>README.md</code>、
   <code>docs/DEVELOPMENT-STATUS.md</code> 和本文。
2. 检查本文迭代账本、最新 Git 历史、工作区与对应 Demo 项目。
3. 从最新 <code>main</code> 创建符合仓库约定的主题分支。
4. 一次只把一个“动作到证据”的纵向切片标为进行中；不得同时开启第三轮。
5. 行为、边界、运行命令或下一优先级变化时，同步本文、CHANGELOG 和开发
   状态；完整 Release 构建测试仍是提交门禁。

所有演示文本应区分“实际执行证据”和“说明性边界”。未经授权标准与一致性
测试核对，不得把工程回环、固定脚本或成功运行改写成标准合规或生产就绪声明。
