# SECS-II Item

## 范围

当前实现以 <code>SEMI E5-0725</code> 为版本基线，覆盖 Item 数据模型和
一个完整 Item 树的二进制编解码。它不包含 Stream/Function 消息语义、
事务协议或标准消息目录，也不声明已经通过 SEMI 一致性验证。

## 数据模型

<code>SecsItem</code> 是不可变值对象，提供以下动态工厂：

| E5 格式 | 工厂 | CLR 表示 |
|---|---|---|
| List | <code>List</code> | <code>IReadOnlyList&lt;SecsItem&gt;</code> |
| Binary | <code>Binary</code> | <code>byte</code> |
| Boolean | <code>Boolean</code> | <code>bool</code> |
| ASCII | <code>Ascii</code> | <code>string</code>，仅七位字符 |
| JIS-8 | <code>Jis8</code> | 已编码 <code>byte</code> |
| I1/I2/I4/I8 | 同名工厂 | <code>sbyte</code> / <code>short</code> / <code>int</code> / <code>long</code> |
| U1/U2/U4/U8 | 同名工厂 | <code>byte</code> / <code>ushort</code> / <code>uint</code> / <code>ulong</code> |
| F4/F8 | 同名工厂 | <code>float</code> / <code>double</code> |

工厂复制调用方传入的数组，<code>Items</code> 是只读集合，
<code>GetValues&lt;T&gt;()</code> 返回只读 Span。调用方后续修改原始
数组不会改变 Item。

JIS-8 只在核心层保留线上字节，不绑定操作系统代码页。字符转码应作为
显式可选策略提供，并在获得对应标准版本和设备互操作证据后实现。

## 编解码契约

<code>SecsItemCodec</code> 实现 StreamFrame 的
<code>ICodec&lt;SecsItem&gt;</code>：

- 编码使用能表达长度的最短 1、2 或 3 字节长度字段；
- 解码接受合法的 1、2 或 3 字节长度字段，包括非最短表示；
- 数值和 IEEE 754 浮点负载按 Big Endian 读写；
- Boolean 编码固定使用 <code>0x00</code> 和 <code>0x01</code>，
  解码时零为假、非零为真；
- <code>Decode</code> 要求输入恰好包含一个完整根 Item；
- 默认最大深度为 100，默认最大节点数为 1,000,000，构造时可收紧。

以下输入在严格模式下抛出 <code>SecsProtocolException</code>：

- 长度字节计数为零；
- 保留或未知格式；
- 格式头、长度字段或负载截断；
- I2/I4/I8、U2/U4/U8、F4/F8 的负载长度与元素宽度不整除；
- ASCII 负载包含大于 <code>0x7F</code> 的字节；
- List 声明的元素数无法由剩余字节承载；
- 超过深度或节点上限；
- 完整根 Item 后仍有尾随字节。

兼容现场设备偏差时不得放宽这个默认编解码器。兼容行为必须通过命名
明确、默认关闭的选项或独立适配器提供，并包含严格拒绝与兼容接受的成对
测试。

## 测试证据

协议测试覆盖全部 15 种格式、空值、整数边界、浮点已知位模式、
255/256/65535/65536 长度边界、嵌套 List、逐字节分段
<code>ReadOnlySequence</code>、非最短合法长度、非法输入、资源上限和
往返行为。这些是独立编写的实现测试，不替代 SEMI 授权的一致性测试套件。
