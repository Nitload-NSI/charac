using System.Diagnostics;
using System.Text.Json;

namespace Charac.Client;

internal sealed record OidcTokenSet(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

internal static class OidcSession
{
    private static string SessionPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(ClientConfiguration.DefaultPath)!, "session.json");

    public static OidcTokenSet? Load(string issuer, string clientId)
    {
        if (!File.Exists(SessionPath))
            return null;
        try
        {
            var session = JsonSerializer.Deserialize(File.ReadAllText(SessionPath), ClientJsonContext.Default.OidcSessionFile);
            return session is { Issuer: var savedIssuer, ClientId: var savedClientId }
                && string.Equals(savedIssuer, issuer, StringComparison.Ordinal)
                && string.Equals(savedClientId, clientId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(session.AccessToken)
                ? new(session.AccessToken, session.RefreshToken, session.ExpiresAt)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string issuer, string clientId, OidcTokenSet tokens)
    {
        var directory = System.IO.Path.GetDirectoryName(SessionPath)!;
        Directory.CreateDirectory(directory);
        var temporary = SessionPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var session = new OidcSessionFile(issuer, clientId, tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAt);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(temporary, options))
        using (var writer = new StreamWriter(stream))
            writer.Write(JsonSerializer.Serialize(session, ClientJsonContext.Default.OidcSessionFile));
        RestrictPermissions(temporary);
        File.Move(temporary, SessionPath, overwrite: true);
        RestrictPermissions(SessionPath);
    }

    public static void Delete()
    {
        try { File.Delete(SessionPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { path, "/inheritance:r", "/grant:r", $"{Environment.UserName}:F" }
        });
        if (process is null)
            throw new InvalidOperationException("Could not restrict the OIDC session file permissions.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Could not restrict the OIDC session file permissions.");
    }
}

internal sealed record OidcSessionFile(string Issuer, string ClientId, string AccessToken,
    string? RefreshToken, DateTimeOffset ExpiresAt);