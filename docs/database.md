# PostgreSQL 管理数据

## 技术与范围

Server 使用 EF Core 10 和 Npgsql，目标数据库为 PostgreSQL 18。数据层位于 `src/server/Data/`，直接使用具体的 `AccessDbContext`；授权解析位于 `Authentication/SshAccessResolver.cs`。查询、写入与测试使用 LINQ、ChangeTracker 和 SaveChanges；迁移由 EF 工具生成，不拼接 SQL 或使用 FromSqlRaw / ExecuteSqlRaw。EF 参数化不能代替身份认证、授权和字段校验。

目前实现管理数据模型、EF 迁移、SSH 登录密钥引用、工作区历史、授权解析、迁移命令与数据库 readiness。OIDC、Session Manager 和受保护的 WAN `/session` 路由已有首版实现；真实私钥 SSH 与正式 Client 的交互式 `ls` 已在 `.100 → .101` 实测；正常退出及重新附着待复测。本机 `database target register` 和 `database grant register` 已可持久登记正式目标、主机公钥、OIDC 身份及授权；`endpoint_regist` 可在本机交互收集端点信息、核对主机指纹并把已有登录私钥复制进服务目录，详见[SSH Broker](ssh-session.md#三参数交互式端点登记)；完整的 LAN 网络管理入口与 CA 签发仍待实现。旧 `database enroll-probe`、`database grant-probe` 只面向专用测试库；`database key` 命令管理登录密钥引用和授权绑定。WAN 不接收客户端自报身份来调用授权解析。

## 第一版模型

所有实体使用 UUID 主键。身份、目标、授权、主机公钥和登录密钥只有启用后才能参与授权；本机密钥登记命令会明确创建启用的密钥记录。

| 表（access schema） | 保存内容 | 约束 |
| --- | --- | --- |
| identities | OIDC issuer、subject、启用状态 | issuer + subject 唯一 |
| user_certificate_authorities | 用户证书 CA 名称、公钥、签名密钥引用、启用状态 | 名称唯一；不保存私钥内容 |
| ssh_login_keys | 普通 SSH 登录密钥名称、文件名、启用状态 | 名称与文件名唯一；私钥留在 Server 文件系统 |
| workspace_records | 正式工作区的身份、目标、账户、认证方式与生命周期历史 | 历史不等于可恢复的在线 SSH 连接 |
| targets | 名称、SSH 地址和端口、可选的用户 CA、启用状态 | 名称唯一；可空 CA 外键 |
| host_keys | 已确认的目标主机公钥、启用状态 | 目标 + 公钥唯一；目标外键 |
| grants | 身份、目标、系统账户、可选登录密钥与证书 principal、启用状态、可选 UTC 到期时间 | 身份 + 目标唯一；身份、目标与登录密钥外键 |

首版明确采用一个外部身份在每个目标上对应一个系统账户。不同目标可映射不同账户，不同身份映射相同账户也不共享工作区归属。将来如需同一身份在一个目标上选择多个账户，需要扩展授权模型与选择参数。

私钥文件位于 `[ssh_keys] directory` 指定的受保护目录，数据库只保存密钥名称、普通文件名和启用状态，不保存登录密钥公钥。目标主机公钥仍单独保存在 `host_keys` 中，建连时必须验证。密钥绑定到授权后，Broker 才能为该授权读取文件；数据库记录本身不会生成密钥、修改目标 `authorized_keys` 或使密钥自动获得访问权。`workspace_records` 保留正式工作区的历史，即使 Server 停止也不会自动清理。其 `ClosedAt` 为空只表示未观察到结束，不能作为仍可附着的依据；在线状态仍以进程内 Session Manager 为准。详见[SSH Broker](ssh-session.md)。

密码登录不要求 CA；`targets.UserCertificateAuthorityId` 可为空。配置该字段时，它表示 Server 为该目标签发用户登录证书所用的 CA。管理员仍须在 LAN 内配置目标 sshd 信任对应 CA，并建立 principal 与账户的允许关系；数据库关联不会自动修改 sshd。`host_keys` 是 Server 验证 sshd 身份时使用的主机公钥白名单，与用户 CA 分开。首版保存多个精确公钥以支持人工轮换，目标主机证书 CA 的验证策略尚未实现。

删除行为为 Restrict，防止误删身份、目标或 CA 时悄悄级联移除授权。SaveChanges 校验必填字段、长度、非空 ID、端口范围和 UTC 到期时间；数据库承担非空列、长度、唯一键和外键约束。实体值与关联是否合法仍须由未来 LAN 管理入口校验，例如主机公钥是否真实、目标地址是否允许、签名密钥引用是否在受信范围内。

## 授权检查与签发

`SshAccessResolver.ResolveAsync` 仅接受已完成 OIDC 验证的 `ExternalIdentity` 与目标 ID。一次参数化 LINQ 查询同时要求身份、目标、授权启用，授权未到期，且存在启用的目标主机公钥。无匹配时返回空结果；数据库故障抛出错误，调用方必须拒绝继续操作。查询使用 AsNoTracking，不缓存授权，所以后续调用可以看到已提交的撤销。

返回值是供 Broker 使用的内部授权快照，包含目标地址、系统账户、可选 principal 与 CA 信息、受信主机公钥及授权截止时间，不对 Client 返回。它既不是签发好的 SSH 证书，也不是可长期重复使用的访问令牌。

目标调用时机为创建会话、重新附着、接管，以及建立新的后端 SSH 连接之前。终端每次按键或输出不查询数据库，使用工作区当前 lease 检查控制权。附着与接管还必须检查工作区归属，目标授权通过不代表可以访问其他身份的工作区。

授权判断与后续网络连接并非原子事务：查询返回后仍可能发生撤销。签发应紧接授权检查、限制证书 principal 和有效期，且不超过授权截止时间；不可长期保存授权快照后再签发。Session Manager 后续需要消费撤销通知或定期复核，定义最大撤销生效延迟，并撤销现有控制权及按策略关闭连接。SSH 证书到期只限制后续认证，不会自动踢掉已有连接。这些签发与在线撤销行为目前尚未实现。

## 配置与迁移

开发时优先读取仓库根目录被 Git 忽略的 `workspace-access.config`；可从根目录 `workspace-access.config.example` 复制模板。发布后可用全局 `--config <路径>` 指定受保护的配置文件，或自行将同名文件放在可执行文件旁；显式路径不存在时直接报错。`workspace-access.config` 支持：

```ini
[database]
host=127.0.0.1
port=5432
name=workspace_access
username=workspace_access
password=<数据库密码>
timeout=5
maximum_pool_size=100
```

`host`、`name`、`username` 是必填项；`port`、`password`、`timeout`（秒）、`maximum_pool_size` 可选。Server 用 `NpgsqlConnectionStringBuilder` 组装连接，密码中的连接字符串分隔符不会被当作其他参数。旧的 `[database] connection_string` 仍可单独使用，但不可与拆分字段混用。可选的环境变量 `WORKSPACE_ACCESS_DATABASE` 优先于文件，填写完整的 Npgsql 连接字符串。包含密码的部署配置应由服务管理器或受权限保护的配置提供，不能提交仓库或出现在命令行参数中。未配置数据库连接时 Server 仍可启动，readiness 报告 `database=not_configured`。

Server 为每个请求范围创建独立的 `AccessDbContext`，查询时由 Npgsql 连接池提供连接；一个 `DbContext` 不跨并发请求共享。可用 `maximum_pool_size` 限制并发数据库连接。当前授权查询是短查询，无需在 Server 内另建共享连接或异步命令队列；如果将来需要严格的数据库写入顺序，应在具体业务操作处定义顺序与事务边界。

由管理员创建专用数据库及角色。正常运行的 WAN 服务使用最小权限角色；当前授权查询只需管理表读取权限。LAN 管理入口将来需要独立的写权限。迁移使用临时注入的迁移角色连接，具备对应数据库/schema 的建表与修改权限。不要让 WAN 服务常态使用 postgres 超级用户。数据库不保存 CA 私钥、OIDC 令牌或终端输出。

开发时先配置连接，再执行：

```powershell
dotnet run --project src/server -- --config workspace-access.config database migrate
```

发布后可运行 `char_rac_server database migrate`。该命令显式调用 EF MigrateAsync，重复运行只应用尚未执行的迁移；如果指定数据库不存在且账号有创建权限，EF 可以创建它。执行前确认连接指向专用数据库并检查迁移。正常服务启动不自动迁移。迁移命令允许单条语句最多执行两分钟，运行时查询默认十秒超时，避免数据库建表耗时与连接授权请求共用短超时。

添加后续迁移：

```powershell
dotnet tool restore
dotnet ef migrations add ChangeName --project src/server --output-dir Data/Migrations -- --config workspace-access.config
```

EF 工具与运行时使用相同配置。更改模型后须提交迁移、模型快照和依赖锁文件。迁移创建需要配置数据库连接，但不需要数据库在线。

`/health/ready` 的 database 值为 `not_configured`、`unavailable`、`migration_required` 或 `ready`。检查实际连接、待执行迁移和管理表读取权限。即使 database 为 ready，尚未完成的托管后端 SSH 凭据、完整终端客户端和会话端到端验收仍使整体返回 HTTP 503。

## 数据维护与保留

PostgreSQL 的 `autovacuum` 默认启用，会根据各表的插入、更新和删除量自动执行 `VACUUM` / `ANALYZE`。默认约每分钟检查一次数据库，但这不是每分钟清理一次每张表；未达到触发条件的表不会因此运行 `VACUUM`。正式部署应保持 `autovacuum` 与 `track_counts` 启用，先观察运行数据，再决定是否针对高写入量的表调整阈值。当前没有证据需要另设固定时间的全库 `VACUUM` 任务，也不应把 `VACUUM FULL` 当作周期任务：它会对表取得排他锁。

可在 pgAdmin 的 Query Tool 中执行以下**只读**查询，检查当前实例设置及 `access` schema 的维护情况：

```sql
SHOW autovacuum;
SHOW track_counts;
SHOW autovacuum_naptime;

SELECT relname, n_live_tup, n_dead_tup,
       last_autovacuum, autovacuum_count,
       last_autoanalyze, autoanalyze_count
FROM pg_stat_user_tables
WHERE schemaname = 'access'
ORDER BY relname;
```

`n_live_tup` 和 `n_dead_tup` 是估算值；刚建好或很少改动的表尚无 `last_autovacuum` 记录是正常现象。普通 `VACUUM` 只回收已删除或更新的旧行及索引项，供后续写入复用，不会删除仍有效的业务记录。数据库连接池与 PostgreSQL 缓存也不需要定时清空。所有实体主键由应用生成 UUIDv7，没有需要回收、重置或复用的自增主键序列。

`workspace_records` 是工作区历史，不是数据库垃圾；`identities`、`targets`、`host_keys`、`ssh_login_keys`、`grants` 等是持久管理数据。当前不对它们设置自动删除期限。若将来需要压缩历史，须先确定审计保留期、备份和清理范围，只针对已结束且超过保留期的工作区记录实施单独的限批清理；不能把 `ClosedAt IS NULL` 当成在线判断并直接删除。专用测试库中由本机 Broker 探测生成的记录仍由显式 `database clear-probes` 命令清理，不影响正式记录。

PostgreSQL 机制参考：[Routine Vacuuming](https://www.postgresql.org/docs/18/routine-vacuuming.html)、[Vacuuming Configuration](https://www.postgresql.org/docs/18/runtime-config-vacuum.html)、[Statistics Monitoring](https://www.postgresql.org/docs/18/monitoring-stats.html)。

## 验收

设置 `WORKSPACE_ACCESS_TEST_DATABASE` 为专用 PostgreSQL 18 测试数据库的连接字符串后运行 `./build.ps1 --target Verify --locked-restore`。测试会在指定数据库的 `access` schema 上执行真实 EF 迁移；每个 `DbContext` 通过 Npgsql 连接池获取连接，用例结束时按生成的 ID 删除自身测试数据。不会删除数据库，也不要求测试角色拥有 `CREATEDB`。测试角色需要在该数据库中创建 schema 和表、读写业务表的权限。迁移建立的表与历史记录会保留；测试进程异常终止时也可能留下带随机 ID 的测试记录，因此不要把生产数据库配置为测试目标。未设置此变量时数据库集成测试明确跳过，原有核心测试仍运行。连接上限为 1 的本地测试角色可以顺序运行这些用例；并行运行其他数据库客户端仍可能受该上限影响。

测试覆盖迁移重复执行与模型一致性、唯一键与外键、一次授权查询、跨用户/issuer/目标隔离、带 SQL 特殊字符的输入、禁用状态、过期与撤销后的再次授权。数据库测试公钥是占位数据，仅验证存储和授权条件；另有主机公钥格式与精确匹配单元测试，真实 SSH 登录与证书登录仍待验收。

参考：[Npgsql EF Core 10](https://www.npgsql.org/efcore/release-notes/10.0.html)、[EF 迁移部署](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)。
