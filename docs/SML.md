# SML 调试读写

## 范围

<code>SecsFrame.Sml</code> 为现有动态 <code>SecsMessage</code> 与全部
<code>SecsItem</code> 类型提供确定性文本写出和严格读取。它是独立的可选
表示层，不进入 HSMS 会话、事务或线上二进制编解码路径。

当前实现是 SecsFrame 自主定义并验证的非规范性 SML 调试 profile，不声明
符合某个 SEMI 标准、厂商方言或一致性测试。消息模型没有名称字段，因此
profile 只表示 Stream、Function、W-Bit 和可选单根 Item。

## 使用

~~~csharp
using SecsFrame;
using SecsFrame.Sml;

var message = new SecsMessage(
    stream: 6,
    function: 11,
    replyExpected: true,
    rootItem: SecsItem.List(
        SecsItem.Ascii("LOT-001"),
        SecsItem.U4(1001)));

var codec = new SmlMessageCodec();
var text = codec.Encode(message);
var decoded = codec.Decode(text);
~~~

写出固定使用 LF、InvariantCulture 数字、明确的 Item 数量和稳定缩进：

~~~text
'S6F11'W
<L [2]
    <A [7] 'LOT-001'>
    <U4 [1] 1001>
>
.
~~~

读取允许 token 之间存在空白，但格式名、Boolean 和十六进制 token 的大小写
保持严格；完整消息之后的尾随文本会被拒绝。无 Body 使用消息头后直接跟
句点，空 List 使用 <code>&lt;L [0] ... &gt;</code>，两者不会混淆。

## 文本约定

- Item 格式名为 <code>L</code>、<code>B</code>、<code>Boolean</code>、
  <code>A</code>、<code>J</code>、<code>I1/I2/I4/I8</code>、
  <code>U1/U2/U4/U8</code> 和 <code>F4/F8</code>。
- Binary 与 JIS-8 使用 <code>0xHH</code> 原始字节；JIS-8 不猜测现场代码页。
- Boolean 使用 <code>True</code> 或 <code>False</code>。
- ASCII 位于单引号内，反斜线、单引号、回车、换行、制表符和不可打印
  七位字符分别使用可逆转义；<code>\xHH</code> 仍必须位于七位范围。
- 浮点数使用 .NET round-trip 格式，保留有限值、非数和正负无穷的往返。
- 读取严格验证声明数量、数值宽度、单根 Item、消息终止符和尾随文本。

<code>SmlMessageCodec</code> 默认沿用核心 Item codec 的最大嵌套深度和
Item 总数，并另外限制单个 Item 的值数量及输入/输出文本长度。调用方可以
在构造时收紧这些限制；解析失败使用带字符偏移、行和列的
<code>SmlParseException</code>。

## 参考与版权边界

实现前只读交叉核对了 MIT 许可的
[secs4net](https://github.com/mkjeff/secs4net) 中公开的
<code>Secs4Net.Sml</code> 项目，以确认常见的消息头、Item 格式名和 Boolean
拼写。本实现围绕 SecsFrame 的不可变模型和严格资源边界独立编写，没有复制
其源码；测试向量也在本仓库自主构造。JIS-8 原始字节与 ASCII 转义是本
profile 为无损调试增加的明确约定。

该参考不参与构建。官方 secs4net 互操作项目仍只使用 nuget.org 发布并固定
为 3.1.0 的 NuGet 包。
