# 会话生命周期

| 阶段 | Windows | Linux |
| --- | --- | --- |
| 创建 | Server 通过 SSH 请求目标 OpenSSH 创建 ConPTY Shell | Server 通过 SSH 请求目标 OpenSSH 创建 POSIX PTY Shell |
| 断开客户端 | Server 保持 SSH channel 并持续读取输出 | 同左 |
| 后端中断 | 原 SSH channel 消失 | 同左 |

当前 `SshWorkspaceManager` 按工作区分别保存身份归属、控制权、SSH channel 和最近 256 KiB 输出。一个工作区同时只有一个输入控制者；Server 可管理多个工作区。客户端断线不终止 SSH；显式结束、后端 SSH EOF 或 Server 停止会释放资源。后端结束时，Server 会先排空该观察者队列，再通过 WebSocket 发送 `ended` 事件并关闭连接；接管造成的旧观察者断开不会声称 SSH 已结束。撤销授权会阻止新的附着与结束请求，但尚不会自动终止已经附着的连接。Server 重启无法凭 SSH CA 恢复已经断开的交互终端，如需跨重启持久化，应设计目标侧会话管理。协议见[连接与会话](../connection-protocol.md)。

正式工作区创建时，Server 将身份键、目标、系统账户、认证方式与创建时间写入 PostgreSQL `access.workspace_records`。正常关闭会补写结束时间和原因，Server 正常停止记录 `server_stopped`；崩溃可能留下未补写的历史行。历史行始终保留，但它不是持久化的 SSH channel，也不会在重启后出现在可附着资源列表。测试用例只清理各自创建的临时数据，Server 停止不清理已登记身份、密钥或历史记录。

同一 OIDC 身份的活跃 Client 连接由 Server 按安装凭据仲裁：一台 Client 可同时附着多个工作区，另一台 Client 在前者仍有活跃连接时被拒绝。所有 WebSocket 断开后，设备占用释放，但 SSH 工作区继续运行。管理端 `disconnect` 仅中断该身份当前的 Client/WebSocket 并释放占用；它不删除工作区或修改目标授权。


资源列表仅暴露当前身份仍有权访问且账户映射一致的存活工作区，并携带创建时间及 `attached`/`detached` 状态。列表是快照，不预占控制权；查询后退出、接管、授权撤销等变化以实际附着请求的结果为准。Server 不为资源列表探测或新建 SSH 连接。
