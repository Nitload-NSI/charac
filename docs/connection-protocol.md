# 连接与会话协议

Server 按工作区管理一个后端 SSH 会话与一个当前输入控制者。CLI 通过 OIDC 登录和 HTTPS 创建工作区，再通过 WebSocket 附着。现已实现一次性 `ls` 验收路径和基本输入、尺寸、输出消息；完整交互终端、屏幕快照和断线自动重连尚未完成。路径见[单域名入口](client-server-transport.md)。

工作区创建时绑定外部身份、目标机器和系统账户。WebSocket 断开只解除附着和输入控制权；运行中的 Server 继续读取 SSH 输出并按有界策略保存状态。同身份可重连；显式接管后旧连接的输入必须被拒绝。其他身份不能附着已有工作区。显式结束或后端 SSH 退出时释放工作区。

`SshWorkspaceManager` 为每个工作区持有 `WorkspaceConnectionGate` 和 SSH 会话；不可伪造的 lease 标识控制权，接管与输入入队在同一临界区排序。独立后台任务持续读取 SSH 输出，最多保留最近 256 KiB、256 个输出块；附着时回放并继续发送。附着客户端的队列有界，满时对 SSH 读取施加背压，避免静默丢失 VT 控制序列；CLI 同时检查输出序号。断线期间的有限历史不是屏幕快照，重新附着后的完整画面恢复尚未实现。客户端 WebSocket 断开只解除附着，不关闭 SSH。

| 消息 | 目标行为 |
| --- | --- |
| Attach / Takeover | 选择工作区并取得或显式接管控制权 |
| Input / Resize | 经当前 lease 验证后写入 SSH channel，尺寸设定有边界 |
| Output | 从 SSH channel 持续读取并携带序列号 |
| Snapshot | 恢复屏幕、光标与历史边界；具体编码待确定 |
| Heartbeat / Detach | 检测失效连接并解除附着，保持后端 SSH |
| Terminate | 授权后关闭 SSH 会话及工作区 |

Server 重启或后端 SSH 连接中断不能仅靠 WebSocket 重连恢复原终端；若需要跨 Server 生命周期的持久终端，应另行设计目标侧会话设施。
