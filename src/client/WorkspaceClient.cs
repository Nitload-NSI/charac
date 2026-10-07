using System.Net;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Charac.Client;

internal static class WorkspaceClient
{
    public static async Task<int> RunAsync(string server, Guid? targetId, Guid? sessionId, bool interactive = false)
    {
        if (!Program.TryGetServerUri(server, out var origin) ||
            (!interactive && targetId is null && sessionId is null) ||
            (targetId is not null && sessionId is not null))
        {
            Console.Error.WriteLine("Provide a valid Server origin and one target or session ID.");
            return 2;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var deviceCredential = ClientDeviceCredential.LoadOrCreate();
            http.DefaultRequestHeaders.Add(ClientDeviceCredential.HeaderName, deviceCredential);
            if (interactive && (Console.IsInputRedirected || Console.IsOutputRedirected))
                throw new InvalidOperationException("Connect requires an interactive terminal.");
            var accessToken = await SignInAsync(http, origin, showIdentity: false);
            if (targetId is null && sessionId is null)
            {
                var selection = await ResourceMenu.SelectAsync(http, origin, Console.In, Console.Out);
                if (selection is null)
                {
                    Console.WriteLine("已取消连接。");
                    return 0;
                }
                targetId = selection.TargetId;
                sessionId = selection.SessionId;
            }
            var id = sessionId ?? await CreateAsync(http, origin, targetId!.Value, interactive);
            if (interactive)
            {
                Console.WriteLine($"Workspace: {id}");
                Console.WriteLine($"Reconnect: connect --server {origin} --id {id}");
                await RunInteractiveAsync(origin, id, accessToken, "/session", deviceCredential);
            }
            else
            {
                await RunLsAsync(origin, id, accessToken, "/session", deviceCredential);
                Console.WriteLine($"Workspace remains on Server: {id}");
            }
            return 0;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            Console.Error.WriteLine("This identity is already connected from another Client installation. Disconnect it first.");
            return 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or WebSocketException or
            TaskCanceledException or OperationCanceledException or InvalidOperationException or JsonException or
            IOException or UnauthorizedAccessException or FormatException or KeyNotFoundException or
            Win32Exception or ArgumentException)
        {
            Console.Error.WriteLine($"Workspace command failed: {exception.Message}");
            return 1;
        }
    }

    public static Task<int> RunLocalProbeAsync(string server) => RunLocalAsync(server, interactive: false);

    public static Task<int> RunLocalShellAsync(string server) => RunLocalAsync(server, interactive: true);

