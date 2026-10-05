# CLI 唤起 authentik：OIDC Playground

这个独立小程序演示现有 Client 的登录代码：读取 authentik 的 OIDC discovery，生成 PKCE 与 `state`，监听 `127.0.0.1` 临时端口，通过系统浏览器打开授权页面，接收 `/callback` 授权码，再向 token endpoint 交换 access token。它复用 `src/client/OidcLogin.cs`、Server 的 OIDC 设置校验及 INI 路径解析，不需要额外 NuGet 包，也不建立 SSH 会话。

先在 authentik 创建 OAuth2/OIDC Application 与 **public client**，允许 Authorization Code + PKCE，配置 `openid` scope、非对称签名密钥，以及允许的 loopback Redirect URI。动态端口可使用 Redirect URIs 正则 `^http://127\.0\.0\.1:[0-9]+/callback$`。不要把 Client Secret 放进 CLI。步骤参考 [authentik 官方文档](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/)。

在仓库根目录的 `workspace-access.config` 中配置与 Server 相同的 `[oidc]` 字段：

```ini
[oidc]
issuer=https://auth.example.com/application/o/workspace-access/
client_id=<authentik-public-client-id>
; 只有 discovery 路径不能由 issuer 推导时才填写，例如 authentik 全局 issuer 模式
; discovery_url=https://auth.example.com/application/o/workspace-access/.well-known/openid-configuration

; 可选：调用已启动的 Server /identity，让 Server 验证令牌并显示身份键
server_url=http://127.0.0.1:5080
```

在仓库根目录运行：

```powershell
dotnet run --project demo/oidc-cli
```

需要使用其他文件时，只传路径：`dotnet run --project demo/oidc-cli -- --config .\workspace-access.config`。未指定时，优先查找仓库根目录的 `workspace-access.config`，与 Server 相同。程序会尝试打开浏览器，并同时打印授权 URL。浏览器回到本机后会显示 RAC 的授权回调页；它只表示已收到授权码，CLI 仍需向 authentik 交换令牌。只有终端显示登录成功才算完成。令牌交换失败时会显示 HTTP 状态及 OAuth 错误代码，不输出 access token。配置 `[oidc] server_url` 且 Server 已启用 OIDC 和数据库时，demo 还会请求 `/identity`，显示用于映射的 `(issuer, subject)`；不配置该字段时只验证 authentik 令牌交换。

默认 discovery URL 为 `<issuer 去掉末尾斜杠>/.well-known/openid-configuration`。authentik 使用全局 issuer 模式，或其他 Provider 的 discovery 路径不同的时候，请在 `[oidc]` 设置 `discovery_url`。`issuer` 必须与 discovery 文档中的 `issuer` 完全一致。浏览器回调必须发生在运行 CLI 的同一台设备上；无本机浏览器的设备授权流程不在此演示范围。

Server 返回的 `(issuer, subject)` 是稳定的外部身份键，不是用户资料。Server 本地若需要易读名称，应称为 **user label**：它只用于管理员识别和显示，不能代替身份键，也不参与授权判断。用户的姓名、邮箱等资料由 authentik 或其他身份提供方管理；RAC 不应把自己的 PostgreSQL 身份映射表当作用户资料主库。

回调页的西文使用随程序集嵌入的 IBM Plex Mono Regular/Bold，中文使用系统字体回退。字体以 data URL 随 HTML 返回，无需外部字体请求；CSP 仅为字体允许 `data:` 来源。字体来自 [IBM Plex 官方仓库固定版本](https://github.com/IBM/plex/tree/763c36ef9117782905ae010056dfbe8fd2653a25/packages/plex-mono)，按 [SIL Open Font License 1.1](../../src/client/Assets/Fonts/IBM-Plex-LICENSE.txt) 分发，许可证随构建与发布输出保留。

该流程现已接入正式 Client：使用 `login --server <origin>` 验证登录，或 `connect --server <origin> --target <id>` 登录并进入交互工作区。正式 Client 从 Server 取得 OIDC 配置；本 demo 仍直接读 INI，用于独立排查身份提供方。命令及验收边界见[正式 Client 流程](../../docs/client-server-transport.md)。
