# Android CLI 客户端

Termux 主流程使用 OIDC Device Authorization Grant：CLI 打印 verification URL 和 user code，用户在浏览器完成 Authentik 登录后回到 Termux 输入 `y`，CLI 轮询 token endpoint 并继续访问 Server。该流程不需要本机 loopback、Android APK 或自定义 redirect URI。Authentik Provider 必须启用 Device-code grant，并在 discovery 中提供 `device_authorization_endpoint`。

Android APK 只作为可选的自定义 URI 回调桥，兼容旧的 `com.nitload.charac://oauth/callback` 流程，不是 Termux CLI 的运行前提。若使用该桥，Authentik Provider 仍必须允许精确 redirect URI `com.nitload.charac://oauth/callback`。

推荐把 Android CLI 作为 Termux DEB 分发，而不是把 CLI 当成普通 Android APK。**当前 Termux DEB 已标记为不可用，暂不作为交付包；下面的安装步骤仅保留用于后续修复和设备验收。** APK 只是可选的 One UI 启动器：它不能单独运行 CLI，必须与 Termux、Termux DEB、运行命令权限和 `allow-external-apps=true` 配置一起使用。直接安装 APK 后点击图标不能替代 Termux 环境，也不会自动安装 .NET 或 `charac`。

Android 首期支持 ARM64 设备上的 Termux 终端，CLI 使用 `linux-bionic-arm64` RID。当前 DEB 已加入 Device Flow 实现，但尚未完成真实 Termux/三星设备验收，仍属于候选包；另有独立的 .NET Android 启动器项目 `src/android/CharacAndroid.csproj`，包 ID 为 `com.nitload.charac`；Server 仍只部署在 Windows 或 Linux。

执行 `./build.ps1 --target Publish --runtime linux-bionic-arm64 --locked-restore`，或在 Linux 上使用 `bash ./build.sh`。随后运行 `./packaging/package.ps1 -Target ClientTermuxDeb`，生成 `artifacts/packages/<版本>/charac-termux_<版本>-2_aarch64.deb`。当前包是 Device Flow 候选包，必须完成真实设备登录、轮询和交互验收后才能标记为可交付；APK 的环境检查也不能替代 CLI 验收。

如果暂时不使用 DEB，也可以复制 `artifacts/publish/linux-bionic-arm64/client/` 到 Termux 私有目录，例如 `$HOME/.local/opt/charac`；该目录是 framework-dependent 发布，需要先安装 Termux 的 `dotnet-runtime-10.0`。为 APK 提供固定入口 `$PREFIX/bin/charac`，其内容用 Termux 的 `sh` 调用 `dotnet $HOME/.local/opt/charac/charac.dll` 并原样传递参数；入口必须可执行。

手动复制方案在 Termux 中完成后，创建入口：

```sh
cat > "$PREFIX/bin/charac" <<'SH'
#!/data/data/com.termux/files/usr/bin/sh
exec dotnet "$HOME/.local/opt/charac/charac.dll" "$@"
SH
chmod 700 "$PREFIX/bin/charac"
```

两种安装方式都要设置 Client 的默认入口：

```sh
mkdir -p "$HOME/.config/charac"
printf '[client]\nserver=https://access.example.com\n' > "$HOME/.config/charac/client.config"
```

将 `access.example.com` 改为实际 RAC HTTPS 入口。当前代码含 `login` OIDC PKCE 浏览器登录、本机 loopback 回调、`connect` 正式 WebSocket 交互及一次性 `ls` 诊断，但尚未在 Android/Termux 实机验证浏览器回调或交互。终端原始模式、键盘映射、窗口尺寸同步与 WebSocket 自动重连仍待实现。跨平台构建和发布不等于设备运行验收；验收须在真实 ARM64 Android/Termux 环境检查启动、认证回调、前后台切换和网络恢复。

参考：[.NET RID 目录](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog)、[Termux .NET 包](https://github.com/termux/termux-packages/tree/master/packages/dotnet10.0)。

## One UI APK 与 Termux

APK 包名使用 `com.nitload.charac`，厂商标识为 `nitload`，要求 Android 12 或更高版本。启动器检测 `com.termux` 与自身的 `com.termux.permission.RUN_COMMAND` 授权；用户点击环境检查后，通过 Termux 的 `RUN_COMMAND` 在后台检查 `$PREFIX/bin/charac` 与 `dotnet`，成功才允许启动固定命令 `charac connect`。Termux 里的 Client 自动读取 `~/.config/charac/client.config`。APK 不保存 SSH 私钥、OIDC 令牌或 Termux 的设备凭据。

Android 应用不能直接读取、改写 Termux 私有目录，也不能替用户静默授予跨应用执行权限。用户仍需在 Android 设置中授权 charac 的 `RUN_COMMAND` 权限，并在 Termux 的 `~/.termux/termux.properties` 设置 `allow-external-apps=true`；然后安装上面的 DEB（自动安装 .NET 运行时依赖）或按手动方案复制 CLI，并设置用户级配置。启动器有权限设置入口；检测不到 Termux 时显示安装提示。One UI 的后台启动限制可能要求在通知栏点开 Termux，或授予 Termux“悬浮窗”权限。

在安装了 .NET Android workload 和 Android SDK 的构建机上，运行 `dotnet build src/android/CharacAndroid.csproj -c Debug -p:NuGetAudit=false` 可生成调试签名 APK；再运行 `./packaging/package.ps1 -Target ClientApkDebug` 将其收集到 `artifacts/packages/charac-client_0.1.0_android-debug.apk`；调试签名只用于设备验证，正式升级发布需要 nitload 保管固定签名密钥。Android/Termux 实机的权限回调、CLI 启动、浏览器登录与键盘交互尚未验收。

依据：[Termux RUN_COMMAND 官方说明](https://github.com/termux/termux-app/wiki/RUN_COMMAND-Intent)。
