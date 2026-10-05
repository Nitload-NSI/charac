# 进程与安全边界

| 项目 | Windows | Linux |
| --- | --- | --- |
| 目标进程权限 | sshd 按目标账户令牌创建 | sshd/PAM 按目标 UID/GID 创建 |
| Server 权限 | 独立服务账户，保护 CA 凭据 | 独立服务账户及受限文件权限 |
| SSH 配置 | Windows OpenSSH 服务配置 | Linux sshd 与 PAM 配置 |

Server 必须先验证 OIDC、授权、账户映射和目标主机密钥，再建立 SSH 连接。目标 sshd 的账户权限限制文件与系统操作；不能把 Server 服务账户权限传给终端用户。证书应短期有效且限定 principal。关闭工作区时先尝试正常关闭 SSH channel，再按明确策略处理残留目标进程；目标进程清理能力须在实际 sshd 环境验收。