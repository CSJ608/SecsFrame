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
<code>SecsFrame.Sml</code> 同样是只依赖核心动态消息模型的可选调试表示层；
核心包不反向依赖它。
<code>SecsFrame.Trace</code> 依赖 SML 表示层和核心公共发送 API，只处理已经
解码的数据消息；它不订阅内部 transport 或创建第二个公共事件消费者。
Trace 的时序重放必须显式启用，只在允许发送记录之间等待，并继续通过公共
发送 API 创建新事务；默认重放不引入时间等待。
结构化诊断使用独立只读快照信封：只复制公共诊断的稳定标量，明确排除原
异常和未解码帧，也不能进入重放路径。
控制消息使用另一个只读元数据信封，只从调用方已消费的公共未认领控制事件
复制十字节头字段；它不增加内部控制面观察者，也不代表完整握手抓包。

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

- 直接采用 StreamFrame 2.5.0 的单调 TCP Session ID，并在 Connected
  发布点同步分配会话 epoch；
- 通过会话感知接收流保留帧的原始 Session ID，迟到消费不会被标成新会话；
- 会话绑定发送在 StreamFrame 原生 FIFO 中校验 Session ID，旧会话消息
  不会重放，整帧写入本机 Socket 后才完成；
- 当前会话关闭/重连与 transport 释放串行化，迟到 actor 输入不会访问已
  释放的 StreamFrame 生命周期；
- Active 连接把显式 T5 映射为固定 StreamFrame 重试间隔，关闭指数退避；
- 显式 T8 映射为 StreamFrame 原生未完成帧超时，并把对应 FrameError
  转换为带 transport Session ID 的 HSMS 关闭原因。

这些能力仍隔离在 <code>IHsmsTransport</code> 后，不进入公共 API。此前
跟踪的 StreamFrame #38/#39 已在 2.3.0 完成迁移，2.3.1 进一步封闭迟到
旧故障污染活会话和排队消息跨 Socket 错发的竞态，2.4.0 又收口发布窗口
旧 epoch 故障并加固 Passive 监听恢复，2.5.0 进一步以接受循环代次门控
阻止重连竞速泄漏旧监听器，并加入不改变编码契约的自适应发送缓冲；
SecsFrame 只保留协议关闭原因和异常边界转换。详细失效模式与验证证据见
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
上下线、动态状态变量、设备常量、应用托管时钟、报告定义、事件链接和
Collection Event；它通过现有端点路由处理 Primary，不创建第二个连接事件
消费者。报告配置在 Equipment 的同一锁边界内原子替换，事件触发先快照
报告与提供器再异步采值，不在锁内运行应用代码。详细边界见
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
<code>StreamConnectionOptions</code> 或 StreamFrame 的会话感知接口。

连接内部独占消费事务事件，并分别更新状态等待信号与公共事件 channel，
因此 readiness 等待不会与业务消息消费者竞争。详细生命周期和取消语义
见 [HSMS-CONNECTION.md](HSMS-CONNECTION.md)。

### 严格与兼容分离

默认按标准拒绝畸形帧、非法 Item 和无效状态转换。对现场设备的已知偏差通过命名明确的兼容选项启用，并记录来源、风险和测试向量。

## 依赖方向

- `SecsFrame` 可以依赖 `StreamFrame`。
- `SecsFrame.Gem` 依赖 `SecsFrame`。
- `SecsFrame.Sml` 依赖 `SecsFrame`，不得依赖 GEM 或线上传输实现。
- `SecsFrame.Trace` 依赖 `SecsFrame.Sml` 与 `SecsFrame`，重放必须回到
  公共发送 API，不得直接构造或写出保留旧事务标识的线上帧；可选时序只
  控制相邻发送前的等待，不得绕过连接状态、T3 或显式 allowlist。诊断
  导出不得隐式包含 <code>Exception</code> 或 <code>HsmsFrame</code>；控制
  元数据导出不得包含 Body 或隐式订阅连接事件流。
- 核心包不得依赖 GEM、SML/SMN、依赖注入容器或具体日志实现。
- 状态机计时使用可替换的时间抽象，测试不得依赖真实长时间等待。
