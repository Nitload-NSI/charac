using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Configuration;
using Charac.Server.Hosting;
using Charac.Server.Ssh;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Charac.Server.Authentication;
using Charac.Server.Data;
using Charac.Server.Data.Entities;

namespace Charac.Server.Tests;

public sealed class PostgresDatabaseTests
{
    public static bool HasPostgres => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("WORKSPACE_ACCESS_TEST_DATABASE"));

    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task MigrationsAreRepeatableAndConstraintsAreEnforced()
    {
        await using var fixture = await TestDatabase.CreateAsync(verifyMigrationRepeatability: true);
        await using var database = fixture.Open();
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));

        var identity = new AccessIdentity { Issuer = fixture.Issuer, Subject = "alice" };
        fixture.TrackIdentity(identity);
        database.Identities.Add(identity);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();

        database.Identities.Add(new AccessIdentity { Issuer = identity.Issuer, Subject = identity.Subject });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        database.ChangeTracker.Clear();

        database.Grants.Add(new AccessGrant
        {
            IdentityId = identity.Id, TargetId = Guid.NewGuid(),
            Account = "alice", CertificatePrincipal = "alice"
        });
        var foreign = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(foreign.InnerException).SqlState);
    }

    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task AuthorizationUsesOneQueryAndScopesIdentityAndTarget()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var grant = await fixture.SeedAsync();
        var counter = new QueryCounter();
        await using var database = fixture.Open(counter);
        var resolver = new SshAccessResolver(database, TimeProvider.System);
        var identity = new ExternalIdentity(grant.Identity.Issuer, grant.Identity.Subject);

        var decision = Assert.IsType<SshAccessDecision>(await resolver.ResolveAsync(identity, grant.TargetId, TestContext.Current.CancellationToken));
        Assert.Equal(1, counter.Reads);
        Assert.Equal(grant.Account, decision.Account);
        Assert.Equal(grant.CertificatePrincipal, decision.CertificatePrincipal);
        Assert.Equal(grant.Target.UserCertificateAuthority!.SigningKeyReference, decision.SigningKeyReference);
        Assert.Single(decision.HostPublicKeys);

        Assert.Null(await resolver.ResolveAsync(new(identity.Issuer, "bob"), grant.TargetId, TestContext.Current.CancellationToken));
        Assert.Null(await resolver.ResolveAsync(new("https://different.example", identity.Subject), grant.TargetId, TestContext.Current.CancellationToken));
        Assert.Null(await resolver.ResolveAsync(identity, Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Null(await resolver.ResolveAsync(new(identity.Issuer, "alice' OR '1'='1"), grant.TargetId, TestContext.Current.CancellationToken));
        Assert.Null(await resolver.ResolveAsync(identity, Guid.Empty, TestContext.Current.CancellationToken));
    }

    [Theory(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    [InlineData("identity")]
    [InlineData("target")]
    [InlineData("grant")]
    [InlineData("host-key")]
    [InlineData("expired")]
    public async Task DisabledOrExpiredManagementDataDeniesAccess(string change)
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var grant = await fixture.SeedAsync();
        await using (var administration = fixture.Open())
        {
            administration.Attach(grant);
            switch (change)
            {
                case "identity": grant.Identity.Enabled = false; break;
                case "target": grant.Target.Enabled = false; break;
                case "grant": grant.Enabled = false; break;
                case "host-key": grant.Target.HostKeys.ForEach(key => key.Enabled = false); break;
                case "expired": grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); break;
            }
            await administration.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var database = fixture.Open();
        var resolver = new SshAccessResolver(database, TimeProvider.System);
        Assert.Null(await resolver.ResolveAsync(new(grant.Identity.Issuer, grant.Identity.Subject), grant.TargetId, TestContext.Current.CancellationToken));
        Assert.Empty(await resolver.ListTargetsAsync(new(grant.Identity.Issuer, grant.Identity.Subject), TestContext.Current.CancellationToken));
    }

    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task NextResolutionSeesRevocationWithoutReusingTrackedAuthorization()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var grant = await fixture.SeedAsync();
        await using var database = fixture.Open();
        var resolver = new SshAccessResolver(database, TimeProvider.System);
        var identity = new ExternalIdentity(grant.Identity.Issuer, grant.Identity.Subject);
        Assert.NotNull(await resolver.ResolveAsync(identity, grant.TargetId, TestContext.Current.CancellationToken));

        await using (var administration = fixture.Open())
        {
            await administration.Grants.Where(x => x.Id == grant.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Enabled, false), TestContext.Current.CancellationToken);
        }

        Assert.Null(await resolver.ResolveAsync(identity, grant.TargetId, TestContext.Current.CancellationToken));
    }

    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task PasswordAccessDoesNotRequireCertificateAuthority()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var grant = await fixture.SeedAsync(withAuthority: false);
        await using var database = fixture.Open();
        var resolver = new SshAccessResolver(database, TimeProvider.System);
        var decision = Assert.IsType<SshAccessDecision>(await resolver.ResolveAsync(
            new(grant.Identity.Issuer, grant.Identity.Subject), grant.TargetId, TestContext.Current.CancellationToken));
        Assert.Equal(grant.Account, decision.Account);
        Assert.Null(decision.UserCertificateAuthorityId);
        Assert.Null(decision.SigningKeyReference);
    }

    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task LoginKeyBindingAndWorkspaceHistoryPersistAcrossContexts()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var grant = await fixture.SeedAsync(withAuthority: false);
        var key = new SshLoginKey
        {
            Name = "test-key-" + Guid.NewGuid().ToString("N"),
            FileName = "key-" + Guid.NewGuid().ToString("N"),
            Enabled = true
        };
        var workspaceId = Guid.CreateVersion7();
        fixture.TrackLoginKey(key.Id);
        fixture.TrackWorkspace(workspaceId);
        await using (var administration = fixture.Open())
        {
            administration.LoginKeys.Add(key);
            administration.WorkspaceRecords.Add(new WorkspaceRecord
            {
                Id = workspaceId, Issuer = grant.Identity.Issuer, Subject = grant.Identity.Subject,
                TargetId = grant.TargetId, Account = grant.Account, Authentication = "key",
                OpenedAt = DateTimeOffset.UtcNow
            });
            await administration.SaveChangesAsync(TestContext.Current.CancellationToken);
            await administration.Grants.Where(x => x.Id == grant.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.SshLoginKeyId, key.Id),
                    TestContext.Current.CancellationToken);
        }
        await using (var fresh = fixture.Open())
        {
            var resolved = Assert.IsType<SshAccessDecision>(await new SshAccessResolver(fresh,
                TimeProvider.System).ResolveAsync(new(grant.Identity.Issuer, grant.Identity.Subject),
                grant.TargetId, TestContext.Current.CancellationToken));
            Assert.Equal(key.Id, resolved.SshLoginKeyId);
            Assert.Equal(key.FileName, resolved.SshLoginKeyFileName);
            Assert.Equal("key", (await fresh.WorkspaceRecords.AsNoTracking()
                .SingleAsync(x => x.Id == workspaceId, TestContext.Current.CancellationToken)).Authentication);
        }
        await using (var administration = fixture.Open())
            await administration.LoginKeys.Where(x => x.Id == key.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Enabled, false),
                    TestContext.Current.CancellationToken);
        await using (var fresh = fixture.Open())
            Assert.Null(await new SshAccessResolver(fresh, TimeProvider.System).ResolveAsync(
                new(grant.Identity.Issuer, grant.Identity.Subject), grant.TargetId,
                TestContext.Current.CancellationToken));
    }
    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task LocalRegistrationCreatesPersistentTargetAndGrantWithoutOverwritingExistingData()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        await using var database = fixture.Open();
        var name = "test-managed-" + Guid.NewGuid().ToString("N");
        const string algorithm = "ssh-ed25519";
        var nameBytes = Encoding.ASCII.GetBytes(algorithm);
        var blob = new byte[4 + nameBytes.Length + 32];
        BinaryPrimitives.WriteUInt32BigEndian(blob, (uint)nameBytes.Length);
        nameBytes.CopyTo(blob, 4);
        blob.AsSpan(4 + nameBytes.Length).Fill(7);
        var hostKey = algorithm + " " + Convert.ToBase64String(blob);

        Assert.Equal(0, await AccessRegistrationCommand.RegisterTargetAsync(database,
            name, "192.0.2.20", 22, hostKey));
        var target = await database.Targets.SingleAsync(x => x.Name == name,
            TestContext.Current.CancellationToken);
        fixture.TrackTarget(target.Id);
        Assert.Equal(0, await AccessRegistrationCommand.RegisterTargetAsync(database,
            name, "192.0.2.20", 22, hostKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AccessRegistrationCommand.RegisterTargetAsync(database, name, "192.0.2.21", 22, hostKey));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["oidc:issuer"] = fixture.Issuer,
            ["oidc:client_id"] = "test-client"
        }).Build();
        Assert.Equal(0, await AccessRegistrationCommand.RegisterGrantAsync(database,
            configuration, name, "alice", "alice-os", null));
        var identity = await database.Identities.SingleAsync(x => x.Issuer == fixture.Issuer &&
            x.Subject == "alice", TestContext.Current.CancellationToken);
        fixture.TrackIdentity(identity);
        Assert.Equal(0, await AccessRegistrationCommand.RegisterGrantAsync(database,
            configuration, name, "alice", "alice-os", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AccessRegistrationCommand.RegisterGrantAsync(database, configuration,
                name, "alice", "other-account", null));
        await using var fresh = fixture.Open();
        var resolved = Assert.IsType<SshAccessDecision>(await new SshAccessResolver(fresh,
            TimeProvider.System).ResolveAsync(new(fixture.Issuer, "alice"), target.Id,
            TestContext.Current.CancellationToken));
        Assert.Equal("alice-os", resolved.Account);
        Assert.Null(resolved.SshLoginKeyId);
    }
    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task ResourceTargetsUseOneQueryAndRespectIdentityAndRevocation()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var grant = await fixture.SeedAsync(withAuthority: false);
        var counter = new QueryCounter();
        await using var database = fixture.Open(counter);
        var resolver = new SshAccessResolver(database, TimeProvider.System);
        var identity = new ExternalIdentity(grant.Identity.Issuer, grant.Identity.Subject);
        var target = Assert.Single(await resolver.ListTargetsAsync(identity, TestContext.Current.CancellationToken));
        Assert.Equal(1, counter.Reads);
        Assert.Equal(grant.TargetId, target.Id);
        Assert.Equal(grant.Target.Name, target.Name);
        Assert.Equal(grant.Account, target.Account);
        Assert.Empty(database.ChangeTracker.Entries());
        Assert.Empty(await resolver.ListTargetsAsync(new(identity.Issuer, "bob"), TestContext.Current.CancellationToken));
        Assert.Empty(await resolver.ListTargetsAsync(new("https://different.example/", identity.Subject), TestContext.Current.CancellationToken));
        await using (var administration = fixture.Open())
        {
            await administration.Grants.Where(x => x.Id == grant.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Enabled, false), TestContext.Current.CancellationToken);
        }
        Assert.Empty(await resolver.ListTargetsAsync(identity, TestContext.Current.CancellationToken));
    }
    [Fact(Skip = "Set WORKSPACE_ACCESS_TEST_DATABASE for PostgreSQL integration tests.", SkipUnless = nameof(HasPostgres))]
    public async Task ResourceEndpointUsesVerifiedIdentityAndRefreshSeesRevocation()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var grant = await fixture.SeedAsync(withAuthority: false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var signingKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = fixture.Issuer, ValidAudience = "rac-resource-test",
                IssuerSigningKey = signingKey, ValidateIssuer = true, ValidateAudience = true,
                ValidateLifetime = true, RequireSignedTokens = true
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped(_ => fixture.Open());
        builder.Services.AddScoped<SshAccessResolver>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SshWorkspaceManager>();
        builder.Services.AddSingleton<Charac.Server.Connections.ActiveClientDevices>();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        ClientResourceRoutes.Map(app);
        await app.StartAsync(timeout.Token);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            http.DefaultRequestHeaders.Add(Charac.Server.Connections.ActiveClientDevices.HeaderName,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            string Token(string subject) => new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = fixture.Issuer, Audience = "rac-resource-test",
                Subject = new ClaimsIdentity([new Claim("sub", subject)]),
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
            });
            var aliceToken = Token(grant.Identity.Subject);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
            using (var response = await http.GetAsync("/resources", timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                Assert.True(response.Headers.CacheControl!.NoStore);
                var resources = await response.Content.ReadFromJsonAsync<ClientResources>(timeout.Token);
                Assert.Equal(grant.TargetId, Assert.Single(resources!.Targets).Id);
                Assert.Empty(resources.Sessions);
            }
            var devices = app.Services.GetRequiredService<Charac.Server.Connections.ActiveClientDevices>();
            var originalDevice = http.DefaultRequestHeaders.GetValues(
                Charac.Server.Connections.ActiveClientDevices.HeaderName).Single();
            Assert.Equal(Charac.Server.Connections.ClientDeviceStatus.Allowed,
                devices.Acquire(new(fixture.Issuer, grant.Identity.Subject), originalDevice, out var deviceLease));
            http.DefaultRequestHeaders.Remove(Charac.Server.Connections.ActiveClientDevices.HeaderName);
            http.DefaultRequestHeaders.Add(Charac.Server.Connections.ActiveClientDevices.HeaderName,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            using (var conflict = await http.GetAsync("/resources", timeout.Token))
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            deviceLease!.Dispose();
            http.DefaultRequestHeaders.Remove(Charac.Server.Connections.ActiveClientDevices.HeaderName);
            http.DefaultRequestHeaders.Add(Charac.Server.Connections.ActiveClientDevices.HeaderName,
                originalDevice);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("bob"));
            var other = await http.GetFromJsonAsync<ClientResources>("/resources?subject=alice", timeout.Token);
            Assert.Empty(other!.Targets);
            Assert.Empty(other.Sessions);
            await using (var administration = fixture.Open())
            {
                await administration.Grants.Where(x => x.Id == grant.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Enabled, false), timeout.Token);
            }
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
            var revoked = await http.GetFromJsonAsync<ClientResources>("/resources", timeout.Token);
            Assert.Empty(revoked!.Targets);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TestDatabase(string connectionString) : IAsyncDisposable
    {
        private Guid? _identityId;
        private Guid? _authorityId;
        private Guid? _loginKeyId;
        private Guid? _workspaceId;
        private Guid? _targetId;

        public string Issuer { get; } = "https://auth.example/" + Guid.NewGuid().ToString("N");

        public static async Task<TestDatabase> CreateAsync(bool verifyMigrationRepeatability = false)
        {
            var configured = Environment.GetEnvironmentVariable("WORKSPACE_ACCESS_TEST_DATABASE")
                ?? throw new InvalidOperationException("A PostgreSQL test connection is required.");
            var settings = new NpgsqlConnectionStringBuilder(configured)
            {
                Pooling = true,
                IncludeErrorDetail = false
            };
            if (string.IsNullOrWhiteSpace(settings.Database))
                throw new InvalidOperationException("The PostgreSQL test connection must specify a dedicated database.");

            var fixture = new TestDatabase(settings.ConnectionString);
            await using (var database = fixture.Open())
            {
                database.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
                await database.Database.MigrateAsync(TestContext.Current.CancellationToken);
                if (verifyMigrationRepeatability)
                    await database.Database.MigrateAsync(TestContext.Current.CancellationToken);
            }
            return fixture;
        }

        public AccessDbContext Open(DbCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connectionString);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            return new AccessDbContext(options.Options);
        }

        public void TrackIdentity(AccessIdentity identity) => _identityId = identity.Id;
        public void TrackTarget(Guid id) => _targetId = id;
        public void TrackLoginKey(Guid id) => _loginKeyId = id;
        public void TrackWorkspace(Guid id) => _workspaceId = id;

        public async Task<AccessGrant> SeedAsync(bool withAuthority = true)
        {
            var authority = new UserCertificateAuthority
            {
                Name = "test-ca-" + Guid.NewGuid().ToString("N"), PublicKey = "ssh-ed25519 test-ca-public-key",
                SigningKeyReference = "test:signing-key", Enabled = true
            };
            var identity = new AccessIdentity
            {
                Issuer = Issuer, Subject = "alice", Enabled = true
            };
            var target = new SshTarget
            {
                Name = "test-target-" + Guid.NewGuid().ToString("N"), Address = "192.0.2.10",
                UserCertificateAuthority = withAuthority ? authority : null,
                UserCertificateAuthorityId = withAuthority ? authority.Id : null, Enabled = true
            };
            target.HostKeys.Add(new SshHostKey
            {
                TargetId = target.Id, PublicKey = "ssh-ed25519 test-host-public-key", Enabled = true
            });
            target.HostKeys.Add(new SshHostKey
            {
                TargetId = target.Id, PublicKey = "ssh-ed25519 disabled-host-public-key", Enabled = false
            });
            var grant = new AccessGrant
            {
                Identity = identity, IdentityId = identity.Id,
                Target = target, TargetId = target.Id, Account = "alice",
                CertificatePrincipal = "alice-terminal", Enabled = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            };
            _identityId = identity.Id;
            _authorityId = withAuthority ? authority.Id : null;
            _targetId = target.Id;
            await using var database = Open();
            database.Grants.Add(grant);
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            return grant;
        }

        public async ValueTask DisposeAsync()
        {
            await using var database = Open();
            await using var transaction = await database.Database.BeginTransactionAsync(CancellationToken.None);
            if (_workspaceId is { } workspaceId)
                await database.WorkspaceRecords.Where(x => x.Id == workspaceId).ExecuteDeleteAsync(CancellationToken.None);
            if (_targetId is { } targetId)
            {
                await database.Grants.Where(x => x.TargetId == targetId).ExecuteDeleteAsync(CancellationToken.None);
                await database.HostKeys.Where(x => x.TargetId == targetId).ExecuteDeleteAsync(CancellationToken.None);
                await database.Targets.Where(x => x.Id == targetId).ExecuteDeleteAsync(CancellationToken.None);
            }
            if (_loginKeyId is { } loginKeyId)
                await database.LoginKeys.Where(x => x.Id == loginKeyId).ExecuteDeleteAsync(CancellationToken.None);
            if (_identityId is { } identityId)
                await database.Identities.Where(x => x.Id == identityId).ExecuteDeleteAsync(CancellationToken.None);
            if (_authorityId is { } authorityId)
                await database.UserCertificateAuthorities.Where(x => x.Id == authorityId).ExecuteDeleteAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }
    }
}
