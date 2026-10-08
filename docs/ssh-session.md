# Server 中的 SSH Broker

## 已实现的路径

Server 的 `SshBroker.OpenWithPasswordAsync` 先从 PostgreSQL 解析已启用的身份、目标、账户授权和目标主机公钥，再用 SSH.NET 验证目标主机身份、以密码登录并打开 PTY Shell。地址、端口、主机公钥和允许登录的账户来自数据库，不能由公网请求替换。`SshWorkspaceManager` 按工作区持有连接、控制权和有界输出；Server 停止时关闭连接。目标 `sshd` 负责用户 Shell 和 Windows ConPTY/Linux PTY。

密码登录不要求用户 SSH 密钥对，也不要求 SSH CA。`targets.UserCertificateAuthorityId` 与 `grants.CertificatePrincipal` 可为空。主机公钥是目标 sshd 的身份凭据，与用户登录密钥不同。Broker 会精确匹配事先登记的主机公钥；错误或缺失时拒绝连接。密码仅用于本次建连，不写入数据库、配置或日志。OIDC、Session Manager、持续读取输出和 WebSocket 附着已有首版实现；真实 authentik 的登录和令牌验证已单独验收，正式身份到密钥工作区的交互式 `ls` 已在 `.100 → .101` 实测；正常退出及重新附着待复测。证书签发、屏幕快照和自动重连尚未实现。

## Server 托管的登录密钥

Server 也支持以 SSH 登录私钥创建正式工作区。数据库 `access.ssh_login_keys` 保存名称、私钥文件名和启用状态，`access.grants.SshLoginKeyId` 将一条授权绑定到密钥；私钥本身只在 Server 的受保护目录中。它不是目标的 SSH host key，也不是 SSH CA 签名密钥。Server 每次建连重新检查 OIDC 身份、目标授权、账户映射与已登记的目标主机公钥，然后加载对应私钥，进行 SSH 公钥认证；无需单独登记登录密钥的 `.pub` 文件或在数据库保存其公钥。未绑定密钥的授权保留密码登录路径。

先在 Server 使用的 INI 配置中设置绝对路径：

```ini
[ssh_keys]
directory=/var/lib/workspace-access/keys
```

Windows 测试时改为实际的 Windows 绝对目录，例如 `directory=C:\Users\<用户>\AppData\Local\WorkspaceAccess\keys`；不要在 Windows 路径后拼接上面的 Linux 示例路径。私钥应位于支持 NTFS ACL 的目录，避免保存在无法可靠设置所有权和 ACL 的 VirtIO-FS 共享盘。将已有私钥以普通文件名放入该目录，例如 `fedora_101`。Server 无需该登录密钥的 `.pub` 文件。目标 sshd 仍须在对应账户的 `authorized_keys` 中信任与此私钥匹配的公钥；不能用任意新生成的公钥代替。命令中的文件名必须与磁盘上完全一致。Linux 私钥须只允许服务账户访问（例如 `0600`），目录不得允许其他用户写入；Windows 的目录和私钥文件须由服务账户、SYSTEM 或 Administrators 拥有，允许访问的账户也只能是这三者；Server 读取时会强制检查。`database key register` 会读取并验证私钥格式，只将名称、文件名和启用状态保存到数据库。当前只支持服务无需交互即可打开的私钥；带口令私钥尚需独立的解锁凭据渠道。路径只来自 Server 配置与本机管理员登记，不接受 HTTP 客户端提交。更换目录或密钥文件无需迁移数据库，但必须保持已登记的文件名可用；修改 `[ssh_keys]` 配置需重启 Server。

完成 `database migrate` 后，以同一配置运行以下本机命令（`<grant-id>` 是授权记录 ID，`broker-probe` 或 `grant-probe` 会打印）：

```text
char_rac_server --config <配置路径> database key register fedora-101 fedora_101
char_rac_server --config <配置路径> database key assign <grant-id> fedora-101
char_rac_server --config <配置路径> database key list
```

