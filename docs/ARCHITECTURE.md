# 架构

## 分层

```text
SecsFrame.Gem        GEM 通用行为与能力（SEMI E30）
        |
SecsFrame            SECS-II 消息/事务（E5）+ HSMS-SS 会话（E37/E37.1）
        |
StreamFrame          TCP、Pipe、帧边界、连接生命周期
```

E172 SEDD 与 E173 SMN 是可选元数据/表示层，不进入核心通讯路径。

## 核心原则

### 动态消息优先

线上消息由 Stream、Function、W-Bit 和动态 Item 树表达。消息目录、SML/SMN 文件和代码生成是可选能力，不能成为发送未知或厂商自定义消息的前置条件。

<code>SecsItem</code> 是不可变值对象，覆盖 E5 的全部 Item 格式。List
保存任意嵌套 Item，数值类型使用明确宽度的 CLR 类型，ASCII 只接受七位
字符，JIS-8 保留原始编码字节。<code>SecsItemCodec</code> 只负责一个
完整 Item 树。

<code>SecsMessage</code> 表达 Stream、Function、W-Bit 和可选的单根
Item，不包含传输会话或事务标识。<code>HsmsDataMessage</code> 在其外层
增加 Session ID 与 System Bytes；<code>HsmsDataMessageCodec</code>
负责十字节数据头和可选 Item Body。空 Body 与零元素 List 是不同状态。
原始 <code>HsmsFrame</code> 仍用于控制消息和后续状态机，不因高层动态
消息 API 而改变。

~~~text
HsmsFramer: 4-byte length
        |
HsmsDataMessageCodec: 10-byte data header
        |
SecsItemCodec: optional single root Item
~~~

解码默认严格且有资源边界：保留格式、零长度字节计数、截断、数值宽度
错位、非法 ASCII、根 Item 后尾随数据均失败；递归深度和总节点数可配置。
数据消息层还拒绝非 Data Message 的 SType 和非零 PType。

### 连接与协议会话分离

TCP `Connected` 不等于 HSMS `Selected`。Host/Equipment 角色也不等于 Active/Passive 连接模式，四种组合应在模型上保持独立。

### 不跨会话重放

HSMS 会话切换后，旧会话的控制请求、数据事务和等待中的应答全部失效。底层发送必须能绑定会话，并在 Socket 写完后才报告成功。

<code>IHsmsTransport</code> 使用单一事件流按 Session ID 报告会话打开、
帧到达和会话关闭。<code>StreamFrameHsmsTransport</code> 已在内部实现：

- 每次 TCP Connected 生成单调递增、不可复用的 Session ID；
- 收到的帧在 codec 解码时绑定 Session ID，迟到消费不会被标成新会话；
- 发送信封在编码前再次校验 Session ID，旧会话消息不会上线；
- 同一时间只向 StreamFrame 提交一帧，并根据 RawBytesSent 的实际分片
  累计确认，整帧写完后才完成发送；
- Active 连接把显式 T5 映射为固定 StreamFrame 重试间隔，关闭指数退避；
- 原始接收字节独立跟踪长度前缀和剩余 Payload，仅有未完成帧时运行 T8，
  并用代次标识隔离接收进展与替换会话的陈旧回调。

这些能力隔离当前 StreamFrame 2.2.0 尚未提供的高级语义，不进入公共
API。#38 已合入上游但尚未发布，#39 正在实现；正式包可用后只替换
<code>IHsmsTransport</code> 实现。详细失效模式与替换条件见
[STREAMFRAME-ADAPTER.md](STREAMFRAME-ADAPTER.md)。

### 单线程会话状态机

<code>HsmsSessionStateMachine</code> 是内部 actor：传输事件、发送完成、
计时器到期和本地 Separate 命令进入同一输入队列，状态只在一个读取者上
修改。Active/Passive 共享状态模型，Host/Equipment 角色不参与连接模式
判断。

