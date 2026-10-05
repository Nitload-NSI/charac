# Workspace Access

面向 Windows 和 Linux 目标的远程 SSH 工作区管理服务，使用 .NET 10 开发；CLI 客户端另支持 Android ARM64 的 Termux 终端发布。服务端与 CLI 客户端分别位于 `src/server/` 和 `src/client/`。

## 目标架构

客户端通过 authentik OIDC 登录，经 HTTPS/WebSocket 连接 Server。Server 校验身份与目标系统账户映射，使用后端 SSH 凭据连接内网 Windows 或 Linux 的 `sshd`。Session Manager 持有每个工作区的 SSH 连接，客户端断线只解除附着；Server 持续消费输出，同一身份重连时重新附着。Windows OpenSSH 负责 ConPTY 和用户 Shell，Linux OpenSSH 负责 POSIX PTY。

SSH CA 是后端凭据的可选方案；正式用户登录使用 OIDC。首期工作区在 Server 与后端 SSH 连接存活期间持续运行；Server 重启或后端 SSH 断线后的恢复另行验收。

## 当前状态

当前已实现 Server 服务入口、EF Core/PostgreSQL 授权、OIDC 令牌校验、密码 SSH Broker、Session Manager 与 WebSocket；Client 提供 `login` 登录验证及 `connect` 的资源选择、交互创建/重新附着。demo 的真实 authentik PKCE 登录已通过，正式 Client 登录和 Server 验签已实测通过，资源选择 → SSH 整链路仍待验收，托管后端 SSH 凭据、自动重连和画面恢复待完善。`/health/live` 返回 200，`/health/ready` 返回 503，检查真实数据库状态并列出待集成项，数据库配置与迁移见[数据库文档](docs/database.md)。

## 构建与运行

安装 .NET 10 或更高稳定版 SDK，在仓库根目录运行：

```powershell
./build.ps1 --target Verify
./build.ps1 --target RunServer
dotnet run --project src/client -- status --server http://127.0.0.1:5080
dotnet run --project src/client -- login --server http://127.0.0.1:5080
dotnet run --project src/client -- connect --server http://127.0.0.1:5080
./build.ps1 --target Publish --runtime win-x64
```

Android 客户端可用 `./build.ps1 --target Publish --runtime linux-bionic-arm64` 发布，详见[Android CLI](docs/platforms/android-client.md)。Linux 使用 `bash ./build.sh`。Server 使用 INI 配置域名和监听地址；开发时读取仓库根目录的 `workspace-access.config`，发布时用 `--config <路径>` 指向独立配置文件。构建中间文件与二进制统一位于根目录 `temp/<项目名>/`。Windows/Linux 发布产物分别位于 `artifacts/publish/<runtime>/server/` 与 `client/`；Android/Termux 仅发布 `client/`。

## 目录

```text
src/server/                      Server 项目与连接仲裁
src/client/                      CLI 客户端项目
tests/WorkspaceAccessServer.Tests/ 核心测试
build/                           NUKE 构建
docs/                            设计、状态与验收文档
deploy/                          Windows Service 与 systemd 模板
```

阅读[文档库](docs/README.md)、[单域名通信入口](docs/client-server-transport.md)、[架构](docs/architecture.md)、[交付状态](docs/implementation-status.md)和[贡献指南](CONTRIBUTING.md)。