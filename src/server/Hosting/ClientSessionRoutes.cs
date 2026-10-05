using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Renci.SshNet.Common;
using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Server.Data;
using Charac.Server.Data.Entities;
using Charac.Server.Ssh;

namespace Charac.Server.Hosting;

internal static class ClientSessionRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/identity", (HttpContext context, ActiveClientDevices devices) =>
        {
            if (!TryIdentity(context.User, out var identity))
                return Results.Unauthorized();
            var credential = context.Request.Headers[ActiveClientDevices.HeaderName].ToString();
            if (credential.Length > 0)
            {
                var status = devices.Check(identity, credential);
                if (status != ClientDeviceStatus.Allowed)
                    return DeviceError(status);
            }
            return Results.Ok(new { issuer = identity.Issuer, subject = identity.Subject });
        }).RequireAuthorization();
        app.MapPost("/session", CreateAsync).RequireAuthorization();
        app.MapGet("/session", AttachAsync).RequireAuthorization();
        app.MapDelete("/session", DeleteAsync).RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        [FromQuery(Name = "target")] Guid? targetId, CreateSessionBody body, HttpContext context,
        SshAccessResolver access, SshBroker broker, SshWorkspaceManager manager,
        AccessDbContext database,
        ActiveClientDevices devices)
    {
        if (targetId is null || targetId == Guid.Empty)
            return Results.BadRequest(new { error = "target_required" });
        if (body.Columns is < 1 or > 500 || body.Rows is < 1 or > 200)
            return Results.BadRequest(new { error = "invalid_session_request" });
        if (!TryIdentity(context.User, out var owner))
            return Results.Unauthorized();

        var deviceStatus = devices.Acquire(owner,
            context.Request.Headers[ActiveClientDevices.HeaderName].ToString(), out var deviceLease);
        if (deviceStatus != ClientDeviceStatus.Allowed)
            return DeviceError(deviceStatus);
        using var heldDevice = deviceLease;

        var decision = await access.ResolveAsync(owner, targetId.Value, context.RequestAborted);
        if (decision is null)
            return Results.Forbid();
        if (decision.SshLoginKeyId is null && string.IsNullOrEmpty(body.Password))
            return Results.BadRequest(new { error = "password_required" });
        var workspaceId = Guid.CreateVersion7();
        var request = new SshSessionRequest(workspaceId, targetId.Value, owner, decision.Account,
            new SshTerminalSize((ushort)body.Columns, (ushort)body.Rows));
        try
        {
            var session = decision.SshLoginKeyId is not null
                ? await broker.OpenWithKeyAsync(request, context.RequestAborted)
                : await broker.OpenWithPasswordAsync(request, body.Password!, context.RequestAborted);
            var record = new WorkspaceRecord
            {
                Id = workspaceId, Issuer = owner.Issuer, Subject = owner.Subject,
                TargetId = targetId.Value, Account = decision.Account,
                Authentication = decision.SshLoginKeyId is null ? "password" : "key",
                OpenedAt = DateTimeOffset.UtcNow
            };
            var recorded = false;
            try
            {
                database.WorkspaceRecords.Add(record);
                await database.SaveChangesAsync(context.RequestAborted);
                recorded = true;
                if (!devices.TryUse(heldDevice!, () => manager.Add(session, owner), out _))
                {
                    session.Dispose();
                    record.ClosedAt = DateTimeOffset.UtcNow;
                    record.EndReason = "start_rejected";
                    await database.SaveChangesAsync(CancellationToken.None);
                    return Results.Conflict(new { error = "device_in_use" });
                }
            }
            catch
            {
                session.Dispose();
                if (recorded && record.ClosedAt is null)
                {
                    record.ClosedAt = DateTimeOffset.UtcNow;
                    record.EndReason = "start_failed";
                    await database.SaveChangesAsync(CancellationToken.None);
                }
                throw;
            }
            return Results.Ok(new { id = workspaceId });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception exception) when (exception is SshException or SocketException or TimeoutException or
            IOException or InvalidOperationException or FormatException)
        {
            return Results.Json(new { error = "ssh_connection_failed" }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task AttachAsync(
        [FromQuery(Name = "id")] Guid? sessionId, [FromQuery] bool? takeover,
        HttpContext context, SshWorkspaceManager manager, SshAccessResolver access,
        ActiveClientDevices devices)
    {
        if (sessionId is null || sessionId == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!TryIdentity(context.User, out var owner))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        var workspace = manager.Find(sessionId.Value);
        if (workspace is null || workspace.Owner != owner)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var decision = await access.ResolveAsync(owner, workspace.TargetId, context.RequestAborted);
        if (decision is null || !string.Equals(decision.Account, workspace.Session.Account, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        await StreamAsync(context, workspace, owner, takeover == true, devices);
    }

    internal static async Task StreamAsync(HttpContext context, SshWorkspace workspace,
        ExternalIdentity owner, bool takeover, ActiveClientDevices? devices = null)
    {
        ActiveClientDevices.ClientDeviceLease? acquiredDevice = null;
        if (devices is not null)
        {
            var status = devices.Acquire(owner,
                context.Request.Headers[ActiveClientDevices.HeaderName].ToString(), out acquiredDevice);
            if (status != ClientDeviceStatus.Allowed)
            {
                context.Response.StatusCode = status == ClientDeviceStatus.Occupied
                    ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
                return;
            }
        }
        using var heldDevice = acquiredDevice;
        var peer = context.Connection.RemoteIpAddress?.ToString();
        SshWorkspaceAttachment? attached;
        if (devices is null)
            attached = workspace.Attach(owner, takeover, peer);
        else if (!devices.TryUse(acquiredDevice!, () => workspace.Attach(owner, takeover, peer),
            out attached))
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }
        if (attached is null || attached.Status != AcquisitionStatus.Acquired || attached.Lease is null ||
            attached.Output is null || attached.Ended is null || attached.Disconnected is null)
        {
            context.Response.StatusCode = attached?.Status == AcquisitionStatus.TakeoverRequired
                ? StatusCodes.Status409Conflict : StatusCodes.Status404NotFound;
            return;
        }

        WebSocket? socket = null;
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            var sender = SendOutputAsync(socket, attached.Output, attached.Ended, ending.Token);
            var receiver = ReceiveInputAsync(socket, workspace, attached.Lease, ending.Token);
            await CompleteStreamAsync(sender, receiver, attached.Disconnected, attached.Ended, ending.Token);
        }
        finally
        {
            ending.Cancel();
            socket?.Abort();
            socket?.Dispose();
            workspace.Detach(attached.Lease);
        }
    }

    internal static async Task CompleteStreamAsync(Task<bool> sender, Task receiver,
        Task disconnected, Task ended, CancellationToken cancellationToken)
    {
        var first = await Task.WhenAny(sender, receiver, disconnected);
        // Workspace shutdown completes both Ended and Disconnected. Drain output and send
        // the ended frame before closing; a viewer takeover only completes Disconnected.
        if ((first == sender || ended.IsCompleted) && await sender)
            await Task.WhenAny(receiver, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
    }
    internal static IResult DeviceError(ClientDeviceStatus status) => status == ClientDeviceStatus.Occupied
        ? Results.Conflict(new { error = "device_in_use" })
        : Results.BadRequest(new { error = "device_credential_required" });

    private static async Task<IResult> DeleteAsync(
        [FromQuery(Name = "id")] Guid? sessionId, HttpContext context,
        SshWorkspaceManager manager, SshAccessResolver access, ActiveClientDevices devices)
    {
        if (sessionId is null || sessionId == Guid.Empty)
            return Results.BadRequest(new { error = "session_required" });
        if (!TryIdentity(context.User, out var owner))
            return Results.Unauthorized();
        var deviceStatus = devices.Check(owner,
            context.Request.Headers[ActiveClientDevices.HeaderName].ToString());
        if (deviceStatus != ClientDeviceStatus.Allowed)
            return DeviceError(deviceStatus);
        var workspace = manager.Find(sessionId.Value);
        if (workspace is null || workspace.Owner != owner)
            return Results.NotFound();
        var decision = await access.ResolveAsync(owner, workspace.TargetId, context.RequestAborted);
        if (decision is null || !string.Equals(decision.Account, workspace.Session.Account, StringComparison.Ordinal))
            return Results.Forbid();
        workspace.Dispose();
        return Results.NoContent();
    }

    internal static async Task<bool> SendOutputAsync(WebSocket socket,
        ChannelReader<SshOutput> output, Task<string> ended, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var chunk in output.ReadAllAsync(cancellationToken))
            {
                var message = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "output",
                    sequence = chunk.Sequence,
                    data = Convert.ToBase64String(chunk.Data)
                });
                await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken);
            }
            if (!ended.IsCompletedSuccessfully)
                return false;
            var endMessage = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "ended",
                reason = ended.Result
            });
            await socket.SendAsync(endMessage, WebSocketMessageType.Text, true, cancellationToken);
            if (socket.State == WebSocketState.Open)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Workspace ended", cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or IOException)
        {
            return false;
        }
    }

    private static async Task ReceiveInputAsync(WebSocket socket, SshWorkspace workspace,
        ConnectionLease lease, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close)
                    return;
                if (received.MessageType != WebSocketMessageType.Text || !received.EndOfMessage)
                    return;
                using var message = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                var root = message.RootElement;
                var type = root.GetProperty("type").GetString();
                if (type == "input")
                {
                    var data = Convert.FromBase64String(root.GetProperty("data").GetString() ?? "");
                    if (!workspace.TryInput(lease, data))
                        return;
                }
                else if (type == "resize")
                {
                    var columns = root.GetProperty("columns").GetInt32();
                    var rows = root.GetProperty("rows").GetInt32();
                    if (columns is < 1 or > 500 || rows is < 1 or > 200 ||
                        !workspace.TryResize(lease, new SshTerminalSize((ushort)columns, (ushort)rows)))
                        return;
                }
                else
                    return;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or
            IOException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
        }
    }

    internal static bool TryIdentity(ClaimsPrincipal principal, out ExternalIdentity identity)
    {
        var issuer = principal.FindFirst("iss")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            identity = null!;
            return false;
        }
        identity = new ExternalIdentity(issuer, subject);
        return true;
    }

    private sealed record CreateSessionBody(string? Password = null, int Columns = 80, int Rows = 24);
}
