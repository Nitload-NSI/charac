# 总功能描述

## 产品目标

Workspace Access Host 提供持续存在的字符终端工作区。用户完成 authentik OIDC 认证后，以已授权的系统用户身份访问 shell 与 TUI 程序。Windows 和 Linux 共享连接、授权和工作区模型，通过平台适配实现系统服务、用户执行上下文与伪终端。

产品使用单一 C# 项目，内部按目录组织架构。发布程序通过 `host` 和 `agent` 参数分别运行系统服务与用户代理角色，共享一套部署产物。

## 功能

| 功能 | 当前设计 |
| --- | --- |
| 身份来源 | authentik 管理外部身份，操作系统管理系统账户 |
| 身份关联 | 以 `(issuer, subject)` 标识外部身份，管理员配置系统账户映射与 host 访问规则 |
| 远程连接 | 客户端经 WSS 连接 broker，控制与终端数据按协议传输 |
| 连接独占 | 每个 host 一个有效远程连接；同身份显式接管时撤销旧控制权 |
| 工作区归属 | 归属独立于连接存在；断线后原身份可重新附着 |
| 终端持久性 | 用户代理持有 PTY 与进程，连接断开期间继续运行和读取输出 |
| 画面恢复 | 按终端状态快照与有序输出恢复光标、屏幕及滚动历史 |
| 输入控制 | 输入、终端缩放及会话变更均校验当前连接控制权 |
| 文件访问 | 遵循执行账户及实际存储后端权限；并行编辑使用独立工作目录 |
| 数据保存 | 应用负责文件保存，代理保存运行期间的终端状态；系统启动后建立新的运行会话 |
| 审计 | 记录认证结果、映射、连接获取、接管和会话结束等事件 |

## 平台差异

| 项目 | Windows | Linux | 详细说明 |
| --- | --- | --- | --- |
| 服务宿主与 IPC | Windows Service、命名管道 | systemd、Unix domain socket | [服务宿主](platforms/service-hosting.md) |
| 用户环境来源 | 目标用户已登录时启动用户代理，关联 SID | 为现有系统账户建立 UID/GID 用户上下文 | [用户环境](platforms/user-environment.md) |
| 伪终端 | ConPTY 与 Windows 进程 API | POSIX PTY、控制终端与进程组 | [终端后端](platforms/terminal-backend.md) |
| 权限与进程边界 | Windows 访问令牌、对象 ACL、Job Object | UID/GID、文件权限、cgroup 和进程组 | [权限与进程](platforms/process-security.md) |
| 用户会话生命周期 | 跟随用户登录会话，锁屏期间保持工作区 | 跟随用户会话宿主与服务策略运行 | [生命周期](platforms/session-lifecycle.md) |
| 文件系统与共享目录 | NTFS 或 VirtioFS；验证 SID 与宿主权限映射 | 原生文件系统或挂载目录；验证 UID/GID 与锁行为 | [存储访问](platforms/storage.md) |
| 系统管理权限 | Windows 用户权限分配和提升策略 | sudo、polkit 与服务权限策略 | [系统管理](platforms/system-control.md) |
| 发布与注册 | Windows RID、SCM 注册和用户登录启动 | Linux RID、systemd system/user unit | [发布部署](platforms/deployment.md) |

## 使用条件

Windows 标准运行方式是已登录用户代理。宿主进入等待状态后，目标用户登录即可建立代理上下文。部署可以选用专门的本地账户。Linux 的用户上下文由受控启动组件创建。两个平台的首期连接策略均为 host 级独占。

当前代码覆盖范围见[交付状态](implementation-status.md)，验收项见[验证文档](testing.md)。
