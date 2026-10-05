using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Charac.Client;

internal static class OidcLogin
{
    public static async Task<string> LoginAsync(HttpClient client, string issuer, string clientId,
        string discoveryUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) ||
            issuerUri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("The Server returned invalid OIDC settings.");

        if (!Uri.TryCreate(discoveryUrl, UriKind.Absolute, out var discoveryUri) ||
            discoveryUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The Server returned an invalid OIDC discovery URL.");
        using var discoveryResponse = await client.GetAsync(discoveryUri, cancellationToken);
        discoveryResponse.EnsureSuccessStatusCode();
        using var discovery = await JsonDocument.ParseAsync(
            await discoveryResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var metadata = discovery.RootElement;
        if (metadata.GetProperty("issuer").GetString() != issuer)
            throw new InvalidOperationException("OIDC discovery issuer does not match the Server configuration.");
        var authorize = HttpsEndpoint(metadata.GetProperty("authorization_endpoint").GetString());
        var token = HttpsEndpoint(metadata.GetProperty("token_endpoint").GetString());

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/callback";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "openid",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };
        var authorizationUri = new UriBuilder(authorize)
        {
            Query = string.Join('&', query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        }.Uri;
        Console.WriteLine($"Open this URL to sign in: {authorizationUri}");
        try
        {
            Process.Start(new ProcessStartInfo(authorizationUri.ToString()) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or
            PlatformNotSupportedException or InvalidOperationException)
        {
            Console.WriteLine("Open the URL manually in a browser on this device.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var code = await ReceiveCodeAsync(listener, state, timeout.Token);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier
        });
        using var tokenResponse = await client.PostAsync(token, body, cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(await DescribeTokenErrorAsync(tokenResponse, cancellationToken));
        using var tokenDocument = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (tokenDocument.RootElement.GetProperty("token_type").GetString() is not { } tokenType ||
            !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            tokenDocument.RootElement.GetProperty("access_token").GetString() is not { Length: > 0 } accessToken)
            throw new InvalidOperationException("OIDC token response did not contain a bearer access token.");
        return accessToken;
    }

    private static async Task<string> ReceiveCodeAsync(TcpListener listener, string expectedState,
        CancellationToken cancellationToken)
    {
        using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (requestLine is null || requestLine.Length > 8192 || !requestLine.StartsWith("GET ", StringComparison.Ordinal))
            throw new InvalidOperationException("The OIDC callback request is invalid.");
        string? header;
        do
        {
            header = await reader.ReadLineAsync(cancellationToken);
            if (header is { Length: > 8192 })
                throw new InvalidOperationException("The OIDC callback request is too large.");
        } while (header is { Length: > 0 });

        var path = requestLine.Split(' ', 3)[1];
        if (!Uri.TryCreate("http://127.0.0.1" + path, UriKind.Absolute, out var callback) ||
            callback.AbsolutePath != "/callback")
            throw new InvalidOperationException("The OIDC callback path is invalid.");
        var values = callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts.Length == 2 ? parts[1] : ""));
        values.TryGetValue("code", out var code);
        var valid = values.TryGetValue("state", out var state) &&
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(state),
                Encoding.ASCII.GetBytes(expectedState)) &&
            !string.IsNullOrWhiteSpace(code);
        var page = CallbackPage
            .Replace("__FONT_REGULAR__", ReadFontBase64("Regular"), StringComparison.Ordinal)
            .Replace("__FONT_BOLD__", ReadFontBase64("Bold"), StringComparison.Ordinal)
            .Replace("__STATE__", valid ? "success" : "failure", StringComparison.Ordinal)
            .Replace("__ICON__", valid ? "✓" : "!", StringComparison.Ordinal)
            .Replace("__TITLE__", valid ? "已收到授权码" : "授权回调未通过验证", StringComparison.Ordinal)
            .Replace("__MESSAGE__", valid
                ? "请返回终端，等待 RAC 完成登录。现在可以关闭此页面。"
                : "请关闭此页面，在终端重新发起登录。", StringComparison.Ordinal);
        var content = Encoding.UTF8.GetBytes(page);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {content.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Content-Security-Policy: default-src 'none'; font-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
        if (!valid)
            throw new InvalidOperationException("OIDC callback state or authorization code is invalid.");
        return code!;
    }

    private static async Task<string> DescribeTokenErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var message = $"OIDC token exchange failed (HTTP {(int)response.StatusCode})";
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.String)
                return message;

            var code = error.GetString();
            if (string.IsNullOrEmpty(code) || code.Length > 80 ||
                !code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
                return message;

            message += $": {code}";
            if (document.RootElement.TryGetProperty("error_description", out var description) &&
                description.ValueKind == JsonValueKind.String)
            {
                var detail = description.GetString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    detail = new string(detail.Take(300).Select(character =>
                        char.IsControl(character) ? ' ' : character).ToArray());
                    message += $" ({detail})";
                }
            }
        }
        catch (JsonException)
        {
            // A non-JSON proxy or provider error still reports the HTTP status.
        }

