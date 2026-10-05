using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Renci.SshNet.Common;
using Charac.Server.Authentication;
using Charac.Server.Ssh;

namespace Charac.Server.Hosting;

/// <summary>Explicit loopback-only transport diagnostic; never maps the public session routes.</summary>
internal static class LocalProbeRoutes
{
    private static readonly ExternalIdentity Owner = new("urn:workspace-access:local-transport-probe", "loopback");

    public static bool IsLoopbackHost(string host) =>
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    public static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri.Host);

    public static void Map(WebApplication app, SshProbeSettings settings)
    {
        app.MapPost("/local/session", (CreateSessionBody body, HttpContext context,
            SshSessionRegistry sessions, SshWorkspaceManager manager) =>
            CreateAsync(body, context, settings, sessions, manager));
        app.MapGet("/local/session", AttachAsync);
        app.MapDelete("/local/session", Delete);
    }

    private static bool IsLocal(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);

    private static async Task<IResult> CreateAsync(CreateSessionBody body, HttpContext context,
        SshProbeSettings settings, SshSessionRegistry sessions, SshWorkspaceManager manager)
    {
        if (!IsLocal(context))
            return Results.NotFound();
        if (string.IsNullOrEmpty(body.Password) || body.Columns is < 1 or > 500 || body.Rows is < 1 or > 200)
            return Results.BadRequest(new { error = "invalid_session_request" });

        var id = Guid.CreateVersion7();
        try
        {
            var session = await SshPasswordConnection.OpenAsync(id, Guid.CreateVersion7(),
                settings.Address, settings.Port, settings.Account, [settings.HostPublicKey],
                body.Password, new SshTerminalSize((ushort)body.Columns, (ushort)body.Rows),
                "xterm-256color", sessions.Remove, context.RequestAborted);
            try
            {
                sessions.Add(session);
                manager.Add(session, Owner);
            }
            catch
            {
                session.Dispose();
                throw;
            }
            return Results.Ok(new { id });
        }
        catch (Exception exception) when (exception is SshException or SocketException or TimeoutException or IOException)
        {
            return Results.Json(new { error = "ssh_connection_failed" },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task AttachAsync([FromQuery(Name = "id")] Guid? id,
        HttpContext context, SshWorkspaceManager manager)
    {
        if (!IsLocal(context))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (id is null || id == Guid.Empty || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var workspace = manager.Find(id.Value);
        if (workspace is null || workspace.Owner != Owner)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await ClientSessionRoutes.StreamAsync(context, workspace, Owner, takeover: false);
    }

    private static IResult Delete([FromQuery(Name = "id")] Guid? id,
        HttpContext context, SshWorkspaceManager manager)
    {
        if (!IsLocal(context))
            return Results.NotFound();
        if (id is null || id == Guid.Empty)
            return Results.BadRequest(new { error = "session_required" });
        var workspace = manager.Find(id.Value);
        if (workspace is null || workspace.Owner != Owner)
            return Results.NotFound();
        workspace.Dispose();
        return Results.NoContent();
    }

    private sealed record CreateSessionBody(string Password, int Columns = 80, int Rows = 24);
}
