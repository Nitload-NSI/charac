# 系统管理权限

| 能力 | Windows | Linux |
| --- | --- | --- |
| 本机关机和重启 | 系统用户权限分配，包含 `SeShutdownPrivilege` | 登录会话授权、polkit、sudo 或 root 权限 |
| 网络发起系统关机 | 对应远程管理权限及服务策略 | 对应远程管理服务和账户策略 |
| 提升执行权限 | Windows 用户令牌与提升机制 | sudo、capabilities 及服务管理接口 |

终端使用目标系统账户的权限。部署阶段明确该账户的系统管理能力，并使用操作系统策略控制可执行的管理操作。通过远程终端运行的程序属于目标机器上的本地进程。

复用登录用户时，账户级权限策略也作用于该用户的其他程序。需要不同权限边界的部署使用专用执行账户或经过验证的受限上下文。

管理入口与普通终端输入分别授权。系统管理操作记录请求身份、目标、时间和执行结果。系统启动后的工作区状态由[用户环境](user-environment.md)和[生命周期](session-lifecycle.md)定义。

验收使用系统权限查询和具有代表性的系统管理 API，确认工作区与管理入口符合配置。PTY 输入作为终端字节流处理。

参考：[Windows 关机 API 权限](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-exitwindowsex)。
