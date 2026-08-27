# 仓库工作约定

## 标准工作流

1. 从最新 `main` 创建主题分支：`feat-*`、`fix-*`、`docs-*`、`ci-*`。
2. 提交信息使用 Conventional Commits 前缀和中文摘要。
3. 所有行为变化同步更新 `CHANGELOG.md` 的 `Unreleased` 段。
4. 通过 Pull Request 合并；CI 未通过时不得合并。
5. 不直接推送受保护的 `main`，不使用 force push。

## 提交前验证

```bash
dotnet build SecsFrame.slnx -c Release
dotnet test SecsFrame.slnx -c Release --no-build
```

协议行为变更必须同时包含协议向量测试或状态机测试。兼容模式必须默认关闭，并记录对应设备行为与互操作证据。

## 标准与版权

- 实现应记录对应的 SEMI 标准编号和版本，但不得把受版权保护的标准正文、表格或 Schema 提交到仓库。
- 未经完整标准和一致性测试验证，不得在文档中宣称完整合规。
- 参考 MIT 等开源实现时保留其许可证要求，不复制来源不明的协议材料。
