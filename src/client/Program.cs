using System.Text.Json;

namespace Charac.Client;

internal static class Program
{
    private const int ProtocolVersion = 3;

    public static async Task<int> Main(string[] args)
    {
        string? configPath = null;
        if (args.Length >= 2 && args[0] == "--config")
        {
            configPath = args[1];
            args = args[2..];
        }
        if (args.Length >= 2 &&
            args[0] is "status" or "login" or "whoami" or "connect" or "run" or "attach" &&
            !args.Contains("--server", StringComparer.Ordinal) &&
            !args[1].StartsWith("--", StringComparison.Ordinal))
        {
            var candidate = args[1].Contains("://", StringComparison.Ordinal)
                ? args[1]
                : "https://" + args[1];
            if (TryGetServerUri(candidate, out _))
                args = [args[0], "--server", candidate, .. args[2..]];
        }
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            PrintUsage();
            return 0;
        }
        if (args[0] is "status" or "login" or "whoami" or "connect" or "run" or "attach" or "probe-session" or "shell" &&
            !args.Contains("--server", StringComparer.Ordinal))
        {
            try
            {
                var origin = ClientConfiguration.LoadServer(configPath);
                args = [args[0], "--server", origin, .. args[1..]];
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
        }

        if (args is ["status", "--server", var server])
        {
            return await ShowStatusAsync(server);
        }
        if (args is ["login" or "whoami", "--server", var identityServer])
            return await WorkspaceClient.WhoAmIAsync(identityServer);
        if (args is ["connect", "--server", var resourceServer])
            return await WorkspaceClient.RunAsync(resourceServer, null, null, interactive: true);
        if (args is ["connect", "--server", var connectServer, "--target", var connectTarget] &&
            Guid.TryParse(connectTarget, out var connectTargetId) && connectTargetId != Guid.Empty)
            return await WorkspaceClient.RunAsync(connectServer, connectTargetId, null, interactive: true);
        if (args is ["connect", "--server", var reconnectServer, "--id", var reconnectSession] &&
            Guid.TryParse(reconnectSession, out var reconnectId) && reconnectId != Guid.Empty)
            return await WorkspaceClient.RunAsync(reconnectServer, null, reconnectId, interactive: true);
        if (args is ["run", "--server", var runServer, "--target", var targetText] &&
            Guid.TryParse(targetText, out var targetId) && targetId != Guid.Empty)
            return await WorkspaceClient.RunAsync(runServer, targetId, null);
        if (args is ["attach", "--server", var attachServer, "--id", var sessionText] &&
            Guid.TryParse(sessionText, out var sessionId) && sessionId != Guid.Empty)
            return await WorkspaceClient.RunAsync(attachServer, null, sessionId);
        if (args is ["probe-session", "--server", var probeServer])
            return await WorkspaceClient.RunLocalProbeAsync(probeServer);
        if (args is ["shell", "--server", var shellServer])
            return await WorkspaceClient.RunLocalShellAsync(shellServer);
        if (args is ["callback", "--port", var portText, "--code", var code, "--state", var state] &&
            int.TryParse(portText, out var callbackPort) && callbackPort is > 0 and <= 65535)
            return await SendCallbackAsync(callbackPort, code, state);

        PrintUsage(Console.Error);
        return 2;
    }

    private static async Task<int> ShowStatusAsync(string server)
    {
        if (!TryGetServerUri(server, out var serverUri))
        {
            Console.Error.WriteLine("Server must be an HTTPS origin (HTTP is allowed only for loopback).");
            return 2;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var statusUri = new Uri(serverUri, "/status");
            using var response = await client.GetAsync(statusUri);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(body);
            var status = document.RootElement;
            if (status.GetProperty("service").GetString() != "workspace-access" ||
                status.GetProperty("protocolVersion").GetInt32() != ProtocolVersion)
            {
                Console.Error.WriteLine("The endpoint is not a compatible Workspace Access Server.");
                return 1;
            }

            var ready = status.GetProperty("ready").GetBoolean();
            Console.WriteLine($"Server reachable: {serverUri}");
            Console.WriteLine(ready ? "Service ready." : "Service not ready yet.");
            return 0;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not read server status: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> SendCallbackAsync(int port, string code, string state)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var callback = new UriBuilder(Uri.UriSchemeHttp, "127.0.0.1", port, "/callback")
            {
                Query = $"code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}"
            }.Uri;
            using var response = await client.GetAsync(callback);
            response.EnsureSuccessStatusCode();
            return 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            Console.Error.WriteLine($"OIDC callback forwarding failed: {exception.Message}");
            return 1;
        }
    }

    internal static bool TryGetServerUri(string value, out Uri serverUri)
    {
        serverUri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) ||
            (parsed.Scheme == Uri.UriSchemeHttp && !parsed.IsLoopback) ||
            parsed.AbsolutePath != "/" ||
            parsed.Query.Length != 0 ||
            parsed.Fragment.Length != 0 ||
            parsed.UserInfo.Length != 0)
        {
            return false;
        }

        serverUri = parsed;
        return true;
    }

    private static void PrintUsage(TextWriter? output = null)
    {
        output ??= Console.Out;
        output.WriteLine("Usage: charac [--config <client.config>] status [--server https://access.example.com]");
        output.WriteLine("       charac login --server https://access.example.com");
        output.WriteLine("       charac connect [--server https://access.example.com]");
        output.WriteLine("       charac connect [--server https://access.example.com] --target <target-id>");
        output.WriteLine("       charac connect [--server https://access.example.com] --id <workspace-id>");
        output.WriteLine("       charac whoami --server https://access.example.com");
        output.WriteLine("       charac run --server https://access.example.com --target <target-id>");
        output.WriteLine("       charac attach --server https://access.example.com --id <workspace-id>");
        output.WriteLine("       charac probe-session --server http://127.0.0.1:5080");
        output.WriteLine("       charac shell --server http://127.0.0.1:5080");
        output.WriteLine("       charac callback --port <port> --code <code> --state <state>");
        output.WriteLine("Login opens the browser and verifies identity with Server; tokens are kept only for this command.");
        output.WriteLine("Connect signs in, lists your resources when no ID is supplied, and opens an interactive workspace. Ctrl+] detaches; Server keeps the SSH session.");
        output.WriteLine("Run and attach sign in with OIDC and verify a Linux ls through the managed SSH workspace.");
        output.WriteLine("Probe-session verifies one loopback-only Client -> Server -> SSH ls without OIDC.");
        output.WriteLine("Shell opens a loopback-only interactive SSH terminal; Ctrl+] exits (F12 in key fallback mode).");
    }
}
