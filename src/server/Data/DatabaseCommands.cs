using Microsoft.EntityFrameworkCore;

namespace Charac.Server.Data;

internal static class DatabaseCommands
{
    public static async Task<int> MigrateAsync(string? configPath = null)
    {
        try
        {
            await using var database = new AccessDbContextFactory().CreateDbContext(configPath is null ? [] : ["--config", configPath]);
            await database.Database.MigrateAsync();
            Console.WriteLine("Database migrations applied.");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException or ArgumentException)
        {
            Console.Error.WriteLine($"Database migration failed ({exception.GetType().Name}). Check connection configuration and migration permissions.");
            return 1;
        }
    }
}
