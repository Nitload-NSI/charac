namespace Charac.Client;

internal static class ClientConfiguration
{
    public static string DefaultPath
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrWhiteSpace(applicationData))
                    throw new InvalidOperationException("Cannot locate the user configuration directory.");
                return Path.Combine(applicationData, "Charac", "client.config");
            }

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg))
                return Path.Combine(xdg, "charac", "client.config");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                throw new InvalidOperationException("Cannot locate the user configuration directory.");
            return Path.Combine(home, ".config", "charac", "client.config");
        }
    }

    private static string LegacyPath
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CharRAC", "client.config");
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg))
                return Path.Combine(xdg, "char-rac", "client.config");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "char-rac", "client.config");
        }
    }
    public static string LoadServer(string? path = null)
    {
        path = path is null ? (File.Exists(DefaultPath) ? DefaultPath :
            File.Exists(LegacyPath) ? LegacyPath : DefaultPath) : Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Client configuration not found: {path}. Create [client] server=https://your-server or pass --server.");

        string? section = null;
        string? server = null;
        foreach (var source in File.ReadLines(path))
        {
            var line = source.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals <= 0)
                throw new InvalidOperationException($"Invalid client configuration line in {path}.");
            if (!string.Equals(section, "client", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(line[..equals].Trim(), "server", StringComparison.OrdinalIgnoreCase))
                continue;
            if (server is not null)
                throw new InvalidOperationException($"Duplicate [client] server in {path}.");
            server = line[(equals + 1)..].Trim().Trim('"');
        }
        if (server is null || !Program.TryGetServerUri(server, out var uri))
            throw new InvalidOperationException($"[client] server must be an HTTPS origin (HTTP only for loopback): {path}.");
        return uri.ToString();
    }
}