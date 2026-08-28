# HSMS 数据事务

## 当前范围

内部 <code>HsmsDataTransactionManager</code> 位于
<code>HsmsSessionStateMachine</code> 之上，提供不依赖消息目录的第一版
SECS-II/HSMS 数据事务能力：

- 只在当前 transport session 为 Selected 时发送数据；
- 为出站消息分配 System Bytes，并避免与同一 transport session 中仍
  打开的数据事务冲突；
- 无 W-Bit 的消息在整帧实际写出后完成，不创建 T3；
- 有 W-Bit 的消息在整帧实际写出后启动独立 T3；
- 使用 transport Session ID、HSMS 头 Session ID 与 System Bytes 的
  组合键关联不带 W-Bit 的 Secondary；
- Reject、Deselect、连接关闭、调用取消和畸形匹配 Secondary 都会明确
  结束对应等待；
- 入站 W-Bit 消息可使用原 Session ID 与 System Bytes 发送一次
  Secondary；
- 会话状态、未消费控制消息、未匹配数据和解码失败继续作为事件上报。

这层仍是内部 API，目的是先稳定协议状态与失效语义，再设计公共连接外观。
它不要求预注册 SxFy、报告、事件或厂商自定义消息。

## 发送与 T3

~~~text
SendAsync
    |
    v
事务 actor 分配 System Bytes 并登记等待
    |
    v
会话 actor 校验 Selected 与当前 transport session
    |
    v
IHsmsTransport 确认四字节长度、十字节头和 Body 全部写出
    |
    +-- W=false --> 完成
    |
    +-- W=true  --> 启动该事务自己的 T3
                       |
                       +-- 匹配 Secondary --> 停止 T3 并完成
                       +-- T3 到期        --> 只结束事务，不关闭会话
~~~

对调用的取消表示停止等待。若数据帧已被会话 actor 接受，底层仍等待明确
的写出或会话失效结果，不能把取消解释为“线上一定没有发送”。取消后的迟到
数据不会静默丢弃，而是进入未匹配数据事件。

## 关联与入站消息

仅当消息不带 W-Bit，且三个关联字段都匹配当前打开事务时，才作为该
Primary 的 Secondary。相同 System Bytes 但不同 transport session 或
HSMS Session ID 不会误完成事务；替换连接也不会继承旧等待。

其余合法 Data Message 统一作为未匹配入站数据上报。这里有意不根据
Function 奇偶、消息名称或预定义目录猜测其角色，因此既能表达无需回复的
入站 Primary，也保留迟到或意外 Secondary 的原始信息。后续 E5/GEM
行为层可以在获得消息语义后进一步分类。

入站消息带 W-Bit 时可以调用回复路径。回复必须清除 W-Bit，复制原 HSMS
Session ID 与 System Bytes，并且同一入站信封只允许启动一次回复。旧
transport session 的信封不能在新连接上回复。

## 失败边界

- 匹配 Data Message 的 Reject 以明确拒绝异常结束事务，同时保留控制
  事件用于诊断；
- Deselect 回到 Connected、Separate 或连接关闭会结束该会话所有发送
  与 T3 等待；
- 匹配事务但 Body 非法的 Secondary 直接以解码错误结束等待，并上报
  原始帧和错误；
- T3 到期不会自动关闭 Selected 会话，也不自动重试；
- 本层不生成 S9Fx，不解释 SxF0，也不实现 GEM 业务错误恢复。

## 测试证据

手动 transport 与手动计时器测试覆盖写出前不启动 T3、无 W-Bit 路径、
嵌套 Item 往返、并发独立计时器、重复 System Bytes 避让、复合键匹配、
Reject、Deselect、断线、取消、迟到消息、畸形 Secondary 和一次性回复。
Active/Passive 双端真实 TCP 回环另行覆盖 Select 后的 Primary/Secondary
完整往返。

这些测试是自主构造的工程向量，不是 SEMI 一致性测试。

## 标准核对边界

实现追踪 SEMI E5-0725、E37-0222 与 E37.1-0819。扩大公共事务 API 或
声明合规前，仍须使用团队合法获得的版本核对 Primary/Secondary 定义、
W-Bit 与 Function 约束、T3 的精确启停、SxF0/S9Fx 对事务的影响、
System Bytes 分配规则及 Reject 后处理。仓库不得提交标准正文、状态表
或由付费材料机械转换的测试资料，也不据当前工程测试声明完整合规。

<code>HsmsDataTransactionManager</code> 仍是内部 actor；公共调用方通过
<code>HsmsConnection</code> 使用其动态发送、入站回复和事件能力。公共
生命周期与取消语义见 [HSMS-CONNECTION.md](HSMS-CONNECTION.md)。
