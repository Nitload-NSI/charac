# 仓库开发约定

## 入口

阅读 `docs/overview.md`、`docs/architecture.md` 和涉及的 `docs/platforms/` 文档。当前实现状态以 `docs/implementation-status.md` 为准。

## 实现

- 使用 .NET 10、nullable 和集中包版本管理。
- 通过 `build.ps1` 或 `build.sh` 调用 NUKE；交付前执行 `Verify`。
- 产品项目为 `src/WorkspaceAccessHost.csproj`，代码直接在 `src/` 下通过目录和命名空间区分职责。
- 领域逻辑保持操作系统与传输框架无关；平台调用分别放入 `Platforms/Windows`、`Platforms/Linux`。
- 同一程序通过 `host`、`agent` 参数选择进程角色，按目标平台发布一套产品产物。
- 连接接管、断线及输入派发遵守 host 独占和工作区归属规则。
- 认证、授权、用户映射完成后才可调用连接控制核心。
- 增加真实 PTY、IPC、OIDC 实现时，同步维护 readiness 和状态文档。

## 文档

文档库是面向所有仓库访问者的当前设计说明。使用中文正文和准确的 API 名称；描述目标行为、运行前提、实现状态和验收条件。保持总功能表中的每项平台差异与一份独立说明文档对应。新增页面加入导航，并通过 `CheckDocs`。
