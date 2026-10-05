# 身份认证与授权

CLI 已实现 authentik OIDC Authorization Code + PKCE 的浏览器登录和本机回调；令牌仅保存在当前 CLI 进程内。Server 使用 JWT Bearer 中间件从 HTTPS OIDC discovery 获取签名密钥，校验签名、issuer、audience 和有效期，再以稳定的 `(iss, sub)` 识别用户。资源列表、创建、附着、结束工作区均重新查询数据库授权。需在 authentik 创建 public client、启用 PKCE、设置允许的 `http://127.0.0.1:<动态端口>/callback` 回调，并为 Provider 配置非对称 JWT 签名密钥。Server 配置 `[oidc] issuer` 和 `client_id`；若采用 authentik 的全局 issuer 模式，还需设置指向应用 slug 下 discovery 文档的 `discovery_url`。2026-10-03 已通过真实 authentik public client 的 demo 浏览器登录与令牌交换；2026-10-04 正式 Client 的 `login` 已通过 Server 令牌校验；2026-10-05 已在 `.100 → .101` 实测目标授权、资源选择和交互式 `ls`；正常退出及重新附着待复测。

管理员将外部身份映射到允许访问的目标机器和系统账户。Windows 目标使用账户 SID，Linux 目标使用 UID/GID；实际登录环境及文件权限由目标 sshd 和操作系统建立。不同外部身份即使映射到同一个系统账户，工作区归属仍独立。

Server 当前的 `access.identities` 只保存 `(issuer, subject)` 身份键和启用状态，并不保存用户资料。若未来加入本地易读名称，应命名为 **user label**，仅供管理员识别和显示；标签不能作为认证或授权依据，也不能声称是从身份平台实时同步的资料。姓名、邮箱等真正的用户资料由 authentik 或其他 OIDC Provider 管理；Server 不直接读取 authentik 的 PostgreSQL。正式 Client 的 `login` 和 `connect` 使用相同的浏览器登录与回调页；独立的 [OIDC CLI Playground](../demo/oidc-cli/README.md) 保留为身份提供方调试入口。具体命令见[正式 Client 流程](client-server-transport.md)。

当前建连支持 Server 托管的 SSH 登录私钥或一次性输入的 SSH 密码，两者均继续验证数据库中登记的目标主机公钥。密钥授权不要求 Client 提供 SSH 密码；密码授权的密码在 HTTPS 请求体中传给 Server，不写入数据库或日志。私钥内容只保存在服务账户可访问的文件中，数据库保存引用。目标 sshd 只在内网对 Server 开放。正式用户登录只使用 OIDC；SSH CA 仍是可选的后端凭据方案，不是用户登录步骤。

认证失败、映射缺失或授权撤销时，不允许调用连接控制核心；已附着连接应关闭并撤销控制权。审计记录身份、目标、系统账户、工作区、操作及结果，避免记录令牌和终端内容。实现进度见[交付状态](implementation-status.md)。

身份映射和目标授权的数据模型与 LINQ 解析已实现，详见[数据库管理与撤销边界](database.md)。受保护的 `/session` 路由将已验证的 OIDC 身份交给授权解析；未在数据库登记对应 `(iss, sub)` 的用户会收到 403。数据库授权查询用于会话创建、附着、接管和结束，不用于逐次终端输入。数据库中的启用和到期状态目前不自动终止现有 SSH 连接；在线撤销机制仍待实现。

本机 ssh broker-probe 在专用测试数据库中保存虚拟身份，供开发者检查记录并验证授权查询与 Broker；它不是 OIDC 登录入口，不对 WAN 开放。诊断记录在 SSH 失败后仍会保留，需要手动清理。

## 当前测试库中的 OIDC 登记

先在 authentik 创建 OAuth2/OpenID Provider：使用 public client、Authorization Code + PKCE、`openid` scope、非对称签名密钥，允许 CLI 的 loopback 回调。动态端口可在 Redirect URIs 中配置为正则 `^http://127\.0\.0\.1:[0-9]+/callback$`；具体选项见[authentik OAuth2 Provider 文档](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/)。在根目录的 `workspace-access.config` 加入：

```ini
[oidc]
issuer=https://auth.example.com/application/o/workspace-access/
client_id=<authentik-client-id>
; 只有 discovery 文档不在 issuer 下时才填写，例如 authentik 全局 issuer 模式
; discovery_url=https://auth.example.com/application/o/workspace-access/.well-known/openid-configuration
```

CLI 的 `login`（或 `whoami`）会在浏览器登录后向 Server 请求已验证的 `issuer` 和 `subject`。它们必须与数据库中的身份记录精确一致；`broker-probe` 生成的虚拟身份不能代表真实 OIDC 用户。在专用测试库中，可使用 Server 的本机 `database grant-probe` 命令为一个已保存的 `broker-probe` 目标登记真实 OIDC 身份和 `[ssh_probe] account` 授权。此命令仅接受库名含 `test` 的数据库，不提供 WAN 自助登记入口。
