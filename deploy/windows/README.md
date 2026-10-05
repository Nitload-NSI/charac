# Windows Server 部署模板

执行 ./build.ps1 --target Publish --runtime win-x64，将 artifacts/publish/win-x64/server/ 部署到受保护的服务目录；ARM64 使用 win-arm64。发布目录中的 char_rac_server.exe 是唯一服务端入口，不需要 host 或 agent 参数。

以受限服务账户注册 Windows Service，并按环境配置监听地址及反向代理。目标 Windows 主机需单独安装和配置 OpenSSH Server，限制为内网访问，并配置目标账户映射和服务器主机密钥验证；改用证书登录时再配置 SSH CA 信任。密码和私钥 SSH Broker、OIDC、Session Manager 与 WebSocket 已接入；正式 Client 已在 Windows 开发机经 authentik 连接 Linux `.101` 并执行交互命令，目标 Windows sshd 尚待验收。可用 `--config` 指向单独维护的 INI 文件，无需复制到发布目录。

示例（请先按实际路径与服务账户调整）：

```powershell
sc.exe create CharacServer binPath= '"C:\Program Files\Charac\char_rac_server.exe" --config "C:\ProgramData\Charac\workspace-access.config"' start= auto obj= 'NT AUTHORITY\LocalService'
sc.exe start CharacServer
sc.exe query CharacServer
```

/health/live 返回存活状态；/health/ready 在集成完成前返回 503。

本机 `status` 和 `reload` 命令连接正在运行的服务，不启动第二份实例。命令使用与服务启动时相同的 `--config` 路径，并须在服务账户上下文中执行；上面的 `LocalService` 示例不能直接从普通管理员终端调用同账户管道。`reload` 当前只在线应用 `[Logging]` 设置，其余配置修改需要重启服务。
