# Linux Server 部署模板

执行 bash ./build.sh --target Publish --runtime linux-x64，将 artifacts/publish/linux-x64/server/ 安装到 /opt/workspace-access；ARM64 使用 linux-arm64。创建受限的 workspace-access 服务账户，安装 deploy/linux/workspace-access-server.service 并按本机路径调整。目标 Linux 主机单独部署 sshd，限制为内网访问。

```sh
sudo install -d -m 0750 /opt/workspace-access
sudo install -m 0755 char_rac_server /opt/workspace-access/char_rac_server
sudo install -m 0644 workspace-access-server.service /etc/systemd/system/workspace-access-server.service
sudo systemctl daemon-reload
sudo systemctl enable --now workspace-access-server
sudo systemctl status workspace-access-server
```

其余发布文件和运行时依赖也必须部署到同一目录。目标主机密钥、账户权限与网络策略需要另行配置；改用证书登录时再配置 SSH CA 信任。密码与私钥 SSH Broker、OIDC、Session Manager 和 WebSocket 已接入；`/health/ready` 在剩余验收项完成前仍返回 503。可在 systemd 的 `ExecStart` 末尾添加 `--config /etc/workspace-access/workspace-access.config`，由服务账户读取该文件，无需复制到发布目录。

本机管理通道只允许服务账户连接。服务启动后，可用相同的 `--config` 路径执行 `status` 和 `reload`，例如 `sudo -u workspace-access /opt/workspace-access/char_rac_server --config /etc/workspace-access/workspace-access.config status`。`reload` 目前只在线应用 `[Logging]`；其他配置变化需要计划性重启。服务单元没有 `PrivateTmp`，因为 .NET 在 Linux 上通过 `/tmp` 中的 Unix 域套接字实现命名管道；`UMask=0077` 与同账户检查限制本地访问。
