# Windows 部署模板

当前产物用于服务宿主与代理框架验证，健康状态列出远程工作区的集成进度。

## 发布与目录

执行 `./build.ps1 --target Publish --runtime win-x64`，将发布目录整体部署到 `C:\Program Files\WorkspaceAccessHost`。ARM64 使用 `win-arm64`。服务与用户代理共用目录中的 `WorkspaceAccessHost.exe`，分别传入 `host` 和 `agent`。

服务账户需要读取安装目录和配置。框架阶段服务使用 LocalService。正式代理 IPC 的管道 ACL 配置与服务账户保持一致。

## 注册服务

在管理员 PowerShell 中执行：

```powershell
sc.exe create WorkspaceAccessHost binPath= '"C:\Program Files\WorkspaceAccessHost\WorkspaceAccessHost.exe" host' start= auto obj= 'NT AUTHORITY\LocalService'
sc.exe start WorkspaceAccessHost
sc.exe query WorkspaceAccessHost
```

健康地址为 `http://127.0.0.1:5080/health/live`。就绪地址为 `/health/ready`。日志由 .NET 日志提供程序输出。

## 用户代理

以目标普通用户运行 `WorkspaceAccessHost.exe agent` 验证入口。用户登录启动任务的可执行路径指向 `WorkspaceAccessHost.exe`，参数填写 `agent`，并选择目标用户的交互登录上下文。代理 IPC 注册与终端集成完成后，按部署策略启用该启动任务。

源仓库设计文档：`docs/platforms/service-hosting.md`、`docs/platforms/user-environment.md`、`docs/platforms/storage.md`。
