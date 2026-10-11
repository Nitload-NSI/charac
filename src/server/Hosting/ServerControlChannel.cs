using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Server.Data;
using Charac.Server.Ssh;

namespace Charac.Server.Hosting;

internal sealed record ServerControlSession(Guid Id, Guid TargetId, string Issuer, string Subject,
    string Account, string State, string? ConnectionPeer, DateTimeOffset CreatedAt,
    DateTimeOffset? AttachedAt);
internal sealed record ServerControlReply(bool Ok, string Message, int ProcessId, int Workspaces)
{
    public ServerControlSession[]? Sessions { get; init; }
    public int? Disconnected { get; init; }
}
internal sealed record ServerControlRequest(string[] Arguments);

/// <summary>Same-user local control channel; never exposed through the HTTP listener.</summary>
internal sealed class ServerControlChannel : IHostedService, IDisposable
{
    private readonly string _configPath;
    private readonly IConfigurationRoot _liveConfiguration;
    private readonly SshWorkspaceManager _workspaces;
    private readonly ActiveClientDevices _devices;
    private readonly ILogger<ServerControlChannel> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Dictionary<string, string?> _startupSettings;
    private readonly bool _allowDisconnect;
    private readonly string? _oidcIssuer;
    private Task? _listener;

    public ServerControlChannel(string configPath, IConfigurationRoot liveConfiguration,
        SshWorkspaceManager workspaces, ActiveClientDevices devices,
        ILogger<ServerControlChannel> logger)
    {
        _configPath = configPath;
        _liveConfiguration = liveConfiguration;
        _workspaces = workspaces;
        _devices = devices;
        _logger = logger;
        var startup = WorkspaceAccessConfiguration.Load(configPath);
        _startupSettings = RestartRequiredSettings(startup);
        _allowDisconnect = string.Equals(startup["management:allow_disconnect"], "true",
            StringComparison.OrdinalIgnoreCase);
        _oidcIssuer = startup["oidc:issuer"]?.Trim();
    }