`database key unassign <grant-id>` 可撤销绑定并恢复该授权的密码建连方式。登记与绑定是持久数据，不会因 Server 停止而删除。注册操作在名称和文件名不变时可重复运行；轮换密钥时先以新名称登记并在目标 sshd 授权新公钥，再重新绑定授权，验证成功后移除旧公钥。正式 Client 对密钥授权不提示 SSH 密码；未绑定密钥时由 Server 返回 `password_required`，Client 才提示输入密码。当前没有自动从文件系统扫描或自动授权目标 sshd 的行为。

若测试库已清空，可用 `[ssh_probe]` 的地址、端口、账户和主机公钥，加上 `[oidc]` 的 issuer 登记完整的 `.101` 测试目标。以下命令只在本机执行，其中 `enroll-probe` 和 `key-probe` 只允许名称含 `test` 的数据库：

```text
char_rac_server --config <配置路径> database enroll-probe fedora-101 <OIDC-subject> fedora-101
char_rac_server --config <配置路径> database key grants
char_rac_server --config <配置路径> ssh key-probe <target-id> <OIDC-subject>
```

`enroll-probe` 会创建或核对目标、主机公钥、身份、授权和密钥绑定，重复运行不会新增记录；有同名但内容不同的记录时拒绝覆盖。`key-probe` 经数据库授权和 SSH Broker，用登记的私钥打开 PTY 并执行 `ls`。它是本机诊断命令，使用指定的 subject 模拟已验证身份，**不验证 authentik 令牌**；完整认证仍应使用正式 Client `connect` 测试。2026-10-05 已在 `.100 → .101` 用真实密钥验证 `key-probe`，获得远端 `ls` 输出。

`access.workspace_records` 持久保存正式创建的工作区 ID、身份键、目标、系统账户、认证方式、创建时间与已观察到的结束时间/原因。它是历史记录，`GET /resources` 的可附着列表仍只来自 Server 进程内的存活 SSH 连接。Server 正常停止时记录 `server_stopped`；崩溃时 `ClosedAt` 可能为空，这不代表原 SSH 终端仍能恢复。现有 `database clear-probes` 是显式管理员操作，只清理专用测试库中的探测记录；Server 停止不执行数据库清理。

## 三参数交互式端点登记

管理员可以在 Server 主机的本机终端运行以下命令。`<名称>` 只是展示和数据库检索用的标签；`<SSH 地址>` 是目标主机名或 IP；`<私钥路径>` 是 **Server 本机** 可读文件的路径，不是 Client 路径，也不是目标 sshd 的路径。命令只有三个位置参数：

```powershell
char_rac_server endpoint_regist vm-102 10.10.0.102 "C:\path\to\id_ed25519"
```

开发构建产物在 `temp/WorkspaceAccessServer/bin/Release/net10.0/`；Windows PowerShell 可在该目录运行 `.\char_rac_server.exe`，或将所在目录加入 `PATH` 后使用上面的命令。未指定 `--config` 时会查找仓库根目录、当前目录或程序目录中的 `workspace-access.config`；部署到其他位置时请加 `--config <绝对路径>`。

命令随后交互询问端口（默认 22）、可信的 **目标主机公钥** 文件路径，并显示其 SHA-256 指纹。管理员须与目标控制台或其他可信渠道的指纹比较，再粘贴相同指纹确认。不能把首次从待连接网络获取的 `ssh-keyscan` 结果本身视为可信。随后可输入 OIDC subject 和目标 OS 账户，直接为该身份创建授权；subject 留空只登记目标和登录密钥，之后可用 `database grant register <名称> <subject> <账户> <名称>` 授权。已登记过相同内容可重复运行；不同地址、公钥或私钥会被拒绝覆盖。

Server 校验私钥格式后，将其**复制**到 `[ssh_keys] directory` 的受保护目录，使用与端点名称关联的普通文件名，核对复制品的权限和可加载性；数据库仅保存文件名。源文件不会自动删除，管理员需自行管理其权限和后续清理。目标 sshd 的对应 OS 账户仍须在 `authorized_keys` 中信任这把私钥对应的登录公钥。命令不会上传私钥到目标机器，也不会修改 `authorized_keys`。Server 必须有权读取源文件并写入受保护密钥目录；Linux 导入文件以 `0600` 创建，Windows 继承已核验的受限目录 ACL。若数据库提交失败，已导入的未引用文件可能留在目录中，重试前应核对登记结果。

