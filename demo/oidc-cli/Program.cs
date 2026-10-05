using System.Net.Http.Headers;
using System.Text.Json;
using Charac.Server.Authentication;
using Charac.Server.Hosting;

namespace Charac.Client;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            PrintUsage();
            return 0;
        }

        if (!TryReadConfigPath(args, out var configPath))
        {
            PrintUsage();
            return 2;
        }

        try
        {
            var configuration = WorkspaceAccessConfiguration.Load(configPath);
            var oidc = OidcSettings.FromConfiguration(configuration) ??
                throw new InvalidOperationException("Configure [oidc] issuer and client_id before running this demo.");
            var server = ReadServerUri(configuration["oidc:server_url"]);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var accessToken = await OidcLogin.LoginAsync(http, oidc.Issuer, oidc.ClientId,
                oidc.DiscoveryUrl);
            Console.WriteLine("authentik/OIDC login succeeded. Access token remains in this process.");

            if (server is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "/identity"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                using var identity = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                Console.WriteLine($"Verified issuer: {identity.RootElement.GetProperty("issuer").GetString()}");
                Console.WriteLine($"Verified subject: {identity.RootElement.GetProperty("subject").GetString()}");
            }

            return 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            OperationCanceledException or InvalidOperationException or JsonException or IOException or
            KeyNotFoundException or ArgumentException)
        {
            Console.Error.WriteLine($"OIDC demo failed: {exception.Message}");
            return 1;
        }
    }

    private static bool TryReadConfigPath(string[] args, out string? configPath)
    {
        configPath = null;
        if (args.Length == 0)
            return true;
        if (args is ["--config", var path] && !string.IsNullOrWhiteSpace(path) &&
            !path.StartsWith("--", StringComparison.Ordinal))
        {
            configPath = path;
            return true;
        }
        return false;
    }

    private static Uri? ReadServerUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var server) ||
            (server.Scheme != Uri.UriSchemeHttps && !(server.Scheme == Uri.UriSchemeHttp && server.IsLoopback)) ||
            server.UserInfo.Length != 0 || server.AbsolutePath != "/" ||
            server.Query.Length != 0 || server.Fragment.Length != 0)
            throw new InvalidOperationException("[oidc] server_url must be an HTTPS origin or a loopback HTTP origin.");
        return server;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project demo/oidc-cli -- [--config <workspace-access.config path>]");
    }
}
