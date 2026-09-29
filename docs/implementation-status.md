# 交付状态

## 框架已实现

| 模块 | 可验证内容 |
| --- | --- |
| 解决方案 | 单一 .NET 10 产品项目、测试项目、NUKE 项目、集中包版本与依赖锁文件 |
| NUKE | Restore、Compile、Test、CheckDocs、Verify、Publish、RunHost、RunAgent |
| Host | `host` 角色、ASP.NET Core 入口、Windows Service / systemd 生命周期集成、loopback 健康端点 |
| Agent | `agent` 角色、目标用户进程入口、日志、宿主退出响应 |
| 平台层 | `Platforms/Windows` 与 `Platforms/Linux` 目录及平台描述 |
| 核心契约 | 外部身份、终端尺寸与 PTY 会话接口 |
| 连接仲裁 | host 独占、显式接管、断线保留归属、过期 lease 拒绝与并发控制 |
| 开发治理 | 平台差异文档、链接和导航校验、双平台 CI、服务部署模板 |

## 集成工作

| 阶段 | 实现内容 | 验收入口 |
| --- | --- | --- |
| 用户代理 | Windows 登录启动、命名管道；Linux 用户启动组件与 socket | 系统账户归属及 IPC 对端校验 |
| 终端后端 | ConPTY、POSIX PTY、输入输出与进程清理 | 交互 shell、尺寸变更、退出及断线运行 |
| 认证授权 | authentik OIDC、账户映射、host 授权及撤销 | 两个外部身份和两个系统账户的访问测试 |
| 传输接入 | WSS、显式接管、心跳、有序输入 | 旧连接输入拒绝与网络恢复 |
| 终端状态 | 屏幕状态、输出历史、快照同步 | 全屏编辑器重连后的画面一致性 |
| 存储验收 | 目标 VirtioFS 版本与挂载配置 | 权限、变更通知、锁与保存行为 |

## 健康状态

`/health/live` 表示 HTTP 宿主存活。`/health/ready` 在框架阶段返回 HTTP 503，列出 OIDC、代理 IPC、PTY 和 WebSocket 集成项。就绪检查在接入真实组件后按实际依赖状态计算。

构建和发布证明代码与依赖可生成目标平台产物。操作系统用户上下文、服务安装、PTY 和 VirtioFS 行为通过目标系统上的集成验收确认。
