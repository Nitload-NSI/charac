# 服务宿主与本机通信

| 平台 | 系统入口 | 用户代理通信 |
| --- | --- | --- |
| Windows | Windows Service Control Manager；`AddWindowsService` | 带 SID ACL 的命名管道 |
| Linux | systemd system unit；`AddSystemd` | 带文件权限和对端凭据检查的 Unix domain socket |

Host 负责远程连接与控制权仲裁，用户代理持有工作区。服务账户具备配置读取与本机通信所需的权限。IPC 连接建立后验证协议版本、对端系统身份和目标账户。

Windows 代理在目标用户登录时启动。Linux 用户代理由用户上下文启动组件管理。本机通信路径按 host 与系统用户标识确定，进程启动后执行注册握手。

当前 Host 已集成系统服务生命周期与健康检查。IPC 注册属于后续集成项。部署入口见 [Windows](../../deploy/windows/README.md) 与 [Linux](../../deploy/linux/README.md)。

验收：服务随系统启动；服务停止完成资源清理；代理识别正确服务身份；其他账户访问 IPC 时按规则返回拒绝结果。

参考：[Windows Service](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)、[systemd 集成](https://devblogs.microsoft.com/dotnet/net-core-and-systemd/)。
