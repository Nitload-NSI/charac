# Client 与 Server 的单域名入口

## 配置域名与监听地址

Server 可通过全局 `--config <路径>` 显式指定 INI 配置；未指定时，开发环境优先读取仓库根目录的 `workspace-access.config`，发布时需要提供外部配置路径，或自行将同名文件放在可执行文件旁。启动时加载，修改后需重启。配置示例：

```ini
[server]
domain=127.0.0.1
listen=http://127.0.0.1:5080
```

`domain` 是客户端使用的对外域名，只填主机名，不带协议或端口。Server 以它校验 HTTP `Host`；不匹配时返回 400。`listen` 是 Kestrel 实际绑定的 HTTP 地址，默认仅在本机监听。两者分开配置，例如通过 Caddy 提供 `https://access.example.com` 时，设 `domain=access.example.com`，保留 `listen=http://127.0.0.1:5080`，并让 Caddy 将原始 Host 和 WebSocket Upgrade 转发到该监听地址。配置域名不会自动创建 DNS 记录、签发 TLS 证书或开放端口；这些由部署环境和反向代理负责。目标 `sshd` 始终只在内网可达。

## 路由

CLI 只接受一个 Server origin，例如 `https://access.example.com`。操作类型由 HTTP 方法和固定路径表示，目标与会话定位 ID 通过 query 传递。账户、认证令牌、SSH 证书和密钥不放进 URL。

| 方法与路径 | 用途 | 当前状态 |
| --- | --- | --- |
| `GET /status` | 公开的协议版本、OIDC public client 配置与会话可用状态 | 已实现；`ready` 表示 OIDC 和数据库连接已配置，不保证后端在线 |
| `GET /resources` | 当前身份可新建的目标及持有的存活会话快照 | 已实现，需 Bearer token |
| `GET /identity` | 返回当前经过验证的 `(issuer, subject)`，供本机管理员登记授权 | 已实现，需 Bearer token |
| `POST /session?target={targetId}` | 验证 Bearer token 与授权后，按授权绑定的 Server 私钥或临时 SSH 密码创建工作区 | 已实现；返回工作区 ID，密码不入库 |
| `GET /session?id={sessionId}` | 再次校验身份和目标授权，升级为 WebSocket 附着 | 已实现；`takeover=true` 显式接管，默认已有控制者时返回 409 |
| `DELETE /session?id={sessionId}` | 再次校验身份、设备和目标授权后结束工作区 | 已实现；后端 SSH 连接随之关闭 |

两个 ID 均使用非空 GUID。query 只负责定位资源，不代表访问权；创建和附着先验证 authentik access token、目标授权和工作区归属。系统账户由 Server 根据数据库授权映射；已绑定 SSH 登录密钥时 Server 从受保护目录加载私钥，未绑定时返回 `400 password_required`，Client 才在 HTTPS 请求体提供一次性 SSH 密码。密钥配置见[SSH Broker](ssh-session.md)。正式 Client 在 `GET /identity`、`GET /resources`、`POST /session`、WebSocket `GET /session` 和 `DELETE /session` 请求头发送 `X-RAC-Device` 安装凭据。除 `/identity` 外，所列请求都必须带有效凭据；另一安装已有活跃连接时返回 409 `device_in_use`，缺少或无效凭据时返回 400 `device_credential_required`。独立 OIDC demo 的 `/identity` 调用仍可不带该头。WebSocket 使用 `Authorization: Bearer` 请求头，输入消息为 `{"type":"input","data":"<base64>"}`，尺寸消息为 `{"type":"resize","columns":80,"rows":24}`，输出消息为 `{"type":"output","sequence":1,"data":"<base64>"}`。输出序列号可发现丢失，但没有屏幕快照。后端 SSH 结束时，Server 会在已排队输出之后发送 `{"type":"ended","reason":"ssh_closed"}`，再关闭 WebSocket；显式结束工作区时 `reason` 为 `workspace_closed`。接管造成的观察者连接关闭不发送后端结束事件。若连接意外中断而没有收到 `ended`，Client 将其视为 Server 连接中断，不能误报为 SSH 已正常结束。LAN 管理面承担目标注册、后端凭据配置与授权维护，不挂在这个 WAN 路由组上。协议兼容性由 `/status` 中的 `protocolVersion=3` 表示，当前不在 URL 中加入 `/api/v1`。

## 正式 Client 登录与交互

`login --server <origin>` 复用已在 demo 验证的浏览器 Authorization Code + PKCE 流程，再请求 Server `/identity`。只有 Server 接受令牌后，CLI 才显示登录成功及 `(issuer, subject)`。`whoami` 保留为同一入口。Client 从 Server `/status` 获取 `[oidc]` 配置，不需要复制 Server 的 INI 或数据库密码。demo 继续用于独立验证身份提供方。

