# 身份认证与授权

认证使用标准 OpenID Connect（OIDC）发现、OAuth 2.0 Authorization Code + PKCE 和 JWT Bearer 验证。桌面 Client 使用动态 loopback URI `http://127.0.0.1:<动态端口>/callback`；Termux Client 可使用 OIDC Device Authorization Grant，或使用 `com.nitload.charac://oauth/callback` 并由 Android APK 接收回调，再通过 Termux `RUN_COMMAND` 转回 CLI。身份提供方须支持对应授权流和客户端类型，并允许所选流程需要的 redirect URI。authentik 是当前已实测的提供方，不是设计或实现的供应商限制。

CLI 已实现基于 OIDC discovery 的 Authorization Code + PKCE 浏览器登录和本机回调；令牌仅保存在当前 CLI 进程内。Server 使用 JWT Bearer 中间件从 HTTPS OIDC discovery 获取签名密钥，校验签名、issuer、audience 和有效期，再以稳定的 `(iss, sub)` 识别用户。Provider 应支持 public client、PKCE、`openid` scope、可验证的签名 JWT access token（audience 与 `[oidc] client_id` 一致）及 OIDC discovery；资源列表、创建、附着、结束工作区均重新查询数据库授权。Server 配置 `[oidc] issuer` 和 `client_id`；默认 discovery 地址为 `{issuer}/.well-known/openid-configuration`，不符合此路径时可通过 `discovery_url` 显式指定。正式 Client 的 `login` 已通过 authentik 浏览器登录及 Server 令牌校验；2026-10-05 已在 `.100 → .101` 实测目标授权、资源选择和交互式 `ls`；正常退出及重新附着待复测。

管理员将外部身份映射到允许访问的目标机器和系统账户。Windows 目标使用账户 SID，Linux 目标使用 UID/GID；实际登录环境及文件权限由目标 sshd 和操作系统建立。不同外部身份即使映射到同一个系统账户，工作区归属仍独立。

Server 当前的 `access.identities` 只保存 `(issuer, subject)` 身份键和启用状态，并不保存用户资料。若未来加入本地易读名称，应命名为 **user label**，仅供管理员识别和显示；标签不能作为认证或授权依据，也不能声称是从身份平台实时同步的资料。姓名、邮箱等用户资料由所接入的身份提供方管理；Server 不直接读取身份提供方的数据库。正式 Client 的 `login` 和 `connect` 使用相同的浏览器登录与回调页。具体命令见[正式 Client 流程](client-server-transport.md)。

当前建连支持 Server 托管的 SSH 登录私钥或一次性输入的 SSH 密码，两者均继续验证数据库中登记的目标主机公钥。密钥授权不要求 Client 提供 SSH 密码；密码授权的密码在 HTTPS 请求体中传给 Server，不写入数据库或日志。私钥内容只保存在服务账户可访问的文件中，数据库保存引用。目标 sshd 只在内网对 Server 开放。正式用户登录只使用 OIDC；SSH CA 仍是可选的后端凭据方案，不是用户登录步骤。

认证失败、映射缺失或授权撤销时，不允许调用连接控制核心；已附着连接应关闭并撤销控制权。审计记录身份、目标、系统账户、工作区、操作及结果，避免记录令牌和终端内容。实现进度见[交付状态](implementation-status.md)。

身份映射和目标授权的数据模型与 LINQ 解析已实现，详见[数据库管理与撤销边界](database.md)。受保护的 `/session` 路由将已验证的 OIDC 身份交给授权解析；未在数据库登记对应 `(iss, sub)` 的用户会收到 403。数据库授权查询用于会话创建、附着、接管和结束，不用于逐次终端输入。数据库中的启用和到期状态目前不自动终止现有 SSH 连接；在线撤销机制仍待实现。

本机 ssh broker-probe 在专用测试数据库中保存虚拟身份，供开发者检查记录并验证授权查询与 Broker；它不是 OIDC 登录入口，不对 WAN 开放。诊断记录在 SSH 失败后仍会保留，需要手动清理。

## 当前测试库中的 OIDC 登记

以下配置以当前已实测的 authentik 为例；其他兼容 OIDC 的身份提供方也可使用，但须满足上文列出的授权流、JWT 和 discovery 要求。先创建 public client，启用 Authorization Code + PKCE、`openid` scope 和非对称签名密钥，并允许 CLI 的 loopback 回调。动态端口可在 authentik Redirect URIs 中配置为正则 `^http://127\.0\.0\.1:[0-9]+/callback$`；具体选项见[authentik OAuth2 Provider 文档](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/)。在根目录的 `workspace-access.config` 加入：

```ini
[oidc]
issuer=https://auth.example.com/application/o/workspace-access/
client_id=<oidc-client-id>
; 只有 discovery 文档不在 issuer 下时才填写；authentik 全局 issuer 模式需要显式指定
; discovery_url=https://auth.example.com/application/o/workspace-access/.well-known/openid-configuration
```

CLI 的 `login`（或 `whoami`）会在浏览器登录后向 Server 请求已验证的 `issuer` 和 `subject`。它们必须与数据库中的身份记录精确一致；`broker-probe` 生成的虚拟身份不能代表真实 OIDC 用户。在专用测试库中，可使用 Server 的本机 `database grant-probe` 命令为一个已保存的 `broker-probe` 目标登记真实 OIDC 身份和 `[ssh_probe] account` 授权。此命令仅接受库名含 `test` 的数据库，不提供 WAN 自助登记入口。
