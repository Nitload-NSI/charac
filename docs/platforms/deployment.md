# 平台发布与部署

| 项目 | Windows | Linux |
| --- | --- | --- |
| 发布 RID | `win-x64`、`win-arm64` | `linux-x64`、`linux-arm64` |
| 产品入口 | `WorkspaceAccessHost.exe host` / `agent` | `WorkspaceAccessHost host` / `agent` |
| 服务管理 | SCM 注册、启动类型和账户配置 | systemd unit、服务账户和目录权限 |
| 用户代理启动 | 用户登录启动任务或用户启动项 | 用户上下文启动组件或 user unit |
| 本机通信路径 | 命名管道 | `/run` 下受控 socket 路径 |

NUKE `Publish` 先执行 Verify，再发布单一产品的 self-contained 产物，并复制平台部署模板。输出位于 `artifacts/publish/<runtime>/`，根目录包含可执行文件、配置与运行时依赖，`deploy/` 包含部署模板。Host 和 Agent 使用同一可执行文件，通过启动参数选择角色，整套产物统一部署和更新。

服务配置使用发布目录中的 `appsettings.json` 和 .NET 环境变量覆盖。初始 HTTP 端点位于 loopback。远程传输集成时由受控 TLS 入口提供 WSS，并配置 host 白名单、认证及 Origin 策略。

x64 的 Windows 与 Linux 验证流程由 CI 分别执行。ARM64 产物通过指定 RID 发布，并在对应硬件或虚拟机完成运行验收。

部署步骤见 [Windows](../../deploy/windows/README.md) 与 [Linux](../../deploy/linux/README.md)。服务安装由部署人员执行。