在仓库根目录启动 Server，再在另一个终端操作 Client：

```powershell
dotnet run --project src/server -- --config .\workspace-access.config serve
dotnet run --project src/client -- login --server http://127.0.0.1:5080
dotnet run --project src/client -- connect --server http://127.0.0.1:5080
```

正式 CLI 还可以在用户级 `client.config` 的 `[client] server` 设置默认入口：Windows `%APPDATA%\Charac\client.config`，Linux/Termux 使用 `$XDG_CONFIG_HOME/charac/client.config` 或 `~/.config/charac/client.config`。配置后直接运行 `connect`、`login`、`status`；命令上的 `--server` 优先，`--config <路径>` 可在命令名前选择另一份配置。此文件只保存入口 origin，不保存认证令牌。

`connect` 自行登录并显示资源选择菜单，无需先执行 `login`；`login` 用于验证配置和取得身份键，不建立跨进程登录缓存。浏览器登录后的蓝色回调页、IBM Plex Mono 字体与 demo 共用同一份实现。Server 必须配置 OIDC 和数据库；登录身份仍需由管理员登记目标授权，见[认证与授权](authentication.md)。也可用 `connect --server <origin> --target <UUID>` 跳过菜单；`--target` 是数据库目标 UUID，不是主机 IP。若该授权已绑定可用的 Server SSH 登录密钥，Client 不会提示 SSH 密码；否则继续使用交互式密码登录。

正式 Client 首次运行时在当前用户的本地应用数据目录 `workspace-access/device-key` 生成 32 字节随机安装凭据；后续命令复用它。同一 `(issuer, subject)` 在 Server 上只能由一个安装凭据持有活跃 Client 连接，同一安装可同时打开多个终端。最后一条 WebSocket 断开后，另一设备可登录并连接；已有 SSH 工作区仍保留。管理员的本机 `disconnect` 可以立即切断此身份的所有 Client/WebSocket 并释放占用，见[服务宿主](platforms/service-hosting.md)。这是 Server 进程内的活跃连接仲裁，不限制 authentik 自身的浏览器登录，也不把设备归属写入数据库。安装凭据是可复制的 bearer 秘密，不能证明物理设备；复制到另一机器会被视为同一安装，应像密码一样保护。仅经 HTTPS 发送；开发回环 HTTP 例外。Windows 使用当前用户的应用数据目录权限，Unix 创建文件时限制为 0600。后续若需抵抗凭据复制，应另设计设备密钥的持有证明。

连接成功后持续转发终端输入、输出、鼠标和尺寸变化。`Ctrl+]` 解除附着，按键回退模式也接受 `F12`；工作区继续由 Server 持有。Client 在附着前打印工作区 ID，重新进入使用：

```powershell
dotnet run --project src/client -- connect --server http://127.0.0.1:5080 --id <工作区UUID>
```

重新附着会重新登录、重新检查数据库授权，不再创建 SSH 会话或请求 SSH 密码。若仍有控制者，Server 返回 409，当前 Client 不自动接管。远端 `exit`、SSH 断线或 Server 停止会结束后端会话；正式 Client 的本机退出和传输错误不会发送工作区删除请求。令牌只保留在当前命令进程中；离线输出仍受 Server 的有界缓冲限制，屏幕快照和自动重连尚未实现。真实 OIDC → Server → SSH 交互式 `ls` 已在 `.100 → .101` 实测；正常退出及重新附着待复测，Android 限制见[平台说明](platforms/android-client.md)。
## 登录后的资源发现

`GET /resources` 要求经过 Server 验证的 Bearer token，不接受 query 中自报的用户身份。返回 `targets` 和 `sessions` 两个数组；无授权或未登记的身份返回空数组。未认证返回 401；Server 缺少 OIDC 或数据库配置时返回 503。响应设置 `Cache-Control: no-store`。

`targets` 的每项为 `{ id, name, account }`。Server 使用一次 EF LINQ 查询，按 `(issuer, subject)`、身份/目标/授权启用状态、授权有效期和已启用主机公钥过滤；复用实际建连的授权条件。不返回内网地址、SSH 端口、主机公钥、CA 或后端凭据。可列出的目标代表有权尝试新建会话，不代表机器已经通过在线探测。

`sessions` 的每项为 `{ id, targetId, targetName, account, createdAt, state }`。创建时间使用 UTC，`state` 为 `detached`（没有控制者，可附着）或 `attached`（已有控制者）。只列出当前身份持有、仍有目标访问权且系统账户映射一致的存活工作区；后端结束或 Server 停止后不保留在列表中。创建时间与状态来自进程内 Session Manager，不新增数据库表。

