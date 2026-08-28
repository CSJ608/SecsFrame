# 标准版本与版权边界

## 版本基线

以下版本来自 SEMI 官方目录在 2026-08-28 显示的 Current 版本。每个
协议切片开始前仍需使用团队合法获得的标准副本核对规范性条款和勘误。

| 层 | 标准版本 | 当前状态 |
|---|---|---|
| SECS-II 消息内容 | SEMI E5-0725 | Item 模型与二进制编解码已实现；消息与事务待实现 |
| HSMS / HSMS-SS | SEMI E37-0222 / E37.1-0819 | 只有帧基础；状态机待核对 |
| GEM | SEMI E30-0526 | 规划中 |
| SEDD | SEMI E172-0225 | 规划中 |
| SMN | SEMI E173-0721 | 规划中 |

公开目录链接：

- [SEMI E5](https://store-us.semi.org/products/e00500-semi-e5-specification-for-semi-equipment-communications-standard-2-message-content-secs-ii)
- [SEMI E37](https://store-us.semi.org/products/e03700-semi-e37-high-speed-secs-message-services-hsms-generic-services)
- [SEMI E30](https://store-us.semi.org/products/e03000-semi-e30-specification-for-the-generic-model-for-communications-and-control-of-manufacturing-equipment-gem)
- [SEMI E172](https://store-us.semi.org/products/e17200-semi-e172-specification-for-secs-equipment-data-dictionary-sedd)
- [SEMI E173](https://store-us.semi.org/products/e17300-semi-e173-specification-for-xml-secs-ii-message-notation-smn)

## 实现前必须查证

- E5：Item 格式码、长度表示、Boolean 取值、ASCII/JIS-8 字符约束、
  消息头与事务规则，以及当前版相对上一版的技术变更。
- E37/E37.1：Single Selected Session 的状态转换、控制消息字段、拒绝
  原因、T5/T6/T7/T8 与连接终止规则。
- E30：通讯建立、在线/离线、变量、事件报告、报警、远程命令和时钟能力
  的必选/可选边界。
- E172/E173：允许分发的 Schema/表示形式、版本标识、扩展点和验证规则。

## 版权与合规边界

可以提交：

- 标准编号、版本、公开标题和独立撰写的行为摘要；
- 自主设计的 API、实现和测试；
- 自主构造的最小协议向量与互操作结果；
- 许可证允许的开源参考及其必要归属。

不得提交：

- SEMI 标准正文、表格、图、附录或大段近似改写；
- 从付费标准复制或机械转换的 Schema、消息目录和数据字典；
- 来源不明的抓包、厂商文档或一致性测试材料；
- 未经验证的“完整合规”“通过认证”等声明。

本项目只读参考 StreamFrame 和 MIT 许可的 secs4net 以交叉理解公开协议
实现，没有复制参考仓库代码。未来如引入实质性开源代码，必须保留对应
版权与许可证文本，并在变更说明中记录来源。
