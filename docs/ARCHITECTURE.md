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
完整 Item 树；空 Body 和 Item Body 的选择属于后续
<code>SecsMessage</code> 层。

解码默认严格且有资源边界：保留格式、零长度字节计数、截断、数值宽度
错位、非法 ASCII、根 Item 后尾随数据均失败；递归深度和总节点数可配置。

### 连接与协议会话分离

TCP `Connected` 不等于 HSMS `Selected`。Host/Equipment 角色也不等于 Active/Passive 连接模式，四种组合应在模型上保持独立。

### 不跨会话重放

HSMS 会话切换后，旧会话的控制请求、数据事务和等待中的应答全部失效。底层发送必须能绑定会话，并在 Socket 写完后才报告成功。

StreamFrame 尚未提供该高级语义时，SecsFrame 通过内部传输端口隔离适配代码；公共 API 不暴露临时队列或回调约定。

### 严格与兼容分离

默认按标准拒绝畸形帧、非法 Item 和无效状态转换。对现场设备的已知偏差通过命名明确的兼容选项启用，并记录来源、风险和测试向量。

## 依赖方向

- `SecsFrame` 可以依赖 `StreamFrame`。
- `SecsFrame.Gem` 依赖 `SecsFrame`。
- 核心包不得依赖 GEM、SML/SMN、依赖注入容器或具体日志实现。
- 状态机计时使用可替换的时间抽象，测试不得依赖真实长时间等待。