数据库授权和各工作区状态构成查询时的快照，不是连接许可。实际创建、附着、结束仍重新查权限；管理员改动账户映射后，旧账户工作区不会列出，直接附着/结束请求也被拒绝。在线撤销和旧工作区的管理员清理机制仍待完善。列表读取不持有全局锁执行数据库或网络操作。

不指定 ID 的 `connect --server <origin>` 会在登录后显示菜单：已有会话在前，新建目标在后；输入编号选择、`r` 重新请求列表、`q` 或输入结束退出。占用中的会话显示但不提供编号，CLI 暂不支持显式接管；列表显示后若状态变化，实际附着仍由 Server 拒绝或准许。刷新复用当前进程令牌，不重新打开浏览器。取消和刷新都不会创建或删除工作区。
## CLI 探测

`WorkspaceAccessClient status --server https://access.example.com` 请求 `/status`，检查服务标识和协议版本。`run --server <origin> --target <id>` 从状态响应取得 OIDC issuer、client ID 和 discovery URL，执行浏览器 Authorization Code + PKCE，提示输入 SSH 密码，创建工作区并通过 WebSocket 发送 Linux `ls`；`attach --server <origin> --id <id>` 重新登录并附着已有工作区，也发送 `ls`。这两个命令目前用于一次性验收，不提供持续交互终端。CLI 不持久化令牌和密码。只允许 HTTPS origin；开发时允许 loopback HTTP，例如 `http://127.0.0.1:5080`。不接受 Server origin 中的路径、查询串或用户信息。

无需 OIDC 的本机端到端诊断使用显式 `serve --local-probe` 开关。Server 此时额外注册 `/local/session`，从 `[ssh_probe]` 读取 `.101` 等诊断目标，不创建 PostgreSQL 记录，也不改变正式 `/session` 的认证要求。开关只在 `[server] listen` 和 `domain` 都为数值回环地址时生效；请求来源还必须是回环地址。只用于同一台机器上的两个终端，不要通过 Caddy 转发或用于 LAN/WAN。SSH 后端仍以运行时输入的密码登录，并固定校验配置的目标主机公钥。

终端一保持 Server 运行，终端二运行 CLI：

```powershell
dotnet run --no-restore --no-launch-profile --project src/server -- --config .\workspace-access.config serve --local-probe
dotnet run --no-restore --no-launch-profile --project src/client -- probe-session --server http://127.0.0.1:5080
```

第二条命令会提示输入 SSH 密码，经 HTTP 创建托管工作区、WebSocket 发送一次 `ls`、检查输出和退出码，再关闭该诊断工作区。要在同一个工作区持续输入命令，把第二条命令改为 `dotnet run --no-restore --no-launch-profile --project src/client -- shell --server http://127.0.0.1:5080`。在 Windows VT 终端中，`shell` 开启 VT 输出和延迟行尾换行，读取并转发原始输入字节，包括 nvim 请求的鼠标报告、方向键和 Ctrl+C；只有远端 TUI 开启鼠标跟踪时才转发鼠标报告，退出后会过滤滞留报告。连接后会同步终端尺寸，随后持续转发尺寸变化。按 `Ctrl+]` 退出并关闭本次诊断工作区，Server 仍运行。若终端不支持 Windows VT 输入，CLI 回退到按键读取，鼠标操作不可用，此时可用 `F12` 退出。退出时恢复本机控制台模式、关闭终端鼠标报告并清空待处理的本机输入；只有远端仍处于备用屏幕时才退出备用屏幕，避免把光标恢复到旧位置。本地密码提示和断开状态会接在当前终端行后，不额外插入空行；收到 `ended` 后显示工作区关闭状态。远端 shell 自行结束后 Server 可能已经删除工作区，此时 Client 的清理请求收到 404 不会误报为连接失败。附着期间输出队列采用背压并检查序号，若检测到序号缺口，CLI 会结束该诊断工作区并报告错误。nvim/Mason 的 Up/Down 滚动残影已在 2026-10-03 的本机 Debug Client 测试中未复现；断开提示的位置尚待实机复测。Android/Termux 的原始输入模式、断线恢复及正式认证路径的交互验收仍待完成。`ssh probe` 与 `ssh broker-probe` 则是执行完毕即退出的单次诊断命令；它们不会启动常驻 HTTP Server。

本机可分别运行：

```powershell
dotnet run --project src/server
dotnet run --project src/client -- status --server http://127.0.0.1:5080
dotnet run --project src/client -- run --server http://127.0.0.1:5080 --target <已授权目标的 UUID>
```

健康探针 `/health/live` 与 `/health/ready` 保持独立于客户端协议，也遵守相同的 Host 校验。若部署需要转发头，必须在 `[server] trusted_proxy` 中只信任直接相连的代理地址；Server 仅在此前提下读取一跳转发来源。
