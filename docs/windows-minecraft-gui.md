# Windows Minecraft GUI 示例

## 定位与前提

[示例目录](../examples/windows-minecraft-gui/)提供一个可选的 Windows 图形化前端，调用现有 `charac connect` 与 `charac cp`。它不修改 .NET Client、Server、OIDC 或控制权协议，也不属于 MSI 默认安装内容。

GUI 要求 Windows 10 1809 或更新版本、带 Tcl/Tk 的 Python 3.10 或更新版本，以及已安装的 Charac CLI。目标端要求 Linux、Python 3、tmux、可写的 Minecraft 目录、已有 `run.sh`，以及与配置匹配的 Forge 参数。GUI 仅针对这种 Minecraft/tmux 部署，不是通用 Windows/Linux 服务管理器。

目标账户应具有现有 Minecraft 文件及 tmux socket 的维护权限。所有上传、部署及控制命令都经 Charac 的认证工作区执行，并受目标账户权限约束；GUI 不使用 sudo、不取得目标 SSH 密码或私钥，也不读取 OIDC token 文件。浏览器认证仍由用户完成。若 Server 需要首次 TLS 公钥信任，应先在终端运行 `charac status --server <origin>` 并完成 CLI 的明确确认；GUI 不自动接受证书信任提示。

## 配置与启动

将示例复制到可写目录，复制 [config.example.json](../examples/windows-minecraft-gui/config.example.json) 为同目录的 `config.local.json`。该文件及运行日志均被忽略，示例默认值只含占位信息。

| 配置 | 含义 |
| --- | --- |
| `charac_exe` | 可选；为空时从 PATH 或默认安装目录查找。`CHARAC_EXE` 环境变量可覆盖它，供测试发布产物使用 |
| `server`、`target` | 实际 HTTPS Server origin 和被授权目标的 GUID；Target 不是临时工作区 ID |
| `target_name`、`local_folder` | 界面名称及文件选择器初始目录；目录为空时使用本机 Downloads |
| `upload_folder` | 目标 SSH 账户的 `~/characfile` 绝对路径，必须与 Server 实际上传位置一致 |
| `mc_folder`、`forge_args`、`mc_port` | Minecraft 根目录、匹配 Java 命令行的参数及游戏端口；示例值为 Forge 1.20.1 |
| `tmux_session` | tmux 会话名，默认为 `mc120` |
| `maintainer_group` | 可选的共享 socket 组；为空时保留当前组。配置的组必须已存在且当前用户有权使用 |

高级配置 `tmux_socket`、`tmux_pane` 可覆盖默认的 `<mc_folder>/.mc120.tmux.sock` 和 `<tmux_session>:0.0`。超时常量位于脚本顶部。配置修改后重新启动 GUI；未填写 Server 和 Target 时登录操作明确失败，不连接示例地址。

双击 [charac_gui.pyw](../examples/windows-minecraft-gui/charac_gui.pyw) 即可运行，无需命令行参数。界面文字及日志使用 ASCII；中文等输入路径保持原名传给 CLI，界面中以 Unicode 转义显示。点击文件条目可查看完整本地路径。

## 上传与部署

点击 **Login** 后，GUI 在后台 ConPTY 中执行 `charac connect`，等待浏览器登录、SSH 附着及只读状态查询成功。连接、上传和服务控制在工作线程执行，主窗口继续处理事件。

拖拽文件到窗口或点击 **Choose Files**，然后使用 **After upload** 选择行为：

- **Upload folder only**：默认只上传到 `characfile`，不猜测文件用途。
- **Copy to MC config (backup first)**：上传后复制到 `<mc_folder>/config`。
- **Copy to MC mods (backup first)**：上传后复制到 `<mc_folder>/mods`。

点击 **Upload** 上传整个队列。选择部署时，先将所有被覆盖文件复制到 Minecraft 根目录下独立的 `config-backup-*` 或 `mods-backup-*` 目录，再覆盖目标并比较内容；路径越界、目录输入、符号链接目标或同批重名文件被拒绝。复制或校验失败时保留备份并报告原始异常，当前示例不自动回滚一批已部分复制的文件。

更新配置或模组的操作顺序是：**Login -> Stop Server -> 选择文件及部署目录 -> Upload -> Start Server**。普通文件上传可以仅使用默认上传目录，不需要停服。文件结果、备份目录及异常堆栈保存在窗口和脚本旁的 `.log` 文件中。

## 服务控制与生命周期

**Stop Server** 在确认 tmux 窗格是 Minecraft Java 进程的祖先后发送原生 `stop` 命令，等待进程退出及端口关闭，不强制终止 Java。停服前设置 `remain-on-exit`，以便复用停止后的窗格。

**Start Server** 先检查 Java 进程及端口，拒绝重复启动或替换活动窗格；停止的窗格使用不带强制选项的 `respawn-pane`，会话不存在时创建新的 tmux 会话。在既有目录运行 `bash run.sh`，并等待监听端口与本次启动的 `Done` 日志。状态查询不依赖旧启动日志，因此日志轮转不会把长期运行的服务误报为启动中。

查询每 30 秒更新一次，也可使用 **Refresh**。遇到错误后需重新检查状态。**Disconnect** 或关闭 GUI 只解除 Client 附着；Server 继续持有 SSH 工作区，Minecraft 进程由目标 tmux 保持。上传、开服或停服进行中时需要等待操作完成再关闭；等待浏览器登录时可以关闭窗口取消连接。工作区生命周期仍受现有 [Server/SSH 边界](architecture.md)约束。

## 验证范围

本地 Windows 检查命令如下，不连接真实目标，也不发送真实开停服命令：

```powershell
python examples/windows-minecraft-gui/test_charac_gui.py
```

测试覆盖原生 `WM_DROPFILES`、中文及空格路径、文件选择回调、后台任务完成、真实 ConPTY、连接成功提示、长响应换行、子进程失败、未配置登录拒绝，以及模拟开停服、日志轮转、备份和路径校验。CLI `--help` 冒烟检查需要已安装的可执行文件；未安装时跳过此项，也可使用 `CHARAC_EXE` 指向已发布的程序。Windows CI 在发布 CLI 后执行同一测试。

已在 Windows、Python 3.12/3.13 和 Charac 0.3.0 上验证浏览器登录、GUI 后端状态查询，以及含空格和中文文件名的 1 MiB 上传；远端大小和 SHA256 匹配后清理了测试文件。真实开停服、异常中断和首次 TLS 信任等交互仍需在隔离部署中进一步验收，模拟检查不代替这些平台验收。
