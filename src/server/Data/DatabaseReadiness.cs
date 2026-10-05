using Microsoft.EntityFrameworkCore;

namespace Charac.Server.Data;

internal sealed class DatabaseReadiness(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<string> CheckAsync(CancellationToken cancellationToken)
    {
        if (DatabaseSettings.ConnectionString(configuration) is null)
            return "not_configured";

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
            if (!await database.Database.CanConnectAsync(cancellationToken))
                return "unavailable";
            if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return "migration_required";
            await database.Grants.AsNoTracking().AnyAsync(grant =>
                grant.Identity.Enabled && grant.Target.Enabled &&
                grant.Target.HostKeys.Any(), cancellationToken);
            return "ready";
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or InvalidOperationException or ArgumentException)
        {
            return "unavailable";
        }
    }
}
