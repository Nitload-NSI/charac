using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Widget;

namespace Charac.Android;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "com.nitload.charac", DataHost = "oauth")]
public sealed class MainActivity : Activity
{
    private const string TermuxPackage = "com.termux";
    private const string TermuxHome = "/data/data/com.termux/files/home";
    private const string TermuxBin = "/data/data/com.termux/files/usr/bin";
    private const string ResultAction = "com.nitload.charac.TERMUX_CHECK_RESULT";
    private const string AuthUrlExtra = "charac_auth_url";
    private const string CallbackPortExtra = "charac_callback_port";
    private const string CallbackPortPreference = "oidc_callback_port";
    private TextView? _status;
    private bool _cliReady;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);
        _status = FindViewById<TextView>(Resource.Id.status);
        FindViewById<Button>(Resource.Id.check)!.Click += (_, _) => CheckEnvironment();
        FindViewById<Button>(Resource.Id.settings)!.Click += (_, _) => OpenSettings();
        FindViewById<Button>(Resource.Id.launch)!.Click += (_, _) => Launch();
        ShowPrerequisites();
        if (Intent is not null)
        {
            HandleCheckResult(Intent);
            HandleBrokerIntent(Intent);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        ShowPrerequisites();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null)
        {
            HandleCheckResult(intent);
            HandleBrokerIntent(intent);
        }
    }

    private void HandleBrokerIntent(Intent intent)
    {
        if (intent.GetStringExtra(AuthUrlExtra) is { Length: > 0 } authorizationUrl)
        {
            var port = intent.GetIntExtra(CallbackPortExtra, 0);
            if (port is <= 0 or > 65535)
            {
                SetStatus("OIDC 回调端口无效。请从 Termux 重新发起登录。", false);
                return;
            }

            var preferences = GetPreferences(FileCreationMode.Private);
            if (preferences is null)
            {
                SetStatus("无法保存 OIDC 回调状态。请从 Termux 重新发起登录。", false);
                return;
            }
            var editor = preferences.Edit();
            if (editor is null)
            {
                SetStatus("无法保存 OIDC 回调状态。请从 Termux 重新发起登录。", false);
                return;
            }
            var updatedEditor = editor.PutInt(CallbackPortPreference, port);
            if (updatedEditor is null)
            {
                SetStatus("无法保存 OIDC 回调状态。请从 Termux 重新发起登录。", false);
                return;
            }
            updatedEditor.Apply();
            try
            {
                StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(authorizationUrl)));
                SetStatus("已打开浏览器。完成登录后将自动返回 Termux。", false);
            }
            catch (Exception exception)
            {
                SetStatus("无法打开浏览器：" + exception.Message, false);
            }
            return;
        }

        var callback = intent.Data;
        if (callback is null || callback.Scheme != "com.nitload.charac" ||
            callback.Host != "oauth" || callback.Path != "/callback")
            return;

        var portFromPreferences = GetPreferences(FileCreationMode.Private)!
            .GetInt(CallbackPortPreference, 0);
        var code = callback.GetQueryParameter("code");
        var state = callback.GetQueryParameter("state");
        if (portFromPreferences is <= 0 or > 65535 ||
            string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            SetStatus("OIDC 回调缺少必要参数。请从 Termux 重新发起登录。", false);
            return;
        }

        var command = CommandIntent(TermuxBin + "/charac", [
            "callback", "--port", portFromPreferences.ToString(),
            "--code", code, "--state", state
        ], true);
        if (StartTermux(command))
            SetStatus("登录回调已交给 Termux。请返回终端等待完成。", true);
    }

    private bool HasTermux()
    {
        try
        {
            _ = PackageManager!.GetPackageInfo(TermuxPackage, PackageInfoFlags.Services);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    private bool HasRunPermission() =>
        CheckSelfPermission("com.termux.permission.RUN_COMMAND") == Permission.Granted;

    private void ShowPrerequisites()
    {
        if (!HasTermux())
            SetStatus("未安装 Termux。先从可信来源安装 Termux，再返回此页检查。", false);
        else if (!HasRunPermission())
            SetStatus("Termux 已安装。请在本应用的系统权限设置中授予“在 Termux 中运行命令”，并在 Termux 的 ~/.termux/termux.properties 中设置 allow-external-apps=true。", false);
        else if (!_cliReady)
            SetStatus("Termux 和运行权限已就绪。请在 Termux 设置 allow-external-apps=true，并安装 .NET 10 与 charac CLI，然后点“检查 Termux 环境”。", false);
    }

    private void CheckEnvironment()
    {
        if (!HasTermux() || !HasRunPermission())
        {
            ShowPrerequisites();
            return;
        }

        var callback = new Intent(this, typeof(MainActivity));
        callback.SetAction(ResultAction);
        var nonce = Guid.NewGuid().ToString("N");
        GetPreferences(FileCreationMode.Private)!.Edit()!.PutString("pending_nonce", nonce)!.Apply();
        callback.PutExtra("check_nonce", nonce);
        var flags = PendingIntentFlags.OneShot | PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable;
        var pending = PendingIntent.GetActivity(this, 1, callback, flags);
        var command = CommandIntent(TermuxBin + "/sh", ["-c",
            "test -x /data/data/com.termux/files/usr/bin/charac && command -v dotnet >/dev/null 2>&1"], true);
        command.PutExtra("com.termux.RUN_COMMAND_PENDING_INTENT", pending);
        if (StartTermux(command))
            SetStatus("正在由 Termux 检查 charac 和 .NET；若无结果，请检查 allow-external-apps=true。", false);
    }

    private void HandleCheckResult(Intent intent)
    {
        if (intent.Action != ResultAction)
            return;
        var preferences = GetPreferences(FileCreationMode.Private)!;
        var expectedNonce = preferences.GetString("pending_nonce", null);
        if (expectedNonce is null || !string.Equals(expectedNonce,
                intent.GetStringExtra("check_nonce"), StringComparison.Ordinal))
            return;
        preferences.Edit()!.Remove("pending_nonce")!.Apply();
        var result = intent.GetBundleExtra("result");
        _cliReady = result is not null && result.GetInt("err", 0) == -1 &&
            result.GetInt("exitCode", -1) == 0;
        var detail = result?.GetString("errmsg");
        SetStatus(_cliReady
            ? "环境检查通过。可以启动 charac connect。"
            : "Termux 检查未通过。确认 charac 位于 $PREFIX/bin/charac、.NET 10 已安装，且 allow-external-apps=true。" +
              (string.IsNullOrWhiteSpace(detail) ? "" : "\n" + detail), _cliReady);
        intent.SetAction(null);
    }

    private void Launch()
    {
        if (!_cliReady)
        {
            CheckEnvironment();
            return;
        }
        var command = CommandIntent(TermuxBin + "/charac", ["connect"], false);
        if (StartTermux(command))
            SetStatus("已请求 Termux 启动 charac connect。若终端未出现，请查看 Termux 通知及“悬浮窗”权限。", true);
    }

    private static Intent CommandIntent(string path, string[] arguments, bool background)
    {
        var command = new Intent("com.termux.RUN_COMMAND");
        command.SetClassName(TermuxPackage, "com.termux.app.RunCommandService");
        command.PutExtra("com.termux.RUN_COMMAND_PATH", path);
        command.PutExtra("com.termux.RUN_COMMAND_ARGUMENTS", arguments);
        command.PutExtra("com.termux.RUN_COMMAND_WORKDIR", TermuxHome);
        command.PutExtra("com.termux.RUN_COMMAND_BACKGROUND", background);
        return command;
    }

    private bool StartTermux(Intent intent)
    {
        try
        {
            StartService(intent);
            return true;
        }
        catch (Exception exception)
        {
            SetStatus("无法调用 Termux：" + exception.Message + "。检查运行命令权限和 allow-external-apps 设置。", false);
            return false;
        }
    }

    private void OpenSettings()
    {
        var settings = new Intent(Settings.ActionApplicationDetailsSettings);
        settings.SetData(global::Android.Net.Uri.Parse("package:" + PackageName));
        StartActivity(settings);
    }

    private void SetStatus(string message, bool ready)
    {
        _status!.Text = message;
        FindViewById<Button>(Resource.Id.launch)!.Enabled = ready;
    }
}
