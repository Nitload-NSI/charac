# 贡献指南

首先阅读[总功能描述](docs/overview.md)、[架构](docs/architecture.md)与[开发流程](docs/development.md)。通过 `Verify` 后提交变更。

## 变更要求

- 产品代码直接位于 `src/`，领域逻辑按职责放入目录，原生调用位于 `Platforms/Windows` 或 `Platforms/Linux`。
- 修改平台行为时，同步更新总功能表和对应差异说明页。
- 文档面向仓库访问者，描述当前设计、运行条件、行为和验收标准。
- 新增身份、连接、生命周期行为时，为权限边界和并发结果增加测试。
- 密钥通过部署环境配置管理；示例使用占位值。
- 包版本集中维护在 `Directory.Packages.props`，依赖升级后更新锁文件并执行 `Verify`。

提交说明包含具体行为变化、验证结果及尚待平台环境验证的内容。