此入口没有内置 `scp`：本机文件导入不需要网络传输，而且从目标机器拉取私钥不符合 Server 持有私钥、目标 sshd 持有对应登录公钥的分工。主机公钥可以通过现有的可信管理通道复制到 Server，但仍须独立核对指纹。此命令只在 Server 本机运行，不暴露为 WAN API。

## 本机登记正式 SSH 目标与授权

Server 常驻运行不需要 `[ssh_probe]`。管理员可在 Server 主机上使用与服务相同的 `--config` 和数据库，分别登记目标、目标主机公钥与 OIDC 授权；这些命令不通过 WAN API。先从目标机器控制台等可信渠道复制目标 sshd 的 `ssh_host_ed25519_key.pub`，确认指纹后保存为 Server 本机文件。它是**目标主机公钥**，不是 Server 登录私钥的 `.pub` 文件；不要把未经核对的 `ssh-keyscan` 结果直接登记为信任锚。

```powershell
dotnet run --project src/client -- login --server http://127.0.0.1:5080
dotnet run --project src/server -- --config .\workspace-access.config database target register vm-102 10.10.0.102 22 .\trusted\vm-102-ssh_host_ed25519_key.pub
dotnet run --project src/server -- --config .\workspace-access.config database key register vm-102-login vm_102_login
dotnet run --project src/server -- --config .\workspace-access.config database grant register vm-102 <login打印的subject> <目标OS账户> vm-102-login
dotnet run --project src/server -- --config .\workspace-access.config database target list
dotnet run --project src/server -- --config .\workspace-access.config database grant list
```

`database target register` 从公钥文件读取恰好一行 SSH 主机公钥，校验格式，持久保存目标的名称、地址、端口和信任锚，并打印目标 UUID 和指纹。`database grant register` 使用 `[oidc] issuer` 与传入的 subject 创建或复用身份，再为目标绑定 OS 账户及可选的登录密钥名称。省略末尾密钥名称时使用交互式 SSH 密码路径；指定密钥时会检查其已登记、已启用且私钥文件可由当前服务账户读取。目标 sshd 仍须允许该账户使用对应公钥。两条登记命令重复执行相同内容不创建副本，遇到同名但不同内容或被禁用的记录会拒绝覆盖；变更、轮换与撤销应由单独的管理操作处理。登记完成后，运行中的 Server 下次查询数据库即可看到新授权，无需因数据变化重启。

`[ssh_probe]` 只供 `ssh probe`、`ssh broker-probe`、`serve --local-probe`、`database enroll-probe` 和 `database grant-probe` 等旧诊断命令使用；正式 `serve`、`connect`、`database target/grant/key` 与 `ssh key-probe` 不依赖它，可以从配置中删除。
## 本机配置与诊断

两个诊断命令支持全局 `--config <路径>`，可直接指向编辑好的 INI 文件，无需复制到输出目录；开发时不指定则读取仓库根目录的 `workspace-access.config`。该文件被 Git 忽略，可参考 `workspace-access.config.example`；发布时可用 `--config` 指向独立的受保护配置，或自行将同名文件放在可执行文件旁。`[database]` 是 Server 正常运行使用的 PostgreSQL 连接参数；`[ssh_probe]` 仅提供本机诊断目标参数，不会替代数据库中的正式目标登记。SSH 密码在运行时隐藏输入，不写入配置。

```ini
[database]
host=127.0.0.1
port=5432
name=ni_access_test_db
username=ni_access_test
password=<测试库密码>
timeout=5
maximum_pool_size=100

[ssh_probe]
address=10.10.0.101
port=22
account=youmuliu
host_public_key="ssh-ed25519 AAAA..."
```

先通过 `.101` 控制台或可信管理通道获取完整的 `ssh_host_ed25519_key.pub` 内容，填入 `host_public_key`。复制以 `ssh-ed25519` 开头的一行，配置中可以保留包裹整行的普通 ASCII 双引号；不要用排版用的弯引号。诊断命令会在要求输入 SSH 密码前检查公钥格式。不要只凭待测网络上的 `ssh-keyscan` 输出决定信任。

