using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Server.Ssh;

namespace Charac.Server.Hosting;

internal sealed record ClientSessionResource(Guid Id, Guid TargetId, string TargetName, string Account,
    DateTimeOffset CreatedAt, string State);
internal sealed record ClientResources(SshTargetResource[] Targets, ClientSessionResource[] Sessions);

internal static class ClientResourceRoutes
{
    public static void Map(WebApplication app) => app.MapGet("/resources", async (
        HttpContext context, SshAccessResolver access, SshWorkspaceManager manager,
        ActiveClientDevices devices) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!ClientSessionRoutes.TryIdentity(context.User, out var owner))
            return Results.Unauthorized();
        var status = devices.Check(owner,
            context.Request.Headers[ActiveClientDevices.HeaderName].ToString());
        if (status != ClientDeviceStatus.Allowed)
            return ClientSessionRoutes.DeviceError(status);
        var targets = await access.ListTargetsAsync(owner, context.RequestAborted);
        return Results.Ok(CreateSnapshot(owner, targets, manager.GetSnapshots()));
    }).RequireAuthorization();

    internal static ClientResources CreateSnapshot(ExternalIdentity owner, SshTargetResource[] targets,
        IEnumerable<SshWorkspaceSnapshot> workspaces)
    {
        var allowed = targets.ToDictionary(target => target.Id);
        var sessions = workspaces
            .Where(workspace => workspace.Owner == owner &&
                allowed.TryGetValue(workspace.TargetId, out var target) &&
                string.Equals(target.Account, workspace.Account, StringComparison.Ordinal))
            .OrderBy(workspace => workspace.CreatedAt).ThenBy(workspace => workspace.Id)
            .Select(workspace => new ClientSessionResource(workspace.Id, workspace.TargetId,
                allowed[workspace.TargetId].Name, workspace.Account, workspace.CreatedAt, workspace.State))
            .ToArray();
        return new(targets, sessions);
    }
}
