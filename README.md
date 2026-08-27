# SecsFrame

基于 [StreamFrame](https://github.com/CSJ608/StreamFrame) 的 .NET SECS-II / HSMS-SS / GEM 通讯组件。

> 当前处于早期开发阶段，尚未声明通过 SEMI 一致性认证，也不建议用于生产设备。

## 设计目标

- 基础层动态表达任意 SECS-II 消息，不要求预先声明所有 SxFy 模板。
- HSMS 会话、SECS-II 事务和 GEM 能力分层实现。
- 同时支持 Host 与 Equipment 角色，不把连接主动/被动模式与业务角色绑定。
- 标准严格模式为默认行为；现场设备兼容选项显式启用并有回归测试。
- 支持 SML 调试格式，并逐步支持 SEMI E173 SMN 与 E172 SEDD。

## 规划包

| 包 | 职责 |
|---|---|
| `SecsFrame` | SECS-II 数据模型、HSMS-SS 会话与事务基础 |
| `SecsFrame.Gem` | GEM 通用状态模型与能力服务 |
| `SecsFrame.Smn` | E173 SMN 日志、文档与消息表示 |
| `SecsFrame.Sedd` | E172 SEDD 设备接口数据字典 |

当前代码只覆盖第一阶段的 HSMS 线上帧基础。路线图见 [docs/ROADMAP.md](docs/ROADMAP.md)。

## 构建

```bash
dotnet build SecsFrame.slnx -c Release
dotnet test SecsFrame.slnx -c Release --no-build
```

## 许可证

[MIT](LICENSE)
