# 文件存储与 VirtioFS

| 项目 | Windows | Linux |
| --- | --- | --- |
| 本地文件 | NTFS 等本地卷，使用 Windows 文件 API 与权限 | 原生文件系统，使用 POSIX 文件 API 与权限 |
| 宿主共享目录 | Windows VirtioFS 驱动与 WinFsp | VirtioFS 客户端与宿主目录 |
| 身份映射 | 验证 SID、挂载配置和宿主 UID/GID 的关系 | 验证 UID/GID、附加组及宿主权限 |
| 变更与锁 | 验证 Windows 通知、共享模式和锁的传播 | 验证文件通知、锁及跨挂载访问行为 |

## 路径安排

服务配置、运行元数据及用户配置优先放在操作系统本地卷。项目文件可放在 VirtioFS 工作目录。挂载可用性和目标用户访问检查在创建工作区前执行。

并行编辑为本地操作与远程操作分配独立工作目录。代码项目可以采用独立 clone 或 Git worktree，通过版本控制整合修改。远程连接独占管理客户端控制权，文件写入由应用与存储权限决定。

## 兼容性记录

每个部署记录 PVE、virtiofsd、Windows virtio-win / WinFsp 版本、宿主文件系统、导出路径和挂载选项。

| 验证项 | 场景 |
| --- | --- |
| 可见性 | 目标用户代理上下文访问预期盘符或挂载点 |
| 权限 | 两个系统账户分别读取、创建、修改和删除文件 |
| 外部变更 | Windows 本地程序、宿主和其他 guest 修改文件后，编辑器观察变化 |
| 锁 | 不同进程及不同访问入口的独占锁和共享模式 |
| 保存 | 临时文件写入后重命名替换、刷新、重复保存 |
| 路径 | Unicode、大小写、长路径与符号链接 |
| 开发工具 | Git 操作、构建输出、文件监听和高频小文件访问 |

参考：[Windows VirtioFS](https://github.com/virtio-win/kvm-guest-drivers-windows/wiki/Virtiofs:-Shared-file-system)、[Git worktree](https://git-scm.com/docs/git-worktree)。
