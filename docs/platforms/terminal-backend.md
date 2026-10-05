# 终端后端

| 项目 | Windows | Linux |
| --- | --- | --- |
| 伪终端 | 目标 OpenSSH 使用 ConPTY | 目标 OpenSSH 使用 POSIX PTY |
| Shell | 可将目标用户默认 Shell 设为 PowerShell 7 | 使用目标账户允许的 Shell |
| 尺寸 | SSH window-change 请求 | SSH window-change 请求 |

Server 只持有 SSH shell channel，转发输入、输出和尺寸变化，并管理客户端附着。伪终端创建、用户 Shell 启动与本地进程资源由目标 sshd 负责。需通过真实目标验收 PowerShell 7、交互提示、全屏 TUI、颜色、控制键和尺寸变化。Server 已实现内部密码 SSH Broker、PTY Shell、受保护的 WAN 会话入口与有界输出保持；真实目标上的 `ls` 和完整交互仍待验收，见[SSH 会话设计](../ssh-session.md)。
