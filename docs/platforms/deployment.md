# 平台部署

当前提供可用的 Windows x64 Client MSI、Fedora/RHEL 系 Linux x64 Server RPM 和 Client RPM；Termux aarch64 Client DEB 已加入 Device Flow 候选实现，但尚未完成真实设备验收，不作为稳定交付包。RPM/MSI 由 .NET 10 发布目录生成；`packaging/package.ps1` 只负责封装，发布目录必须先经过 NUKE `Publish`（含 `Verify`）。Android/One UI 的 Termux 启动器 APK 仅作为可选回调桥，见 [Android 客户端](android-client.md)。

在仓库根目录执行：

```powershell
$env:NuGetAudit='false'
.\build.ps1 --target Publish --runtime linux-x64 --locked-restore
.\build.ps1 --target Publish --runtime win-x64 --locked-restore
.\build.ps1 --target Publish --runtime linux-bionic-arm64 --locked-restore
.\packaging\package.ps1 -Target ServerRpm
.\packaging\package.ps1 -Target ClientRpm
.\packaging\package.ps1 -Target ClientMsi
.\packaging\package.ps1 -Target ClientTermuxDeb
```

封装需要 [nFPM](https://nfpm.goreleaser.com/) 2.47.0 和 [WiX Toolset](https://wixtoolset.org/) 6.0.2。脚本默认从 `temp/tools/nfpm/nfpm.exe`、`temp/tools/wix.exe` 寻找，也可传 `-NfpmPath`、`-WixPath`。输出在 `artifacts/packages/<版本>/`，例如 `artifacts/packages/0.1.1/`；这两个工具和发布产物均不提交仓库。Fedora/RHEL 的 ARM64 RPM 尚未配置。

## Server RPM 首次部署前提

这套部署复用现有 authentik、PostgreSQL、Caddy 和内网 sshd，不需要在 Server RPM 内再部署一套身份平台。安装包只装程序、systemd 单元和非 root 服务账户，不会自动填入生产配置、创建数据库、配置反向代理或向目标 sshd 登记公钥。首次上线按下列顺序准备：

1. 确认 Server 主机能访问 PostgreSQL、authentik 的 OIDC discovery/JWKS，以及内网各目标的 SSH 端口；公网只暴露 Caddy 的 HTTPS 入口，不暴露 PostgreSQL 和目标 SSH。
2. 在 PostgreSQL 创建专用数据库和受限角色。安装 RPM 后将配置写入 `/etc/charac/workspace-access.config`，填 `[database]`、`[oidc]`、`[server]` 和 `[ssh_keys]`；文件只允许 `charac` 服务账户读取。用同一配置执行 `char_rac_server --config /etc/charac/workspace-access.config database migrate`，然后核对迁移结果。服务正常启动不会自动迁移。
3. 在 authentik 为 CLI 准备 public OIDC Provider，启用 PKCE 和本机动态 loopback 回调；Server 配置正确的 issuer、client ID 和 discovery URL。详情见[认证](../authentication.md)。
4. 创建服务账户持有的 `/var/lib/charac/keys`（目录 `0700`，私钥 `0600`），从可信渠道核对目标 sshd 主机公钥。使用本机 `endpoint_regist` 导入 Server 登录私钥、登记目标，并授权 OIDC subject 到目标 OS 账户；目标账户的 `authorized_keys` 也须信任相应登录公钥。详情见[SSH 登记](../ssh-session.md#三参数交互式端点登记)。
5. 配置 Caddy 的域名、TLS 和到 Server 监听地址的反向代理；需要读取真实客户端 IP 时再设置 `[server] trusted_proxy`。最后启动 `charac-server.service`，检查 `systemctl status` 与 `journalctl -u charac-server`，再从 Client 完成真实登录和 SSH 连接。

当前 `/health/ready` 还包含未完成能力标记，可能返回 503；不要将它作为首次部署成功的唯一判据。运行时可核对进程状态、数据库迁移、OIDC 登录和实际工作区连接。Server RPM 不启动 PostgreSQL 容器；数据库可以是独立容器或现有 PostgreSQL 实例。

Server RPM 安装自包含程序到 `/opt/charac/server`，安装 `charac-server.service` 和专用 `charac` 系统账户。包不会携带运行配置、数据库密码或 SSH 私钥，也不会在安装时自动启动服务。部署机上准备 `/etc/charac/workspace-access.config`（只允许服务账户读取），在 `[ssh_keys]` 指定 `/var/lib/charac/keys`，该目录及私钥由 `charac` 拥有且不允许其他用户访问。配置样例安装于 `/usr/share/doc/charac-server/workspace-access.config.example`。准备 PostgreSQL 数据库并执行迁移后，再用 `systemctl enable --now charac-server` 启动。服务监听地址和 Caddy 等反向代理按实际部署配置；Server 只需能访问内网 SSH 目标。详情见 [Linux 部署](../../deploy/linux/README.md)。


Server 运行时只需 `--config /etc/charac/workspace-access.config`。这一个 INI 保存监听地址、OIDC 信息、PostgreSQL 连接参数和 `[ssh_keys] directory`；它不负责启动 PostgreSQL 容器。PostgreSQL 容器及其数据卷应由独立的数据库部署管理，RAC 只使用受限数据库账户连接。`charac` 是非 root 的系统服务账户，`StateDirectory=charac` 负责 `/var/lib/charac`，`/var/lib/charac/keys` 建议设为该账户所有、目录 `0700`、私钥 `0600`。当前没有磁盘会话缓存或单独日志文件：工作区状态留在 Server 内存与数据库历史中，标准日志进入 systemd journal，可用 `journalctl -u charac-server` 查看。无需额外配置 `CacheDirectory` 或 `LogsDirectory`；将来确有持久快照或文件日志时再增加。服务通过高端口监听，由 Caddy 承接 443，无需为网络入口提升 Server 到 root。

服务单元保留 `ProtectSystem=strict`，并用 `ReadWritePaths=/tmp` 允许 .NET 在 `/tmp` 创建本机管理通道的 Unix socket 和 Data Protection 临时文件。不要启用 `PrivateTmp`：管理命令从服务外部以同一个 `charac` 账户连接该 socket，需要看到同一个 `/tmp`。已安装旧 RPM 的机器可先执行 `sudo systemctl edit charac-server`，在 `[Service]` 下添加 `ReadWritePaths=/tmp`，保存后运行 `sudo systemctl daemon-reload && sudo systemctl restart charac-server`；新版 RPM 将直接包含这一设置。

Client RPM 安装自包含程序到 `/opt/charac/client`，并提供 `/usr/bin/charac` 命令。Windows MSI 以 per-machine 方式安装到 `Program Files\nitload\charac`，会触发 UAC 管理员授权，并把安装目录加入系统 `PATH`；新终端可运行 `charac.exe --help`。MSI 内嵌所需文件，不依赖独立 CAB。两个 Client 包均不预置登录令牌或目标配置。

RPM 和 MSI 已完成本地构建验证，并在目标环境完成 Server RPM / Windows Client 的可用性验证；Termux DEB 的 Device Flow 候选实现尚未完成真实设备验收。MSI 仍需在正常启用 Windows Installer 服务的机器上完成安装、升级、卸载验收；受限环境里的 ICE 校验无法连接 Windows Installer 服务。
Client 可在用户级 INI 的 `[client] server` 写入默认 HTTPS origin，样例见仓库根目录 `client.config.example`；Client RPM 另安装到 `/usr/share/doc/charac-client/client.config.example`，MSI 则安装到程序目录。Windows 默认读取 `%APPDATA%\Charac\client.config`；Linux/Termux 默认读取 `$XDG_CONFIG_HOME/charac/client.config`，未设置时读取 `~/.config/charac/client.config`。首次执行无配置的 Client 命令会自动创建默认配置目录，但不会写入服务器地址。之后可直接执行 `charac connect`（Windows 为 `charac.exe connect`），或使用更短的 `charac connect https://access.example.com`；`--server <origin>` 可临时覆盖，`--config <路径>` 可在命令前选用其他配置。该文件只存入口地址，不存 OIDC 令牌；旧版 `%APPDATA%\CharRAC`、`~/.config/char-rac` 配置在新位置不存在时仍会自动读取。Client 安装级设备凭据仍独立存于用户本地应用数据目录，不能复制到多台设备共用。
