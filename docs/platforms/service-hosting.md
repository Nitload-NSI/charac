# 服务宿主

| 项目 | Windows | Linux |
| --- | --- | --- |
| Server | Windows Service | systemd service |
| SSH 服务 | OpenSSH sshd 系统服务 | OpenSSH sshd 系统服务 |
| 日志 | Windows 事件与应用日志 | journal 与应用日志 |

Server 作为独立服务运行，管理认证、SSH Broker 和工作区。目标 sshd 独立运行且只在受控内网接受来自 Server 的连接。Server 的存活端点与业务就绪端点分开；后者须以 OIDC、CA、SSH、Session Manager 和 WebSocket 的实际状态为准。当前实现服务入口、健康端点与客户端状态探测；公网域名由 `workspace-access.config` 的 `[server] domain` 指定，并由可信反向代理转发，见[通信入口](../client-server-transport.md)。部署见[平台部署](deployment.md)。

## 本机管理命令

运行中的 Server 在本机建立与配置文件绝对路径绑定的命名管道。新启动的 `char_rac_server --config <同一路径> status`、`sessions` 或 `reload` 作为管理客户端连接现有进程，不启动第二份 HTTP 服务，也不向运行中的进程注入代码。`status` 返回进程 ID 和当前 SSH 工作区数量；`sessions` 列出工作区 ID、OIDC issuer/subject、目标、系统账户、附着状态、时间以及当前 WebSocket 的连接来源；`reload` 重新读取 INI，仅在线应用 `[Logging]` 设置。修改监听地址、OIDC、数据库、SSH 目标或其他设置时，命令明确拒绝并提示重启，原配置和工作区继续运行。配置文件路径必须与服务启动时解析出的路径相同。

管道使用 `PipeOptions.CurrentUserOnly`，因此管理命令必须以与服务相同的系统账户执行。配置路径的哈希只用于定位管道，不是认证凭据。Linux 的命名管道底层使用 Unix 域套接字；部署模板保留 `UMask=0077`，并且不隔离 `/tmp`，以便该账户在另一个终端连接。Windows 服务使用独立账户时，管理命令也须在该账户上下文中运行。此管理通道不在 HTTP/WebSocket 或 Caddy 入口上暴露。必须限制服务账户的交互登录；同账户运行的进程可以调用该管道。连接来源默认取 Server 直接看到的网络对端；经 Caddy 代理时默认显示代理地址。若需要显示 Caddy 上游客户端地址，在 `[server] trusted_proxy=<Caddy 到达 Server 所用的 IP>` 中显式信任这一跳，Server 才读取 `X-Forwarded-For` 的最后一跳。其他来源传入的转发头不被信任。完整配置热替换需要逐组件设计切换与回滚，目前不支持。

切断用户连接需在 INI 中设置 `[management] allow_disconnect=true` 并重启 Server，默认关闭。管理员在服务账户上下文中运行 `char_rac_server --config <同一路径> disconnect --subject <OIDC-sub> --confirm`，Server 按配置的 issuer 和给定 subject 精确匹配，断开该身份当前附着的所有 Client/WebSocket，令旧连接的输入租约立即失效并记录日志。该操作同时释放当前 Client 设备的占用；SSH 工作区继续运行，用户持有效令牌且仍获授权时可以从任一设备重新连接。这是一次性断开操作，不改变授权；持续禁止访问须另行撤销数据库授权。命令需要显式 subject 和 `--confirm`，Server 还会检查启用开关；这些参数用于减少误操作，真正的本机权限边界是管道所属系统账户。`allow_disconnect` 不支持热重载。
