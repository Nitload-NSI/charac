# Workspace Access Host

面向 Windows 和 Linux 的持久终端工作区服务，使用 C# / .NET 10 开发，使用 NUKE 统一构建、测试、文档校验和发布。

产品设计：通过 authentik OIDC 认证后，经 WebSocket 连接宿主上的用户终端。每台宿主拥有一个有效远程控制连接，终端进程在连接断开期间持续运行。

## 当前交付

当前处于框架阶段。已提供服务与用户代理入口、平台契约、单连接控制核心及测试、部署模板和文档库。OIDC、WebSocket、代理 IPC、ConPTY / POSIX PTY 和画面恢复处于待实现阶段。服务启动后，存活检查返回 200，就绪检查返回 503 并列出集成项。

## 快速开始

安装 [global.json](global.json) 指定的 .NET SDK，在仓库根目录执行：

```powershell
# Windows PowerShell
./build.ps1 --target Verify
./build.ps1 --target RunHost
```

```bash
# Linux
bash ./build.sh --target Verify
bash ./build.sh --target RunHost
```

宿主默认监听 `http://127.0.0.1:5080`，健康检查为 `/health/live` 与 `/health/ready`。用户代理入口为 `RunAgent`。使用 Ctrl+C 结束前台开发进程。

发布目标平台的独立运行产物：

```powershell
./build.ps1 --target Publish --runtime win-x64
./build.ps1 --target Publish --runtime linux-x64
```

产物位于 `artifacts/publish/<runtime>/`，包含 `WorkspaceAccessHost` 程序、运行时依赖、配置和 `deploy/` 部署模板。同一程序通过参数选择角色：

```text
WorkspaceAccessHost host
WorkspaceAccessHost agent
```

Windows 使用 `WorkspaceAccessHost.exe`。两个角色在各自的进程和系统用户上下文中运行。

## 阅读入口

- [文档库](docs/README.md)
- [总功能与平台差异表](docs/overview.md)
- [架构与项目职责](docs/architecture.md)
- [开发与 NUKE 构建](docs/development.md)
- [交付状态与后续实现](docs/implementation-status.md)
- [贡献指南](CONTRIBUTING.md)
- [仓库开发约定](AGENTS.md)

## 目录

```text
src/                            单一产品项目
  WorkspaceAccessHost.csproj     产品项目文件
  Program.cs                    角色选择入口
  Core/                         共享模型与契约
  Authentication/               外部身份与认证
  Connections/                  连接归属与控制权
  Terminals/                    PTY 契约与终端逻辑
  Platforms/Windows/            Windows 适配
  Platforms/Linux/              Linux 适配
  Hosting/                      服务宿主角色
  Agent/                        用户代理角色
tests/WorkspaceAccessHost.Tests/ 测试项目
build/                          NUKE 构建项目
docs/                           产品、架构、平台差异及开发文档
deploy/                         Windows Service 与 systemd 部署模板
```
