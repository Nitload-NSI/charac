# 总功能描述

## 产品目标

Workspace Access Server 管理 Windows 与 Linux 上的远程 SSH 工作区。外部用户经兼容 OpenID Connect（OIDC）的身份提供方登录；Server 按经过验证的外部身份授权目标机器和系统账户，连接只在内网可达的 OpenSSH Server。对外接口为 HTTPS/WebSocket，客户端不直接接触目标 `sshd`。当前已使用 authentik 完成端到端验收。

Windows OpenSSH 提供 ConPTY 与用户 Shell，Linux OpenSSH 提供 POSIX PTY 与用户 Shell。Server 负责 SSH 信任、连接控制、工作区归属与输出保持，不自行实现平台 PTY 或用户代理。

## 功能与边界

| 功能 | 目标行为 |
| --- | --- |
| 身份 | OIDC 身份提供方负责用户认证；操作系统负责系统账户与文件权限 |
| 映射 | 以 `(issuer, subject)` 标识外部身份，管理员授权目标机器和系统账户 |
| SSH 信任 | Server 管理后端 SSH 凭据并校验目标主机密钥；SSH CA 是可选实现方式 |
| 外部传输 | CLI 经 HTTPS 登录、经 WebSocket 附着工作区 |
| Android 客户端 | ARM64/Termux CLI 发布；设备运行和交互能力待验收，见[Android CLI](platforms/android-client.md) |
| 工作区 | Session Manager 按工作区持有后端 SSH 连接，可管理多个工作区 |
| 控制权 | 每个工作区只有一个有效远程控制者；同身份可显式接管 |
| 断线 | 外部 WebSocket 断开只解除附着；Server 继续读取后端输出 |
| 恢复 | 同身份重新授权后附着，按终端状态与有序输出恢复画面 |
| 生命周期 | 首期持久性以 Server 进程和后端 SSH 连接存活为前提 |
| 审计 | 记录认证、映射、SSH 凭据、接管、连接及工作区结束事件 |

后端 SSH 凭据只解决 Server 到 sshd 的认证，不负责工作区持久性。Server 重启或后端 SSH 断线后的原会话恢复不属于首期保证。

## 平台差异

| 项目 | Windows | Linux | 详细说明 |
| --- | --- | --- | --- |
| 服务宿主与 SSH | Windows Service、Windows OpenSSH | systemd、OpenSSH | [服务宿主](platforms/service-hosting.md) |
| 用户环境 | Windows 账户与 SSH 登录令牌 | UID/GID 与 SSH 登录环境 | [用户环境](platforms/user-environment.md) |
| 终端后端 | sshd 使用 ConPTY | sshd 使用 POSIX PTY | [终端后端](platforms/terminal-backend.md) |
| 进程与权限 | Windows 访问令牌、ACL | UID/GID、文件权限 | [权限与进程](platforms/process-security.md) |
| SSH 会话生命周期 | Windows sshd 与服务策略 | Linux sshd 与服务策略 | [生命周期](platforms/session-lifecycle.md) |
| 文件系统 | NTFS 或 VirtioFS | 原生文件系统或挂载目录 | [存储访问](platforms/storage.md) |
| 系统管理 | Windows 用户权限与提升策略 | sudo、polkit | [系统管理](platforms/system-control.md) |
| 发布部署 | Windows RID、SCM、OpenSSH 配置 | Linux RID、systemd、OpenSSH 配置 | [发布部署](platforms/deployment.md) |

当前代码覆盖范围见[交付状态](implementation-status.md)，验收见[验证文档](testing.md)。
