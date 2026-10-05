using Microsoft.AspNetCore.Mvc;
using Charac.Server.Authentication;

namespace Charac.Server.Hosting;

internal static class ClientApiRoutes
{
    public static void Map(WebApplication app, OidcSettings? oidc, bool databaseConfigured)
    {
        app.MapGet("/status", () => Results.Ok(new
        {
            service = "workspace-access",
            protocolVersion = 3,
            ready = oidc is not null && databaseConfigured,
            issuer = oidc?.Issuer,
            clientId = oidc?.ClientId,
            discoveryUrl = oidc?.DiscoveryUrl
        }));

        if (oidc is not null && databaseConfigured)
        {
            ClientSessionRoutes.Map(app);
            ClientResourceRoutes.Map(app);
            return;
        }
        app.MapGet("/resources", () => Unavailable());
        app.MapPost("/session", ([FromQuery(Name = "target")] Guid? targetId) =>
            ValidateId(targetId, "target_required"));

        app.MapGet("/session", ([FromQuery(Name = "id")] Guid? sessionId) =>
            ValidateId(sessionId, "session_required"));
    }

    private static IResult ValidateId(Guid? id, string error) =>
        id.HasValue && id.Value != Guid.Empty
            ? Unavailable()
            : Results.BadRequest(new { error });

    private static IResult Unavailable() => Results.Json(
        new { error = "session_service_not_ready" },
        statusCode: StatusCodes.Status503ServiceUnavailable);
}
