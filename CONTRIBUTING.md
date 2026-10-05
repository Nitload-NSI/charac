# 贡献指南

首先阅读[总功能描述](docs/overview.md)、[架构](docs/architecture.md)与[开发流程](docs/development.md)。提交前运行 `Verify`。

服务端代码放在 `src/server/`，CLI 客户端放在 `src/client/`。连接控制与身份边界应保持独立于 SSH 库和 WebSocket 框架。修改 Windows 或 Linux 的 SSH 部署行为时，同步更新对应平台文档和验收项。

包版本集中维护在 `Directory.Packages.props`。新增身份、控制权、断线或会话生命周期行为时，添加能够验证边界的测试。凭据通过部署环境管理，示例使用占位值。提交说明包含行为变化、验证结果和尚待平台环境验收的内容。