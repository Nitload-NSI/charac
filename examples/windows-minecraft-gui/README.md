# Windows Minecraft GUI 示例

这是调用现有 Charac CLI 的可选 Python 前端，支持文件拖拽、文件选择、浏览器登录、上传及 Linux Minecraft 服务端开停。代码入口为 [charac_gui.pyw](charac_gui.pyw)，完整配置、权限边界和验证方法见[使用说明](../../docs/windows-minecraft-gui.md)。

复制 [config.example.json](config.example.json) 为同目录的 `config.local.json`，填写实际 Server、Target 和目标目录，然后双击脚本。仅使用 Python 标准库，不需要 pip 安装；本机配置和运行日志已加入 Git 忽略规则。
