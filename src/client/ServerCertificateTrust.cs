using System.Diagnostics;
using System.Globalization;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Charac.Client;

internal enum CertificateTrustKind
{
    SystemTrusted,
    Pinned,
    NeedsConfirmation,
    Rejected
}

internal sealed record CertificateTrustDecision(CertificateTrustKind Kind, string? Fingerprint = null,
    bool ReplacingExistingPin = false);
internal sealed record ServerCertificatePin(string Origin, string SpkiSha256, DateTimeOffset TrustedAt);
internal sealed record ServerCertificateTrustFile(ServerCertificatePin[] Pins);

internal static class ServerCertificateTrust
{
    private static readonly object FileLock = new();

    public static HttpClient CreateHttpClient(Uri origin, TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                Validate(origin, request.RequestUri, certificate, chain, errors)
        };
        return new HttpClient(handler) { Timeout = timeout };
    }

    public static void ConfigureWebSocket(ClientWebSocket socket, Uri origin) =>
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
        {
            if (certificate is null)
                return false;
            if (certificate is X509Certificate2 certificate2)
                return Validate(origin, origin, certificate2, chain, errors);
            using var converted = new X509Certificate2(certificate);
            return Validate(origin, origin, converted, chain, errors);
        };

    internal static CertificateTrustDecision Evaluate(Uri origin, Uri? requestUri,
        X509Certificate2? certificate, IReadOnlyCollection<X509ChainStatusFlags> chainStatuses,
        SslPolicyErrors errors,
        string? trustedFingerprint, DateTimeOffset now)
    {
        if (errors == SslPolicyErrors.None)
        {
            if (certificate is null || trustedFingerprint is null)
                return new(CertificateTrustKind.SystemTrusted);
            var systemTrustedFingerprint = GetSpkiFingerprint(certificate);
            return string.Equals(trustedFingerprint, systemTrustedFingerprint, StringComparison.Ordinal)
                ? new(CertificateTrustKind.Pinned, systemTrustedFingerprint)
                : new(CertificateTrustKind.NeedsConfirmation, systemTrustedFingerprint, ReplacingExistingPin: true);
        }
        if (!SameOrigin(origin, requestUri) || certificate is null ||
            (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0 ||
            now.UtcDateTime < certificate.NotBefore.ToUniversalTime() ||
            now.UtcDateTime > certificate.NotAfter.ToUniversalTime())
            return new(CertificateTrustKind.Rejected);

        const X509ChainStatusFlags permitted = X509ChainStatusFlags.UntrustedRoot |
            X509ChainStatusFlags.PartialChain;
        if (chainStatuses.Count == 0 || chainStatuses.Any(status => status == X509ChainStatusFlags.NoError ||
            (status & ~permitted) != 0))
            return new(CertificateTrustKind.Rejected);

        var fingerprint = GetSpkiFingerprint(certificate);
        if (string.Equals(trustedFingerprint, fingerprint, StringComparison.Ordinal))
            return new(CertificateTrustKind.Pinned, fingerprint);
        return new(CertificateTrustKind.NeedsConfirmation, fingerprint, trustedFingerprint is not null);
    }

    internal static string GetSpkiFingerprint(X509Certificate2 certificate) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()))
            .TrimEnd('=');

    internal static bool SameOrigin(Uri expected, Uri? actual) => actual is not null &&
        string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.IdnHost, actual.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        expected.Port == actual.Port;

    public static void List(TextWriter output)
    {
        var pins = LoadPins(TrustPath);
        if (pins.Length == 0)
        {
            output.WriteLine("No saved server certificate trust records.");
            return;
        }
        foreach (var pin in pins)
            output.WriteLine($"{pin.Origin} {pin.SpkiSha256} trusted={pin.TrustedAt:O}");
    }

    public static bool Remove(Uri origin)
    {
        lock (FileLock)
        {
            var pins = LoadPins(TrustPath);
            var filtered = pins.Where(pin => !string.Equals(pin.Origin, OriginKey(origin),
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (filtered.Length == pins.Length)
                return false;
            SavePins(TrustPath, filtered);
            return true;
        }
    }

    internal static ServerCertificatePin? LoadPin(string path, Uri origin) =>
        LoadPins(path).SingleOrDefault(pin =>
            string.Equals(pin.Origin, OriginKey(origin), StringComparison.OrdinalIgnoreCase));

    internal static void SavePin(string path, Uri origin, string fingerprint)
    {
        lock (FileLock)
        {
            var originKey = OriginKey(origin);
            var pins = LoadPins(path)
                .Where(pin => !string.Equals(pin.Origin, originKey, StringComparison.OrdinalIgnoreCase))
                .Append(new ServerCertificatePin(originKey, fingerprint, DateTimeOffset.UtcNow))
                .ToArray();
            SavePins(path, pins);
        }
    }

    private static bool Validate(Uri origin, Uri? requestUri, X509Certificate2? certificate,
        X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        var originKey = OriginKey(origin);
        ServerCertificatePin? currentPin;
        try
        {
            currentPin = LoadPin(TrustPath, origin);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Could not read TLS trust records; refusing the connection: {exception.Message}");
            return false;
        }
        var chainStatuses = chain?.ChainStatus.Select(status => status.Status).ToArray() ?? [];
        var decision = Evaluate(origin, requestUri, certificate, chainStatuses, errors,
            currentPin?.SpkiSha256, DateTimeOffset.UtcNow);
        if (decision.Kind == CertificateTrustKind.Pinned)
            return true;
        if (decision.Kind != CertificateTrustKind.NeedsConfirmation || certificate is null)
            return false;
        if (Console.IsInputRedirected || Console.IsErrorRedirected)
        {
            Console.Error.WriteLine($"Untrusted TLS certificate for {originKey}. Run connect in an interactive terminal to review it.");
            return false;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(decision.ReplacingExistingPin
            ? $"WARNING: The TLS public key for {originKey} has changed. This may indicate a legitimate Caddy key rotation or an interception attack."
            : $"The TLS certificate for {originKey} is not signed by a system-trusted CA.");
        Console.Error.WriteLine($"Subject: {SafeDisplay(certificate.Subject)}");
        Console.Error.WriteLine($"Issuer: {SafeDisplay(certificate.Issuer)}");
        Console.Error.WriteLine($"Valid: {certificate.NotBefore.ToUniversalTime():O} - {certificate.NotAfter.ToUniversalTime():O}");
        Console.Error.WriteLine($"Public key fingerprint: {decision.Fingerprint}");
        Console.Error.WriteLine("Verify this fingerprint with the server administrator through a separate trusted channel before accepting it.");
        Console.Error.Write("Trust this server key? [y/N]: ");
        var answer = Console.ReadLine()?.Trim();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            SavePin(TrustPath, origin, decision.Fingerprint!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Could not save TLS trust record; refusing the connection: {exception.Message}");
            return false;
        }
        Console.Error.WriteLine($"TLS public key trusted for {originKey}.");
        return true;
    }

    private static string TrustPath => Path.Combine(
        Path.GetDirectoryName(ClientConfiguration.DefaultPath)!, "server-trust.json");

    private static string OriginKey(Uri origin) =>
        $"{origin.Scheme.ToLowerInvariant()}://{origin.Authority.ToLowerInvariant()}";

    private static string SafeDisplay(string value) => new(value
        .Where(character => !char.IsControl(character) &&
            CharUnicodeInfo.GetUnicodeCategory(character) is not (UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
        .Take(256).ToArray());

    private static ServerCertificatePin[] LoadPins(string path)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            var file = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path),
                ClientJsonContext.Default.ServerCertificateTrustFile);
            return file?.Pins ?? throw new InvalidDataException("The server trust file is invalid.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidDataException("The server trust file is invalid.", exception);
        }
    }

    private static void SavePins(string path, ServerCertificatePin[] pins)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var contents = System.Text.Json.JsonSerializer.Serialize(
            new ServerCertificateTrustFile(pins), ClientJsonContext.Default.ServerCertificateTrustFile);
        using (var stream = new FileStream(temporary, options))
        using (var writer = new StreamWriter(stream))
            writer.Write(contents);
        RestrictPermissions(temporary);
        File.Move(temporary, path, overwrite: true);
        RestrictPermissions(path);
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
            throw new InvalidOperationException("Could not restrict the server trust file permissions.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Could not restrict the server trust file permissions.");
    }
}
