# 开发与 NUKE 构建

使用 .NET 10 或更高版本 SDK。global.json 设置最低 SDK 并允许更高版本；项目目标框架为 net10.0，入口使用显式 Program.Main。包版本集中在 Directory.Packages.props，锁文件固定依赖解析。

解决方案 WorkspaceAccess.slnx 包含 src/server/WorkspaceAccessServer.csproj、src/client/WorkspaceAccessClient.csproj、测试项目和 NUKE 项目。各项目的构建输出统一位于根目录 `temp/<项目名>/bin/`，中间文件位于 `temp/<项目名>/obj/`；`temp/` 被 Git 忽略。Windows 用 ./build.ps1，Linux 用 bash ./build.sh。

| NUKE 目标 | 行为 |
| --- | --- |
| Restore、Compile、Test | 依次恢复、构建和运行核心测试 |
| CheckDocs、Verify | 检查文档；Verify 组合文档与测试 |
| Publish | Verify 后按 RID 发布 server/ 与 client/ |
| RunServer、RunClient | 前台运行对应程序 |

Server 与 Client 共同支持 win-x64、win-arm64、linux-x64、linux-arm64；Client 另支持 linux-bionic-arm64（Android ARM64/Termux，依赖设备上的 .NET 10 运行时）。常用命令：

```powershell
./build.ps1 --target Verify --locked-restore
./build.ps1 --target Publish --runtime win-x64
./build.ps1 --target Publish --runtime linux-bionic-arm64
dotnet run --project src/server
dotnet run --project src/client -- --help
dotnet run --project src/client -- status --server http://127.0.0.1:5080
```

Server 支持全局 `--config <路径>` 指定 INI 文件，服务启动、`database migrate`、`ssh probe` 和 `ssh broker-probe` 均使用它；相对路径在开发仓库中优先按仓库根目录解析，也可使用绝对路径。未指定时，开发环境优先读取仓库根目录被 Git 忽略的 `workspace-access.config`（模板为 `workspace-access.config.example`），发布后需用 `--config` 指向外部文件，或自行把同名文件放在可执行文件旁。配置提供 `[server] domain` 和 `listen`，示例监听 http://127.0.0.1:5080；健康端点为 /health/live 与 /health/ready，Client 状态探测为 /status。单域名路由见[通信入口](client-server-transport.md)。就绪端点在 SSH 和认证集成前返回 503。变更依赖时更新集中版本与锁文件，交付前执行 Verify。平台行为验收见[验证与验收](testing.md)与[Android CLI](platforms/android-client.md)。

示例：`dotnet run --project src/server -- --config workspace-access.config ssh broker-probe`；此诊断现通过 Session Manager 发送 Linux `ls` 并显示输出。EF 工具可使用 `dotnet ef database update --project src/server -- --config workspace-access.config`。`--config` 不存在时会报错，不会回退到默认文件。完整 OIDC + WebSocket 的 CLI 命令见[通信入口](client-server-transport.md)。

数据库模型使用 EF Core 和 Npgsql。在根目录配置 `[database]` 的 `host`、`name`、`username` 等字段后运行 `dotnet run --project src/server -- database migrate`；正常启动不自动迁移。新增迁移与真实 PostgreSQL 集成测试见[数据库开发](database.md)。本机 `ssh probe` 命令可从计划部署 Server 的机器验证到目标 sshd 的主机密钥、密码认证和 PTY Shell；步骤见[SSH Broker](ssh-session.md)。