        return message;
    }

    private static Uri HttpsEndpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("OIDC discovery returned an invalid HTTPS endpoint.");
        return uri;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ReadFontBase64(string weight)
    {
        using var stream = typeof(OidcLogin).Assembly.GetManifestResourceStream(
            $"Charac.Fonts.IBMPlexMono-{weight}.woff2") ??
            throw new InvalidOperationException("The bundled callback font is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Convert.ToBase64String(buffer.ToArray());
    }
    private const string CallbackPage = """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="color-scheme" content="dark">
          <title>RAC · 授权回调</title>
          <style>
            @font-face { font-family: "IBM Plex Mono"; font-style: normal; font-weight: 400; font-display: swap;
                         src: url("data:font/woff2;base64,__FONT_REGULAR__") format("woff2"); }
            @font-face { font-family: "IBM Plex Mono"; font-style: normal; font-weight: 700; font-display: swap;
                         src: url("data:font/woff2;base64,__FONT_BOLD__") format("woff2"); }
            :root { font-family: "IBM Plex Mono", "Microsoft YaHei", "PingFang SC", system-ui, sans-serif; color: #172b3d; background: #1c2732; }
            * { box-sizing: border-box; }
            body { min-height: 100vh; min-height: 100svh; margin: 0; display: grid; place-items: center; padding: 32px 20px;
                   background-image: linear-gradient(#a8d8f508 1px, transparent 1px),
                                     linear-gradient(90deg, #a8d8f508 1px, transparent 1px);
                   background-size: 32px 32px; }
            .card { --accent: #a8d8f5; width: min(100%, 760px); border: 2px solid #10202f;
                    background: #eeeade; box-shadow: -8px -8px 0 var(--accent), 12px 12px 0 #10202f; }
            .failure { --accent: #e44e43; }
            .header { display: flex; align-items: center; justify-content: space-between; gap: 24px;
                      padding: 22px 28px; background: #10202f; color: #eeeade; }
            .brand { display: flex; align-items: center; gap: 18px; }
            .mark { padding: 6px 8px; background: #a8d8f5; color: #10202f;
                    font: 700 27px/1.15 "IBM Plex Mono", monospace; letter-spacing: -.07em; }
            .label { font: 700 11px/1.6 "IBM Plex Mono", "Microsoft YaHei", monospace; letter-spacing: .12em; }
            .index { color: var(--accent); white-space: nowrap; }
            .content { padding: clamp(26px, 6vw, 48px); }
            .status { display: flex; align-items: center; gap: 10px; }
            .symbol { display: grid; place-items: center; width: 26px; height: 26px; background: var(--accent);
                      color: #10202f; font-size: 18px; font-weight: 800; }
            h1 { margin: 24px 0 18px; font-size: clamp(28px, 5vw, 44px); font-weight: 800;
                 letter-spacing: -.04em; line-height: 1.25; overflow-wrap: anywhere; }
            p { margin: 0; max-width: 34em; color: #485767; font-size: 16px; line-height: 1.8; }
            .footer { display: flex; align-items: center; gap: 16px; padding: 18px 28px;
                      border-top: 2px solid #172b3d; background: #e0e3df; }
            .footer::before { content: ""; width: 8px; height: 8px; flex: 0 0 8px; background: #365d7a; }
            .note { color: #485767; font-size: 12px; }
            @media (max-width: 480px) {
              .header { padding: 18px 20px; gap: 12px; }
              .brand { gap: 12px; }
              .index { display: none; }
              .footer { padding: 16px 20px; }
            }
          </style>
        </head>
        <body>
          <main class="card __STATE__">
            <header class="header">
              <div class="brand"><span class="mark">RAC</span><span class="label">WORKSPACE<br>ACCESS</span></div>
              <span class="label index" aria-hidden="true">AUTH / 01</span>
            </header>
            <div class="content">
              <div class="status"><span class="symbol" aria-hidden="true">__ICON__</span><span class="label">授权回调 / AUTHORIZATION</span></div>
              <h1>__TITLE__</h1>
              <p>__MESSAGE__</p>
            </div>
            <footer class="footer"><p class="note">此页面由本机客户端提供。登录结果以终端显示为准。</p></footer>
          </main>
          <script>history.replaceState(null, '', '/callback');</script>
        </body>
        </html>
        """;
}
