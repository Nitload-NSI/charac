# 开发与 NUKE 构建

## 工具链

安装 `global.json` 指定的 .NET 10 SDK，SDK 补丁选择遵循 `latestPatch`。包版本集中放在 `Directory.Packages.props`，每个项目的 `packages.lock.json` 记录依赖解析结果。

`src/WorkspaceAccessHost.csproj` 声明完整 RID 集合，使各平台发布共享稳定的依赖锁。产品代码直接按职责组织在 `src/` 下。解决方案包含产品、测试和 NUKE 三个项目。构建入口关闭 NUKE 遥测，编译和发布使用独立构建进程。

Windows 使用 `./build.ps1`，Linux 使用 `bash ./build.sh`。两者都运行 `build/Build.csproj` 中的 NUKE 程序。也可直接执行：

```text
dotnet run --project build/Build.csproj --configuration Release -- --target Verify
```

## 构建目标

| 目标 | 行为 |
| --- | --- |
| Restore | 恢复解决方案依赖；`--locked-restore` 启用锁定模式 |
| Compile | Restore 后构建整个解决方案 |
| Test | Compile 后执行核心测试并输出 TRX |
| CheckDocs | 检查标题、相对链接文件、README 可达性及差异说明覆盖 |
| Verify | CheckDocs 与 Test；默认目标 |
| Publish | Verify 后按 `--runtime` 发布单一产品和部署模板 |
| RunHost | Compile 后以 `host` 角色前台运行产品 |
| RunAgent | Compile 后以 `agent` 角色在当前用户身份下运行产品 |

配置参数为 `--configuration Debug` 或 `Release`。发布 RID 为 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`。

```powershell
./build.ps1 --target Verify --locked-restore
./build.ps1 --target Publish --runtime win-x64
./build.ps1 --target Publish --runtime linux-x64
```

## 开发检查

1. 修改实现及对应设计文档。
2. 为身份边界、并发控制和资源生命周期增加测试。
3. 执行 Verify，检查 `artifacts/test-results/`。
4. 涉及原生行为时，在目标系统执行[平台验收](testing.md)。

依赖升级时先修改集中版本，再执行 Restore，检查锁文件变化并执行 Verify。CI 先以锁定模式恢复构建程序，再通过 NUKE 验证和发布。

## 本地运行

Host 默认地址为 `http://127.0.0.1:5080`，命令行 `--urls` 或环境变量 `ASPNETCORE_URLS` 可覆盖地址。直接运行示例：

```text
dotnet run --project src -- host --urls http://127.0.0.1:5081
dotnet run --project src -- agent
```

健康检查分别为 `/health/live` 和 `/health/ready`。当前行为见[交付状态](implementation-status.md)。
