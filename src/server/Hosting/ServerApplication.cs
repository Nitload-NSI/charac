using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Server.Data;
using Charac.Server.Ssh;

namespace Charac.Server.Hosting;

internal static class ServerApplication
{
    public static async Task RunAsync(string[] args, string? configPath = null, bool localProbe = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        var resolvedConfigPath = WorkspaceAccessConfiguration.PathToLoad(configPath);
        builder.Configuration.AddIniFile(resolvedConfigPath, optional: false, reloadOnChange: false);
        var settings = ServerSettings.FromConfiguration(builder.Configuration);
        var oidc = OidcSettings.FromConfiguration(builder.Configuration);
        SshProbeSettings? probeSettings = null;
        if (localProbe)
        {
            if (!LocalProbeRoutes.IsLoopbackOrigin(settings.ListenAddress) ||
                !LocalProbeRoutes.IsLoopbackHost(settings.Domain))
                throw new InvalidOperationException("Local probe requires numeric loopback [server] listen and domain.");
            probeSettings = SshProbeSettings.FromConfiguration(builder.Configuration);
        }
        builder.WebHost.UseUrls(settings.ListenAddress);
        if (settings.TrustedProxy is not null)
            AddTrustedProxy(builder.Services, settings.TrustedProxy);

        if (oidc is not null)
        {
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = oidc.Issuer;
                    options.MetadataAddress = oidc.DiscoveryUrl;
                    options.Audience = oidc.ClientId;
                    options.RequireHttpsMetadata = true;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = oidc.Issuer,
                        ValidateAudience = true,
                        ValidAudience = oidc.ClientId,
                        ValidateLifetime = true,
                        RequireSignedTokens = true,
                        ClockSkew = TimeSpan.FromMinutes(1)
                    };
                });
            builder.Services.AddAuthorization();
        }

        var connection = DatabaseSettings.ConnectionString(builder.Configuration);
        if (connection is not null)
        {
            builder.Services.AddDbContext<AccessDbContext>(options => DatabaseSettings.Configure(options, connection));
            builder.Services.AddScoped<OidcIdentityBinding>();
            builder.Services.AddScoped<SshAccessResolver>();
            builder.Services.AddScoped<SshBroker>();
            builder.Services.AddSingleton<WorkspaceRecordStore>();
        }
        builder.Services.AddSingleton(new SshKeyStore(builder.Configuration));
        builder.Services.AddSingleton<SshSessionRegistry>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<SshSessionRegistry>());
        builder.Services.AddSingleton<SshWorkspaceManager>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<SshWorkspaceManager>());
        builder.Services.AddSingleton<ActiveClientDevices>();
        builder.Services.AddSingleton(provider => new ServerControlChannel(resolvedConfigPath,
            builder.Configuration, provider.GetRequiredService<SshWorkspaceManager>(),
            provider.GetRequiredService<ActiveClientDevices>(),
            provider.GetRequiredService<ILogger<ServerControlChannel>>()));
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ServerControlChannel>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DatabaseReadiness>();

        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddWindowsService(options => options.ServiceName = "CharacServer");
        }
        else if (OperatingSystem.IsLinux())
        {
            builder.Services.AddSystemd();
        }
        else
        {
            throw new PlatformNotSupportedException("Charac Server supports Windows and Linux.");
        }

        var app = builder.Build();
        if (settings.TrustedProxy is not null)
            app.UseForwardedHeaders();
        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            if (!string.Equals(context.Request.Host.Host, settings.Domain, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next(context);
        });
        if (oidc is not null)
        {
            app.UseAuthentication();
            app.UseAuthorization();
            if (connection is not null)
            {
                app.Use(async (context, next) =>
                {
                    if (context.User.Identity?.IsAuthenticated == true &&
                        ClientSessionRoutes.TryIdentity(context.User, out var identity))
                    {
                        await context.RequestServices.GetRequiredService<OidcIdentityBinding>()
                            .BindAsync(identity, context.RequestAborted);
                    }
                    await next(context);
                });
            }
        }
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        app.MapGet("/health/ready", async (DatabaseReadiness database, CancellationToken cancellationToken) =>
        {
            var databaseStatus = await database.CheckAsync(cancellationToken);
            List<string> pending = ["ssh-backend-credential", "terminal-client", "session-e2e-validation"];
            if (oidc is null)
                pending.Insert(0, "oidc");
            if (databaseStatus != "ready")
                pending.Insert(0, "database");
            return Results.Json(new
            {
                status = "initializing",
                stage = "integration-pending",
                database = databaseStatus,
                pending
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
        ClientApiRoutes.Map(app, oidc, connection is not null);
        if (probeSettings is not null)
            LocalProbeRoutes.Map(app, probeSettings);
        app.Logger.LogInformation("Server started with SSH workspace manager and OIDC configuration status: {Configured}.", oidc is not null);
        await app.RunAsync();
    }

    internal static void AddTrustedProxy(IServiceCollection services, IPAddress proxy) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Add(proxy);
        });
}
