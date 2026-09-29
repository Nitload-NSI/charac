# 身份认证与授权

## 身份来源

authentik 提供 OIDC 身份认证、MFA 和应用访问策略。操作系统管理系统账户、组和文件权限。Host 配置受信任 issuer、客户端标识和账户映射规则，运行数据记录身份与工作区的关系。

稳定外部身份为经过验证的 `(iss, sub)`。Windows 账户解析为 SID，Linux 账户解析为 UID、主组和附加组。映射配置可来自管理员配置或受管理员控制的可信声明。

## 登录流程

浏览器客户端采用 Authorization Code + PKCE，应用校验 state、nonce、签名、issuer、audience 和有效期。会话密钥与客户端密钥通过部署配置管理。浏览器会话使用 Secure、HttpOnly Cookie，并在握手时校验 Origin；变更操作采用 CSRF 防护。

完成登录后，按 host 访问规则、目标系统账户映射和代理状态进行授权，再进入连接控制权获取阶段。每次重连与接管均重新检查授权。授权撤销时关闭传输并撤销控制权，工作区结束策略由 host 管理配置确定。

## 会话归属

host 工作区有一个外部身份所有者。断线保留所有者；同身份可重连或显式接管。工作区终止并清理后释放归属。两个外部身份即使映射到同一系统账户，也保持各自的工作区授权边界。

## 审计

记录事件时间、host、身份标识、系统账户标识、连接标识、控制权版本及结果。认证凭据、Cookie 和终端内容采用独立的数据保护策略。常规审计聚焦身份与生命周期事件。

参考：[authentik OIDC](https://docs.goauthentik.io/add-secure-apps/providers/oauth2)。实现阶段见[交付状态](implementation-status.md)。
