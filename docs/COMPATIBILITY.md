# 兼容策略

设备兼容行为必须满足以下条件：

1. 默认关闭，标准严格模式不受影响。
2. 使用描述行为的名称，不能使用含糊的 `IgnoreErrors`。
3. 文档记录观察到的线上数据、适用厂商/版本和潜在风险。
4. 包含严格模式拒绝、兼容模式接受的成对测试。
5. 不允许兼容选项改变其它连接或全局静态状态。

计划评估的历史行为包括：

- 把声明为 ASCII 的 UTF-8 字节按兼容编码读取。
- 设备声明的 List 元素数量大于实际可解码元素数量。
- Passive 模式下设备频繁断线和并发重连。
- 限制 Passive 端同一时间只处理一个客户端。

这些条目只是调查清单，不代表当前实现已经支持。

## 上游能力集成

StreamFrame #38（未完成帧超时）与 #39（会话感知发送确认及消息上下文）
已经随官方 StreamFrame 2.3.0 发布，2.3.1 随后补充迟到旧会话故障隔离、
排队发送 Session ID 复核和稳定失效异常修复。SecsFrame 当前固定官方
2.5.0 NuGet 包；2.4.0 收口 Connected 发布点的会话 epoch，并加固 Passive
接受重试与监听端口重绑；2.5.0 再以接受循环代次门控阻止显式关闭/自动重连
竞速泄漏旧监听器，并采用不改变编码契约的自适应发送缓冲。内部
<code>StreamFrameHsmsTransport</code> 使用原生会话 API；此前基于
原始字节回调、发送信封和自建 Session ID 的临时实现已经删除。

这不是设备兼容模式：

- 不放宽协议输入，也不接受标准严格模式本应拒绝的数据；
- 不提供默认关闭的厂商开关；
- 不暴露 StreamFrame 类型为公共 API，不形成跨层兼容承诺；
- SecsFrame 仅保留 HSMS 关闭原因、异常和事件边界转换。

具体机制、测试证据和已知代价见
[STREAMFRAME-ADAPTER.md](STREAMFRAME-ADAPTER.md)。

## 跨实现测试依赖

当前 secs4net 互操作夹具使用 nuget.org 发布的官方
<code>Secs4Net 3.1.0</code> 包，并在
<code>Directory.Packages.props</code> 中固定版本。仓库
<code>NuGet.Config</code> 清除其它源并把全部包映射到 nuget.org。不得
使用本地源码 checkout、ProjectReference、本地 feed 或自行修改的程序集
作为互操作对端。源码仓库只读参考不进入 SecsFrame 的还原、构建或测试图。

包身份、运行框架、测试方向与结果见
[SECS4NET-INTEROP.md](SECS4NET-INTEROP.md)。这些证据避免把本地差异
误判为上游互操作能力。
