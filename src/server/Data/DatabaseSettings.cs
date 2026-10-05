using Microsoft.EntityFrameworkCore;
using Npgsql;
using Charac.Server.Hosting;

namespace Charac.Server.Data;

internal static class DatabaseSettings
{
    public static string? ConnectionString(IConfiguration configuration)
    {
        var environmentValue = Environment.GetEnvironmentVariable("WORKSPACE_ACCESS_DATABASE");
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue;

        return ConnectionStringFromConfiguration(configuration);
    }

    internal static string? ConnectionStringFromConfiguration(IConfiguration configuration)
    {
        var legacy = configuration["database:connection_string"];
        var host = configuration["database:host"];
        var port = configuration["database:port"];
        var database = configuration["database:name"];
        var username = configuration["database:username"];
        var password = configuration["database:password"];
        var timeout = configuration["database:timeout"];
        var maximumPoolSize = configuration["database:maximum_pool_size"];
        var hasFields = new[] { host, port, database, username, password, timeout, maximumPoolSize }
            .Any(value => value is not null);

        if (!string.IsNullOrWhiteSpace(legacy))
        {
            if (hasFields)
                throw new InvalidOperationException("Use either [database] connection_string or separate database fields, not both.");
            return legacy;
        }
        if (!hasFields)
            return null;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Configure [database] host, name and username.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host.Trim(),
            Database = database.Trim(),
            Username = username.Trim()
        };
        if (password is not null)
            builder.Password = password;
        if (port is not null)
            builder.Port = PositiveNumber(port, "port", 65535);
        if (timeout is not null)
            builder.Timeout = PositiveNumber(timeout, "timeout");
        if (maximumPoolSize is not null)
            builder.MaxPoolSize = PositiveNumber(maximumPoolSize, "maximum_pool_size");
        return builder.ConnectionString;
    }

    private static int PositiveNumber(string value, string field, int maximum = int.MaxValue)
    {
        if (!int.TryParse(value, out var number) || number < 1 || number > maximum)
            throw new InvalidOperationException($"[database] {field} must be an integer between 1 and {maximum}.");
        return number;
    }

    public static IConfiguration LoadConfiguration(string? configPath = null) => WorkspaceAccessConfiguration.Load(configPath);

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres =>
        {
            postgres.SetPostgresVersion(18, 0);
            postgres.CommandTimeout(10);
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "access");
        });
}
