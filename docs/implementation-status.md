# 交付状态

## 已实现

| 模块 | 当前能力 |
| --- | --- |
| 解决方案 | .NET 10 Server、CLI Client、测试及 NUKE 项目；集中包版本与锁文件；构建产物统一放在 `temp/<项目名>/` |
| Server | 独立入口、Windows Service / systemd 集成、INI 配置、同账户本机管道 `status`、`sessions`、日志配置 `reload` 和默认关闭的 `disconnect`、OIDC JWT Bearer 验证、单身份单活跃 Client 设备仲裁、存活与就绪端点；受保护的资源列表、工作区创建、WebSocket 附着和结束路由；显式回环诊断入口 |
| 数据层 | EF Core 10 / Npgsql / PostgreSQL 18；身份、目标、主机公钥、普通 SSH 登录密钥引用、用户 CA、授权与工作区历史模型；迁移命令、本机目标/主机公钥/授权登记、`endpoint_regist` 三参数交互登记及私钥导入、单次查询授权解析及数据库 readiness，见[数据库](database.md) |
| Client | CLI 用户级 INI 默认 Server 入口及命令覆盖、状态探测、`login` 浏览器 OIDC PKCE 登录及 Server 身份验证、`connect` 登录后选择已有会话/新建目标，支持刷新列表及直接指定 ID；安装级设备凭据持久保存并随正式请求发送；正式交互创建/重新附着；一次性 `ls` 诊断保留；无 OIDC 的回环 `probe-session` 与交互式 `shell` 诊断命令；Windows MSI 和 Linux RPM 可用；Android ARM64/Termux DEB 当前不可用，暂不作为交付包，见 [Android CLI](platforms/android-client.md) |
| 连接仲裁 | WorkspaceConnectionGate 管理单个工作区的身份归属、控制权接管、断线与过期 lease 拒绝；已接入 WebSocket 输入派发 |
| SSH Broker 与会话 | SSH.NET 密码及登录密钥建连、授权账户与目标解析、主机公钥固定校验、PTY Shell、独立工作区控制权、有界输出与断线保留；本机 `broker-probe` 可发送 Linux `ls`，见[SSH 会话设计](ssh-session.md) |
| 构建与打包 | Restore、Compile、Test、CheckDocs、Verify、Publish、RunServer、RunClient；Windows x64 Client MSI、Linux x64 Server/Client RPM 可用；Termux aarch64 Client DEB 虽可生成但当前不可用，已标记为问题包，不作为交付包；Android 调试 APK 仅保留为辅助诊断；Client Windows x64 Native AOT 试编译成功，尚未作为默认包 |

旧 Host/Agent、PowerShell 标准流后端及自行管理 PTY 的代码已移除。Server 的受保护调用入口和一次性 CLI 验收路径已接通；回环无 OIDC 的 `probe-session` 已在 `.100 → .101` 实测通过，`shell` 的持续键盘输入和 VT 画面已在 VS Code 终端试用。Windows VT 鼠标转发已接入但尚待复测；2026-10-05 托管 SSH 登录密钥已通过本机 `key-probe` 完成 `.100 → .101` 数据库授权、主机公钥、PTY 和 `ls` 实测；登录密钥登记只保存受保护私钥的文件引用，不要求 `.pub` 副本，目标主机公钥固定校验仍保留；正式 Client 已实测通过 authentik 登录、资源选择及 `.101` 交互式 `ls`；SSH 正常退出时的 WebSocket 关闭修复待复测。正式 Client 的 `login` 已实测通过真实 authentik 浏览器 PKCE 登录与 Server 令牌验证；资源选择到 SSH 交互主链路已在 `.100 → .101` 实测，见[通信入口](client-server-transport.md)。

## 待实现

| 组件 | 验收条件 |
| --- | --- |
| 认证授权验收 | 真实 authentik public client 下的 PKCE 登录、令牌验证、数据库身份映射及交互命令已实测；仍需跨设备和撤销验收 |
| 后端 SSH 凭据验收 | Server 的密钥登记、授权绑定和公钥认证已在 `.100 → .101` 实机通过本机诊断；正式 OIDC Client 已连接 `.101` 并执行 `ls`；正常退出和重新附着待复测。SSH CA 为可选方案 |
| Session Manager 完善 | 在线授权撤销、后端异常与慢客户端的完整验收、屏幕快照 |
| WebSocket 协议完善 | 心跳、丢失输出恢复和终端状态快照 |
| 客户端终端 | 正式 `connect` 的持续交互及 `ls` 已实测；尺寸变化、正常退出及重新附着待复测；工作区选择菜单已实现，自动重连待实现 |
| 目标平台验收 | Windows/Linux sshd、用户环境、终端应用和 VirtioFS |

/health/live 表示 HTTP 进程存活。/health/ready 当前仍返回 HTTP 503，实际检查数据库连接、迁移与读取权限，并列出尚未完成的 ssh-backend-credential、terminal-client 和 session-e2e-validation；缺少 OIDC 配置或数据库未就绪时还会列出相应项。Server 进程或后端 SSH 连接终止后，原终端是否可恢复取决于另行部署的目标端持久化设施；当前设计只保证客户端断线期间由运行中的 Server 持有连接。
