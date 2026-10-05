using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Charac.Server.Hosting;

namespace Charac.Server.Data;

/// <summary>EF tooling entry point; the runtime uses the same provider configuration.</summary>
internal sealed class AccessDbContextFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args)
    {
        var connection = DatabaseSettings.ConnectionString(DatabaseSettings.LoadConfiguration(ServerCommandLine.Parse(args).ConfigPath))
            ?? throw new InvalidOperationException("Configure [database] host, name and username or WORKSPACE_ACCESS_DATABASE.");
        var options = new DbContextOptionsBuilder<AccessDbContext>();
        DatabaseSettings.Configure(options, connection);
        var database = new AccessDbContext(options.Options);
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
        return database;
    }
}
