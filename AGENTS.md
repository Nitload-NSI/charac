# 仓库开发约定

## 入口

阅读 `docs/overview.md`、`docs/architecture.md` 和涉及的 `docs/platforms/` 文档。实现状态以 `docs/implementation-status.md` 为准。

## 实现

- 使用 .NET 10、nullable、集中包版本管理和依赖锁文件。SDK 接受 .NET 10 或更高稳定版本。
- 程序入口使用显式 `Program.Main`，不使用顶级语句。
- 产品模块优先使用具体类型，通过目录和命名空间划分；只有确实需要多种实现或明确替换边界时才引入接口。
- 服务端项目为 `src/server/WorkspaceAccessServer.csproj`，CLI 客户端项目为 `src/client/WorkspaceAccessClient.csproj`。
- Server 以系统服务运行，管理 authentik 身份、SSH CA、内网 SSH 连接及工作区；不自行启动用户 Shell 或实现平台 PTY。
- 每个工作区独立仲裁控制权。认证、授权和系统账户映射完成后才能获取控制权；接管与输入派发保持确定顺序。
- 客户端断线不能直接关闭后端 SSH 工作区。Server 停止或内网 SSH 断线后的行为必须显式定义并测试。
- 通过 `build.ps1` 或 `build.sh` 调用 NUKE，交付前执行 `Verify`。接入真实依赖时同步维护 readiness。

## 文档

文档库面向所有仓库访问者，使用中文正文和准确的 API 名称。描述目标行为、前提、实现状态与验收条件。总功能表的每项平台差异都链接一份独立说明文档；新增页面加入导航并通过 `CheckDocs`。