    private static async Task<int> RunLocalAsync(string server, bool interactive)
    {
        if (!Program.TryGetServerUri(server, out var origin) || !origin.IsLoopback)
        {
            Console.Error.WriteLine("Local probe requires a loopback Server origin.");
            return 2;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var id = await CreateLocalAsync(http, origin, interactive);
            try
            {
                if (interactive)
                    await RunInteractiveAsync(origin, id, null, "/local/session", null);
                else
                {
                    await RunLsAsync(origin, id, null, "/local/session", null);
                    Console.WriteLine("Client, Server, WebSocket and remote Linux ls verified.");
                }
                return 0;
            }
            finally
            {
                using var response = await http.DeleteAsync(new Uri(origin, $"/local/session?id={id}"));
                // The SSH peer may have already ended the workspace when its shell exited.
                if (response.StatusCode != HttpStatusCode.NotFound)
                    response.EnsureSuccessStatusCode();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or WebSocketException or
            TaskCanceledException or OperationCanceledException or InvalidOperationException or JsonException or
            IOException or FormatException or KeyNotFoundException or Win32Exception)
        {
            Console.Error.WriteLine($"Local session probe failed: {exception.Message}");
            return 1;
        }
    }

    public static async Task<int> WhoAmIAsync(string server)
    {
        if (!Program.TryGetServerUri(server, out var origin))
        {
            Console.Error.WriteLine("Server must be an HTTPS origin (HTTP is allowed only for loopback).");
            return 2;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Add(ClientDeviceCredential.HeaderName,
                ClientDeviceCredential.LoadOrCreate());
            await SignInAsync(http, origin, showIdentity: true);
            return 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            OperationCanceledException or InvalidOperationException or JsonException or IOException or
            UnauthorizedAccessException or KeyNotFoundException or ArgumentException)
        {
            Console.Error.WriteLine($"Login failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<string> SignInAsync(HttpClient http, Uri origin, bool showIdentity)
    {
        var oidc = await ReadOidcSettingsAsync(http, origin);
        var token = await OidcLogin.LoginAsync(http, oidc.Issuer, oidc.ClientId, oidc.DiscoveryUrl);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.GetAsync(new Uri(origin, "/identity"));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Server rejected the OIDC access token. Check issuer, audience and signing configuration.");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("This identity is already connected from another Client installation.");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (document.RootElement.GetProperty("issuer").GetString() is not { Length: > 0 } issuer ||
            document.RootElement.GetProperty("subject").GetString() is not { Length: > 0 } subject)
            throw new InvalidOperationException("Server returned an invalid identity.");
        Console.WriteLine("Signed in. Identity verified by Server.");
        if (showIdentity)
        {
            Console.WriteLine($"Issuer: {issuer}");
            Console.WriteLine($"Subject: {subject}");
            Console.WriteLine("This command does not save a login token; connect signs in when it starts.");
        }
        return token;
    }

    private static async Task<(string Issuer, string ClientId, string DiscoveryUrl)> ReadOidcSettingsAsync(
        HttpClient http, Uri origin)
    {
        using var response = await http.GetAsync(new Uri(origin, "/status"));
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var status = document.RootElement;
        if (status.GetProperty("service").GetString() != "workspace-access" ||
            status.GetProperty("protocolVersion").GetInt32() != 3 ||
            status.GetProperty("ready").GetBoolean() != true ||
            status.GetProperty("issuer").GetString() is not { Length: > 0 } issuer ||
            status.GetProperty("clientId").GetString() is not { Length: > 0 } clientId ||
            status.GetProperty("discoveryUrl").GetString() is not { Length: > 0 } discoveryUrl)
            throw new InvalidOperationException("Server OIDC session service is not configured.");
        return (issuer, clientId, discoveryUrl);
    }

    private static async Task<Guid> CreateAsync(HttpClient http, Uri origin, Guid targetId, bool interactive)
    {
        var (columns, rows) = interactive ? GetConsoleSize() : (80, 24);
        var endpoint = new Uri(origin, $"/session?target={targetId}");
        using var initial = await http.PostAsJsonAsync(endpoint,
            new CreateSessionRequest(columns, rows), ClientJsonContext.Default.CreateSessionRequest);
        HttpResponseMessage? passwordResponse = null;
        if (initial.StatusCode == HttpStatusCode.BadRequest &&
            await RequiresPasswordAsync(initial))
        {
            if (Console.IsInputRedirected)
                throw new InvalidOperationException("An interactive terminal is required to enter the SSH password.");
            WindowsTerminalInputMode.ResetTerminalState();
            TerminalLine.Align();
            Console.Write("SSH password: ");
            var password = ReadPassword();
            Console.WriteLine();
            passwordResponse = await http.PostAsJsonAsync(endpoint,
                new PasswordSessionRequest(password, columns, rows), ClientJsonContext.Default.PasswordSessionRequest);
        }
        using var response = passwordResponse;
        var result = response ?? initial;
        if (result.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("This authentik identity has no enabled grant for the target.");
        if (result.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("This identity is already connected from another Client installation.");
        result.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await result.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<bool> RequiresPasswordAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.TryGetProperty("error", out var error) &&
            error.GetString() == "password_required";
    }

    private static async Task<Guid> CreateLocalAsync(HttpClient http, Uri origin, bool interactive)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("An interactive terminal is required to enter the SSH password.");
        WindowsTerminalInputMode.ResetTerminalState();
        TerminalLine.Align();
        Console.Write("SSH password: ");
        var password = ReadPassword();
        Console.WriteLine();
        var (columns, rows) = interactive ? GetConsoleSize() : (80, 24);
        using var response = await http.PostAsJsonAsync(new Uri(origin, "/local/session"),
            new PasswordSessionRequest(password, columns, rows), ClientJsonContext.Default.PasswordSessionRequest);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task RunInteractiveAsync(Uri origin, Guid sessionId, string? accessToken,
        string path, string? deviceCredential)
    {
        using var socket = await OpenSessionSocketAsync(origin, sessionId, accessToken, path,
            deviceCredential: deviceCredential);
        await SendResizeAsync(socket, GetConsoleSize(), CancellationToken.None);

        using var terminalInput = TryEnableTerminalInput();
        var previousControlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        using var closing = new CancellationTokenSource();
        var mouseTracking = new TerminalMouseTracking();
        string? exitMessage = null;
        try
        {
            Console.WriteLine(terminalInput is null
                ? "Connected. Ctrl+] or F12 detaches from this workspace. Mouse input requires VT input support."
                : "Connected. Ctrl+] detaches from this workspace; mouse input is forwarded.");
            var receiver = ReceiveShellOutputAsync(socket, mouseTracking, closing.Token);
            try
            {
                if (terminalInput is not null)
                    await SendRawInputAsync(socket, receiver, mouseTracking, closing.Token);
                else
                    await SendShellInputAsync(socket, receiver, closing.Token);
                exitMessage = receiver.IsCompleted
                    ? (await receiver) switch
                    {
                        "ssh_closed" => "SSH connection closed; the workspace has ended.",
                        "workspace_closed" => "Workspace closed by Server.",
                        _ => "Server connection closed."
                    }
                    : "Detached from workspace.";
            }
            finally
            {
                closing.Cancel();
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Detached", CancellationToken.None);
                try { await receiver; }
                catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
                catch (WebSocketException) when (closing.IsCancellationRequested) { }
            }
        }
        finally
        {
            Console.TreatControlCAsInput = previousControlC;
            WindowsTerminalInputMode.ResetTerminalState(mouseTracking.InAlternateScreen);
            if (exitMessage is null)
                TerminalLine.Align();
            else
                TerminalLine.WriteStatus(exitMessage);
        }
    }

    private static async Task<string> ReceiveShellOutputAsync(ClientWebSocket socket, TerminalMouseTracking mouseTracking,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        var output = Console.OpenStandardOutput();
        long? lastSequence = null;
        while (true)
        {
            var frame = await socket.ReceiveAsync(buffer, cancellationToken);
            if (frame.MessageType == WebSocketMessageType.Close)
                return "server_disconnected";
            if (frame.MessageType != WebSocketMessageType.Text || !frame.EndOfMessage)
                throw new InvalidOperationException("The workspace WebSocket sent an invalid frame.");
            using var message = JsonDocument.Parse(buffer.AsMemory(0, frame.Count));
            var type = message.RootElement.GetProperty("type").GetString();
            if (type == "ended")
                return message.RootElement.GetProperty("reason").GetString() ?? "server_disconnected";
            if (type != "output")
                continue;
            var sequence = message.RootElement.GetProperty("sequence").GetInt64();
            if (lastSequence is { } previous && sequence != previous + 1)
                throw new InvalidOperationException("SSH output sequence gap; the terminal view needs a fresh session.");
            lastSequence = sequence;
            var data = Convert.FromBase64String(message.RootElement.GetProperty("data").GetString() ?? "");
            mouseTracking.ObserveOutput(data);
            await output.WriteAsync(data, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
    }

    private static async Task SendShellInputAsync(ClientWebSocket socket, Task receiver,
        CancellationToken cancellationToken)
    {
        var size = GetConsoleSize();
        while (!receiver.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            var nextSize = GetConsoleSize();
            if (nextSize != size)
            {
                await SendResizeAsync(socket, nextSize, cancellationToken);
                size = nextSize;
            }
            if (!Console.KeyAvailable)
            {
                await Task.Delay(30, cancellationToken);
                continue;
            }
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.F12 || key.KeyChar == '\u001d' ||
                key.Key == ConsoleKey.Oem6 && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                return;
            var input = EncodeShellKey(key);
            if (input.Length != 0)
                await SendInputAsync(socket, input);
        }
    }

    private static async Task SendRawInputAsync(ClientWebSocket socket, Task receiver,
        TerminalMouseTracking mouseTracking,
        CancellationToken cancellationToken)
    {
        using var sendLock = new SemaphoreSlim(1, 1);
        using var resizeStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var resize = WatchConsoleSizeAsync(socket, sendLock, resizeStop.Token);
        try
        {
            var input = Console.OpenStandardInput();
            var buffer = new byte[8192];
            Task<int>? pendingRead = null;
            while (!receiver.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                pendingRead ??= input.ReadAsync(buffer, cancellationToken).AsTask();
                var completed = mouseTracking.HasPendingInput
                    ? await Task.WhenAny(pendingRead, receiver, Task.Delay(30, cancellationToken))
                    : await Task.WhenAny(pendingRead, receiver);
                if (completed == receiver)
                    return;
                if (completed != pendingRead)
                {
                    var pending = mouseTracking.FlushPendingInput();
                    if (pending.Length > 0)
                    {
                        await sendLock.WaitAsync(cancellationToken);
                        try { await SendInputAsync(socket, pending); }
                        finally { sendLock.Release(); }
                    }
                    continue;
                }
                var count = await pendingRead;
                pendingRead = null;
                if (count == 0)
                    return;
                var detach = buffer.AsSpan(0, count).IndexOf((byte)0x1d);
                var length = detach >= 0 ? detach : count;
                if (length > 0)
                {
                    var filtered = mouseTracking.FilterInput(buffer.AsSpan(0, length));
                    if (filtered.Length > 0)
                    {
                        await sendLock.WaitAsync(cancellationToken);
                        try { await SendInputAsync(socket, filtered); }
                        finally { sendLock.Release(); }
                    }
                }
                if (detach >= 0)
                    return;
            }
        }
        finally
        {
            resizeStop.Cancel();
            try { await resize; }
            catch (OperationCanceledException) when (resizeStop.IsCancellationRequested) { }
        }
    }

    private static async Task WatchConsoleSizeAsync(ClientWebSocket socket, SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        var size = GetConsoleSize();
        while (true)
        {
            await Task.Delay(100, cancellationToken);
            var next = GetConsoleSize();
            if (next == size)
                continue;
            await sendLock.WaitAsync(cancellationToken);
            try { await SendResizeAsync(socket, next, cancellationToken); }
            finally { sendLock.Release(); }
            size = next;
        }
    }

    private static Task SendResizeAsync(ClientWebSocket socket, (int Columns, int Rows) size,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(
            new ResizeFrame("resize", size.Columns, size.Rows), ClientJsonContext.Default.ResizeFrame);
        return socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static byte[] EncodeShellKey(ConsoleKeyInfo key)
    {
        var escape = key.Key switch
        {
            ConsoleKey.UpArrow => "\u001b[A",
            ConsoleKey.DownArrow => "\u001b[B",
            ConsoleKey.RightArrow => "\u001b[C",
            ConsoleKey.LeftArrow => "\u001b[D",
            ConsoleKey.Home => "\u001b[H",
            ConsoleKey.End => "\u001b[F",
            ConsoleKey.Delete => "\u001b[3~",
            ConsoleKey.PageUp => "\u001b[5~",
            ConsoleKey.PageDown => "\u001b[6~",
            _ => null
        };
        if (escape is not null)
            return Encoding.ASCII.GetBytes(escape);
        if (key.Key == ConsoleKey.Enter)
            return [13];
        if (key.Key == ConsoleKey.Backspace)
            return [127];
        if (key.KeyChar != 0)
            return Encoding.UTF8.GetBytes(key.KeyChar.ToString());
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
            return [(byte)(key.Key - ConsoleKey.A + 1)];
        return [];
    }

    private static IDisposable? TryEnableTerminalInput() =>
        OperatingSystem.IsWindows()
            ? WindowsTerminalInputMode.TryEnable()
            : UnixTerminalInputMode.TryEnable();

    private static (int Columns, int Rows) GetConsoleSize()
    {
        if (Console.IsOutputRedirected)
            return (80, 24);
        try
        {
            return (Math.Clamp(Console.WindowWidth, 1, 500), Math.Clamp(Console.WindowHeight, 1, 200));
        }
        catch (IOException)
        {
            return (80, 24);
        }
    }

    internal static async Task<ClientWebSocket> OpenSessionSocketAsync(Uri origin, Guid sessionId,
        string? accessToken, string path, CancellationToken cancellationToken = default,
        string? deviceCredential = null)
    {
        if (!Program.TryGetServerUri(origin.ToString(), out _) || sessionId == Guid.Empty ||
            path is not ("/session" or "/local/session"))
            throw new InvalidOperationException("Invalid workspace endpoint.");
        if (path == "/session" && string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("An OIDC access token is required for a managed workspace.");
        if (path == "/session" && string.IsNullOrWhiteSpace(deviceCredential))
            throw new InvalidOperationException("A client device credential is required for a managed workspace.");
        if (path == "/local/session" && (!origin.IsLoopback || accessToken is not null))
            throw new InvalidOperationException("Local probe requires a loopback endpoint without an OIDC token.");
        var socket = new ClientWebSocket();
        try
        {
            if (accessToken is not null)
                socket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            if (deviceCredential is not null)
                socket.Options.SetRequestHeader(ClientDeviceCredential.HeaderName, deviceCredential);
            var endpoint = new UriBuilder(origin)
            {
                Scheme = origin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
                Path = path,
                Query = $"id={sessionId}"
            }.Uri;
            await socket.ConnectAsync(endpoint, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task RunLsAsync(Uri origin, Guid sessionId, string? accessToken,
        string path, string? deviceCredential)
    {
        using var socket = await OpenSessionSocketAsync(origin, sessionId, accessToken, path,
            deviceCredential: deviceCredential);
        var marker = Guid.NewGuid().ToString("N");
        var begin = "RAC_BEGIN_" + marker;
        var end = "RAC_END_" + marker + ":";
        await SendInputAsync(socket, "stty -echo\n");
        await SendInputAsync(socket,
            $"printf '\\n{begin}\\n'; ls; result=$?; printf '\\n{end}%s\\n' \"$result\"\n");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var received = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var frame = await socket.ReceiveAsync(buffer, timeout.Token);
            if (frame.MessageType != WebSocketMessageType.Text || !frame.EndOfMessage)
                throw new InvalidOperationException("The workspace WebSocket closed or sent an invalid frame.");
            using var message = JsonDocument.Parse(buffer.AsMemory(0, frame.Count));
            if (message.RootElement.GetProperty("type").GetString() != "output")
                continue;
            var data = Convert.FromBase64String(message.RootElement.GetProperty("data").GetString() ?? "");
            received.Write(data);
            if (received.Length > 256 * 1024)
                throw new InvalidOperationException("Linux ls output exceeded the CLI limit.");
            var text = Encoding.UTF8.GetString(received.GetBuffer().AsSpan(0, (int)received.Length));
            var finish = text.IndexOf(end, StringComparison.Ordinal);
            while (finish >= 0 && (text.Length <= finish + end.Length ||
                !char.IsAsciiDigit(text[finish + end.Length])))
                finish = text.IndexOf(end, finish + end.Length, StringComparison.Ordinal);
            if (finish < 0)
                continue;
            var start = text.LastIndexOf(begin, finish, StringComparison.Ordinal);
            if (start < 0)
                continue;
            var listing = text[(start + begin.Length)..finish].Trim();
            Console.WriteLine("Remote ls output:");
            Console.WriteLine(listing.Length == 0 ? "(empty directory)" : listing);
            if (text[finish + end.Length] != '0')
                throw new InvalidOperationException("Linux ls returned a nonzero exit code.");
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Detached", CancellationToken.None);
            return;
        }
    }

    private static Task SendInputAsync(ClientWebSocket socket, string input) =>
        SendInputAsync(socket, Encoding.UTF8.GetBytes(input));

    private static Task SendInputAsync(ClientWebSocket socket, ReadOnlyMemory<byte> input)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(
            new InputFrame("input", Convert.ToBase64String(input.Span)), ClientJsonContext.Default.InputFrame);
        return socket.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static string ReadPassword()
    {
        var characters = new List<char>();
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                    return new string(characters.ToArray());
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (characters.Count > 0)
                        characters.RemoveAt(characters.Count - 1);
                }
                else if (!char.IsControl(key.KeyChar))
                    characters.Add(key.KeyChar);
            }
        }
        finally
        {
            for (var index = 0; index < characters.Count; index++)
                characters[index] = '\0';
        }
    }
}