    public static string PipeName() => "charac-server-control";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var pipe = CreatePipe();
        _listener = ListenAsync(pipe, _stopping.Token);
        return Task.CompletedTask;
    }

    private NamedPipeServerStream CreatePipe() => new(PipeName(), PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync(NamedPipeServerStream first, CancellationToken cancellationToken)
    {
        NamedPipeServerStream? pipe = first;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (pipe)
                {
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    try
                    {
                        await HandleAsync(pipe, cancellationToken);
                    }
                    catch (IOException exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug(exception, "Control client disconnected.");
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("Control client timed out.");
                    }
                }
                pipe = CreatePipe();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException exception) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "Control channel stopped.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Local control channel failed.");
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var command = await ReadCommandAsync(reader, deadline.Token);
        var request = TryReadRequest(command);
        var reply = request is not null
            ? await RunManagementCommandAsync(request.Arguments)
            : command switch
        {
            "status" => new ServerControlReply(true, "Server is running.", Environment.ProcessId, _workspaces.Count),
            "sessions" => ListSessions(),
            "reload" => Reload(),
            _ when command?.StartsWith("disconnect ", StringComparison.Ordinal) == true =>
                DisconnectUser(command["disconnect ".Length..]),
            _ => new ServerControlReply(false, "Unknown control command.", Environment.ProcessId, _workspaces.Count)
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(), deadline.Token);
    }

    private static ServerControlRequest? TryReadRequest(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith('{'))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ServerControlRequest>(command);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ServerControlReply> RunManagementCommandAsync(string[] arguments)
    {
        if (arguments.Length == 0 || arguments.Any(string.IsNullOrWhiteSpace))
            return ControlFailure("Management command arguments are invalid.");

        using var output = new StringWriter();
        using var error = new StringWriter();
        var previousOutput = Console.Out;
        var previousError = Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        int exitCode;
        try
        {
            exitCode = arguments[0] switch
            {
                "grant" => await AccessRegistrationCommand.RunAsync(arguments, _configPath),
                "endpoint_regist" when arguments is ["endpoint_regist", var name, var address, var keyPath] =>
                    await EndpointRegistrationCommand.RunAsync(name, address, keyPath, _configPath),
                "database" => await RunDatabaseCommandAsync(arguments),
                _ => -1
            };
        }
        finally
        {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
        }

        if (exitCode < 0)
            return ControlFailure("Unsupported management command.");
        var message = output.ToString().Trim();
        var errorText = error.ToString().Trim();
        if (errorText.Length > 0)
            message = errorText;
        return new ServerControlReply(exitCode == 0, message.Length == 0
            ? (exitCode == 0 ? "Management command completed." : "Management command failed.")
            : message, Environment.ProcessId, _workspaces.Count);
    }

    private async Task<int> RunDatabaseCommandAsync(string[] arguments) => arguments switch
    {
        ["database", "migrate"] => await DatabaseCommands.MigrateAsync(_configPath),
        ["database", "key", ..] => await SshLoginKeyCommand.RunAsync(arguments, _configPath),
        ["database", "target" or "grant", ..] => await AccessRegistrationCommand.RunAsync(arguments, _configPath),
        ["database", "enroll-probe", var targetName, var subject, var keyName] =>
            await ProbeEnrollmentCommand.RunAsync(targetName, subject, keyName, _configPath),
        ["database", "list-probes"] => await ProbeRecordsCommand.RunAsync(false, _configPath),
        ["database", "clear-probes"] => await ProbeRecordsCommand.RunAsync(true, _configPath),
        ["database", "grant-probe", var targetText, var issuer, var subject]
            when Guid.TryParse(targetText, out var targetId) =>
            await ProbeGrantCommand.RunAsync(targetId, issuer, subject, _configPath),
        _ => -1
    };

    private ServerControlReply ControlFailure(string message) =>
        new(false, message, Environment.ProcessId, _workspaces.Count);

    private static async Task<string?> ReadCommandAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var command = new StringBuilder();
        var character = new char[1];
        while (true)
        {
            if (await reader.ReadAsync(character.AsMemory(), cancellationToken) == 0)
                return command.Length == 0 ? null : command.ToString().TrimEnd('\r');
            if (character[0] == '\n')
                return command.ToString().TrimEnd('\r');
            if (command.Length == 512)
                return null;
            command.Append(character[0]);
        }
    }

    internal ServerControlReply DisconnectUser(string subject)
    {
        if (!_allowDisconnect || string.IsNullOrWhiteSpace(_oidcIssuer))
            return new ServerControlReply(false,
                "User disconnection is disabled; configure [management] allow_disconnect=true and restart Server.",
                Environment.ProcessId, _workspaces.Count);
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 256 ||
            subject.Any(char.IsControl))
            return new ServerControlReply(false, "Invalid subject.",
                Environment.ProcessId, _workspaces.Count);
        var owner = new ExternalIdentity(_oidcIssuer, subject);
        using var blocking = _devices.BeginDisconnect(owner);
        var count = _workspaces.DisconnectUser(owner);
        _logger.LogWarning("Local operator disconnected {Count} client connection(s) for OIDC subject {Subject}.",
            count, subject);
        return new ServerControlReply(true, "User client connections disconnected; SSH workspaces remain.",
            Environment.ProcessId, _workspaces.Count) { Disconnected = count };
    }

    internal ServerControlReply ListSessions()
    {
        var sessions = _workspaces.GetSnapshots()
            .OrderBy(snapshot => snapshot.CreatedAt)
            .ThenBy(snapshot => snapshot.Id)
            .Select(snapshot => new ServerControlSession(snapshot.Id, snapshot.TargetId,
                snapshot.Owner.Issuer, snapshot.Owner.Subject, snapshot.Account, snapshot.State,
                snapshot.ConnectionPeer, snapshot.CreatedAt, snapshot.AttachedAt)).ToArray();
        return new ServerControlReply(true, "Current workspaces.", Environment.ProcessId, sessions.Length)
        {
            Sessions = sessions
        };
    }

    internal ServerControlReply Reload()
    {
        try
        {
            var candidate = WorkspaceAccessConfiguration.Load(_configPath);
            if (!SameSettings(_startupSettings, RestartRequiredSettings(candidate)))
                return new ServerControlReply(false,
                    "Configuration changes outside [Logging] require a service restart; running workspaces were kept.",
                    Environment.ProcessId, _workspaces.Count);

            _liveConfiguration.Reload();
            return new ServerControlReply(true, "Logging configuration reloaded; running workspaces were kept.",
                Environment.ProcessId, _workspaces.Count);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or ArgumentException)
        {
            _logger.LogWarning(exception, "Configuration reload rejected.");
            return new ServerControlReply(false, "Configuration reload failed; running configuration was kept.",
                Environment.ProcessId, _workspaces.Count);
        }
    }

    private static Dictionary<string, string?> RestartRequiredSettings(IConfigurationRoot configuration) =>
        configuration.AsEnumerable()
            .Where(entry => entry.Value is not null &&
                !entry.Key.StartsWith("Logging:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

    private static bool SameSettings(Dictionary<string, string?> left, Dictionary<string, string?> right) =>
        left.Count == right.Count && left.All(entry => right.TryGetValue(entry.Key, out var value) &&
            string.Equals(entry.Value, value, StringComparison.Ordinal));

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_listener is not null)
            await _listener.WaitAsync(cancellationToken);
    }

    public void Dispose() => _stopping.Dispose();
}
