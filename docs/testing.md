# 验证与验收

NUKE Verify 构建解决方案、检查文档并运行工作区连接仲裁测试。配置 WORKSPACE_ACCESS_TEST_DATABASE 后还会在 PostgreSQL 18 专用测试库中执行迁移、唯一键/外键、授权隔离、过期与撤销等集成测试；用例通过 Npgsql 连接池访问数据库并在结束时清理自身测试数据，未配置时明确跳过。配置方式见[数据库验收](database.md)。核心测试覆盖同身份接管、旧 lease 拒绝、跨 issuer 身份边界、断线保留归属、释放及并发获取；新增 Client 回环 WebSocket 集成测试检查正式端点的 Bearer 请求头、仅含工作区 ID 的 query、输出接收与解除附着，以及无令牌和非回环明文端点的拒绝；自动化测试不模拟 OIDC 签名校验，也不连接真实 sshd。本机 `ssh probe` 与 `ssh broker-probe` 的手动操作见[SSH Broker](ssh-session.md)。已在开发机 `.100` 完成到 `.101` 的直接密码探测及 Session Manager `ls` 探测；demo 已通过真实 authentik public client 的 PKCE 令牌交换；正式 Client `login`/`connect` 的 Server 验签、授权与 SSH 交互整链路，以及未来 `.103` 部署路径待验收。CI 在 Windows 与 Linux x64 上运行 Verify 和 Publish，并在 Linux runner 上交叉发布 Android ARM64/Termux Client；设备运行仍需实机验收。另有 PostgreSQL 18 容器任务启用数据库集成测试。没有 `[oidc]` 配置时工作区路径返回 503；配置占位 OIDC 后，未认证的创建与附着请求在本机返回 401。完整命令见[单域名入口](client-server-transport.md)。

SSH 接入后，需要在真实目标系统验收：

| 领域 | Windows | Linux |
| --- | --- | --- |
| 服务 | Server SCM 生命周期和 sshd 配置 | Server systemd 生命周期和 sshd 配置 |
| 身份 | SSH 证书映射到允许的系统账户 | SSH principal 与 UID/GID 映射 |
| 终端 | OpenSSH/ConPTY、PowerShell 7、TUI、尺寸变化 | OpenSSH/POSIX PTY、shell、TUI、尺寸变化 |
| 会话 | WebSocket 断线期间继续输出、重连、接管与清理 | 同左 |
| 文件 | 实际 VirtioFS 驱动和权限 | 实际挂载、UID/GID 与权限 |

Android 客户端需在真实 Termux 环境检查 .NET 运行时、入口启动、OIDC 回调、键盘与终端原始模式、尺寸变化及网络恢复，见[Android CLI](platforms/android-client.md)。

还需检查目标主机密钥错误、证书过期、权限撤销、Server 重启和后端 SSH 中断时的行为。每次验收记录系统版本、配置、步骤和实际结果；存储细节见[VirtioFS 验收](platforms/storage.md)。
�


资源发现测试覆盖匿名请求在查询数据库前被拒绝、跨 subject/issuer 的会话隔离、已撤销目标与账户映射变更过滤、响应字段不含后端凭据，以及 CLI 返回旧会话/新建目标的选择、占用状态、空列表、刷新、退出和终端控制字符过滤。PostgreSQL 集成测试还检查目标列表仅执行一次查询，并在启用状态、有效期或授权变更后立即反映结果；未配置测试库时显式跳过。真实浏览器登录到菜单、选择 .101 交互及解除附着后重新选择的流程需要实机验收。

本机管理通道测试覆盖 `status`、`sessions`、日志 `reload`、过长指令拒绝、未启用时拒绝 `disconnect`、显式启用后的 subject 校验，以及断开控制者后旧输入租约失效且工作区归属保留。设备仲裁测试覆盖同一安装多连接、另一安装被拒绝、断开后的换机、旧租约不能释放新设备占用，以及客户端凭据的并发首次创建。来源地址测试验证只有 `[server] trusted_proxy` 匹配直接对端时才接纳一跳 `X-Forwarded-For`；真实 Caddy 部署、异机 Client 和远端断线仍需实机验收。