公共 <code>SecsHost</code> 与 <code>SecsEquipment</code> 在连接和动态路由
之上建立角色组合边界。两者都拥有连接生命周期、事件流、动态发送和
Primary 处理能力；角色固定但 Active/Passive 仍由独立选项决定。GEM
能力依赖具体角色类型，不反向污染 HSMS 状态机。
独立 <code>SecsFrame.Gem</code> 程序集已在这一边界上提供可配置的通讯建立、
上下线、动态状态变量、设备常量和应用托管时钟；它通过现有端点路由处理
Primary，不创建第二个连接事件消费者。详细边界见
[GEM-FOUNDATION.md](GEM-FOUNDATION.md)。

~~~text
TCP SessionOpened
        |
        v
    Connected -- Active sends Select Request --> Selecting
        |                                         |
        +-- Passive receives Select Request ------+
                                                  v
                                              Selected
                                                  |
                          Separate / close / error v
                                             Disconnected
~~~

T7 从 TCP 会话打开后运行，到 Selected 时取消。Active 的 Select Request
只有在 <code>IHsmsTransport.SendAsync</code> 确认完整线上帧实际写出后
才启动 T6。Select Response 必须匹配等待中的 System Bytes。状态机只在
Selected 转发数据消息；提前到达的数据、可识别的不支持类型和意外响应
会生成 Reject，不能安全回应的畸形控制头才关闭当前 Session ID。这些行为
不会影响替换会话。详细行为与未实现边界见
[HSMS-SESSION-STATE-MACHINE.md](HSMS-SESSION-STATE-MACHINE.md)。

会话 actor 同时处理 Linktest、Deselect 和 Reject。每个会话只允许一个
本地主动控制事务；Linktest/Deselect 的 T6 同样从实际写出后开始，响应
必须匹配类型与 System Bytes。Deselect 成功回到 Connected 并重启 T7。
无法由会话层认领的 Reject 作为 <code>ControlMessageReceived</code>
事件向上转发，供数据事务 actor 关联，不会被静默丢弃。

### 会话绑定的数据事务

内部 <code>HsmsDataTransactionManager</code> 独占消费会话事件，并把
发送请求、写出完成、接收数据、T3 回调、取消和状态变化串行化。数据发送
仍回到会话 actor 做 Selected 与当前 transport session 校验，事务层不会
绕过 <code>IHsmsTransport</code>。

有 W-Bit 的出站 Primary 在登记关联键后发送，只在整帧实际写出后启动
独立 T3。Secondary 必须不带 W-Bit，并同时匹配 transport Session ID、
HSMS 头 Session ID 与 System Bytes。没有 W-Bit 的出站消息在写出后
直接完成。Reject、Deselect、断线和替换会话都会结束对应等待，T3 单独
到期不关闭 Selected 会话。

没有匹配打开事务的合法数据继续上报，而不是依赖 Function 奇偶或硬编码
消息目录猜测角色。带 W-Bit 的入站消息可以使用绑定原 transport session
的信封回复一次，回复复制协议 Session ID 与 System Bytes。具体失效语义
和测试边界见
[HSMS-DATA-TRANSACTIONS.md](HSMS-DATA-TRANSACTIONS.md)。

### 公共连接外观

<code>HsmsConnection</code> 是运输、会话和数据事务 actor 之上的薄公共
组合层。它要求显式网络、协议 Session ID 和 T3/T5/T6/T7/T8 配置，公开
动态消息发送、一次性入站回复、控制命令、Selected 等待和单消费者事件
流。公共层不暴露 transport Session generation、StreamFrame
<code>StreamConnectionOptions</code> 或 #38/#39 的回调适配。

连接内部独占消费事务事件，并分别更新状态等待信号与公共事件 channel，
因此 readiness 等待不会与业务消息消费者竞争。详细生命周期和取消语义
见 [HSMS-CONNECTION.md](HSMS-CONNECTION.md)。

### 严格与兼容分离

默认按标准拒绝畸形帧、非法 Item 和无效状态转换。对现场设备的已知偏差通过命名明确的兼容选项启用，并记录来源、风险和测试向量。

## 依赖方向

- `SecsFrame` 可以依赖 `StreamFrame`。
- `SecsFrame.Gem` 依赖 `SecsFrame`。
- 核心包不得依赖 GEM、SML/SMN、依赖注入容器或具体日志实现。
- 状态机计时使用可替换的时间抽象，测试不得依赖真实长时间等待。