在 `.100` 的仓库目录运行 `dotnet run --no-launch-profile --project src/server -- --config workspace-access.config ssh probe`，可直接验证 SSH 传输、主机公钥、密码认证和 PTY Shell。2026-10-02 已在 `.100` 上用显式参数形式成功探测 `.101`。

`ssh probe` 和下述 `ssh broker-probe` 都是执行一次便退出的命令，不会启动常驻服务。要让 CLI 通过 Server 的 HTTP/WebSocket 实测一次 `ls`，使用[回环端到端诊断](client-server-transport.md#cli-探测)的 `serve --local-probe` 与 `probe-session`；它不依赖 OIDC，也不向 PostgreSQL 写入测试记录。

运行 `dotnet run --no-launch-profile --project src/server -- --config workspace-access.config ssh broker-probe`，会进一步测试数据库授权解析和真实 Broker。此命令只允许连接库名含 `test` 的专用 PostgreSQL 测试库；它会保存启用的测试身份、目标、主机公钥和授权，打印四条记录的 ID 后再要求输入 SSH 密码。随后通过 `SshWorkspaceManager` 向 Linux Shell 发送 `ls`，读取返回列表并检查退出码为 0；即使 SSH 连接失败，诊断记录也会保留。每次运行新增一组记录，可用 `database list-probes` 查看，用 `database clear-probes` 清理专用测试库中的诊断记录；这两个命令保留 EF 迁移与非诊断数据。测试库须先应用 EF 迁移。成功输出 `Database authorization, host key, password, managed session and Linux ls verified.`。2026-10-02 已在 `.100` 上使用此命令连接 `.101`，读回目录列表并验证 `ls` 退出码为 0。虚拟身份只用于本机测试，不代表 OIDC 已通过，也不对公网开放。两个命令仍支持显式传入地址、端口、账户、公钥的旧形式。

要继续验收 OIDC + WebSocket，先按[身份认证](authentication.md)配置兼容的身份提供方，再在一个终端启动 Server。在另一个终端依次运行以下命令；`<target-id>` 用 `broker-probe` 打印的值，`<issuer>` 与 `<subject>` 用 `whoami` 打印的值。`grant-probe` 是本机管理命令，只能对专用测试库中的诊断目标授权：

```powershell
dotnet run --project src/server -- --config workspace-access.config
dotnet run --project src/client -- whoami --server http://127.0.0.1:5080
dotnet run --project src/server -- --config workspace-access.config database grant-probe <target-id> <issuer> <subject>
dotnet run --project src/client -- run --server http://127.0.0.1:5080 --target <target-id>
```

最后一条命令会重新通过 OIDC 登录，交互输入 SSH 密码，创建工作区并通过 WebSocket 发送 `ls`，显示输出及工作区 ID。使用 `attach --server http://127.0.0.1:5080 --id <workspace-id>` 可再次登录并附着同一工作区，继续发送 `ls`。目录列表应与在 `.101` 上用对应系统账户直接运行 `ls` 的结果对照；CLI 同时检查 Shell 返回的退出码为 0。退出 CLI 后工作区仍在 Server 内，直到后端 Shell 结束、显式 DELETE 或 Server 停止。

## 多目标与生命周期

开发机当前为 `.100`，已测试 `.100 → .101` 的直接密码探测。最终计划把 Server 部署在 `.103`，由它连接 `.100`—`.103` 四个独立 SSH 目标；部署后需重新验收 `.103` 到各目标的连接。每个目标分别管理地址、端口、主机公钥和授权账户，Server 到其自身 sshd 的连接也遵循相同规则。

客户端断线不会立即关闭后端 SSH；运行中的 Session Manager 持续读取并有界保存输出。远端 Shell 退出或 SSH 传输断开时，原交互会话不能仅靠 CA 或数据库恢复。`broker-probe` 测试托管工作区的一次 `ls` 输入和输出，持续交互、接管、撤销及重连仍需单独验收。
