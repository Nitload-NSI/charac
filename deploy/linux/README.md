# Linux 部署模板

当前产物用于服务宿主与用户代理框架验证。远程工作区各组件的状态由 readiness 报告。

## 发布

执行 `bash ./build.sh --target Publish --runtime linux-x64`。将发布目录整体部署到 `/opt/workspace-access/`。ARM64 使用 `linux-arm64`。服务与用户代理共用同一程序，通过 `host`、`agent` 参数选择角色。赋予入口文件执行权限：

```bash
sudo chmod 0755 /opt/workspace-access/WorkspaceAccessHost
```

## 系统服务

由部署人员建立 `workspace-access` 系统用户及同名组，并赋予安装目录读取和执行权限。把 `workspace-access-host.service` 复制到 `/etc/systemd/system/` 后执行：

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now workspace-access-host.service
systemctl status workspace-access-host.service
journalctl -u workspace-access-host.service
```

模板使用低权限 broker 账户，运行目录为 `/run/workspace-access`，状态目录为 `/var/lib/workspace-access`。用户上下文启动组件按专门权限边界接入。HTTP 健康端点位于 `127.0.0.1:5080`。

## 用户代理

`workspace-access-agent.service` 为 systemd user unit。以目标用户身份将其放入 `~/.config/systemd/user/`，通过 `systemctl --user daemon-reload` 和 `systemctl --user enable --now workspace-access-agent.service` 启动。用户管理器随登录和 linger 策略运行，部署时明确相应生命周期。

此模板用于验证现有用户上下文中的 Agent 入口；完整的按需账户启动流程在用户启动组件中实现。

源仓库设计文档：`docs/platforms/user-environment.md`、`docs/platforms/session-lifecycle.md`、`docs/implementation-status.md`。
