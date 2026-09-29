# 终端后端

| 项目 | Windows | Linux |
| --- | --- | --- |
| 伪终端 | ConPTY | POSIX PTY master/slave |
| 进程连接 | 伪控制台通过扩展启动属性关联用户进程 | session、控制终端、标准流和前台进程组 |
| 尺寸变更 | `ResizePseudoConsole` | 窗口尺寸 ioctl 与终端信号行为 |
| 生命周期 | 用户进程、管道和伪控制台句柄 | 子进程、进程组和 PTY 文件描述符 |

共享接口为 `IPtySessionFactory` 与 `IPtySession`。终端输入输出按字节传递；用户代理负责持续读出、缓冲边界及终端状态。平台实现明确句柄所有权、取消和资源释放顺序。

Windows 后端在用户代理上下文中创建 shell。Linux 后端使用适合托管运行时的原生启动边界完成进程和终端设置，子进程进入目标程序前完成系统调用配置。

验收覆盖交互 shell、UTF-8、方向键、Ctrl+C、全屏编辑器、尺寸变更、大量输出、子进程退出及显式终止。断线后的输出持续被消费，重连恢复见[连接协议](../connection-protocol.md)。

参考：[ConPTY 会话](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)、[Linux PTY API](https://man7.org/linux/man-pages/man3/openpty.3.html)。
