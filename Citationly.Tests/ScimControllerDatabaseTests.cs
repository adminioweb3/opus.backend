using System.Data;
using System.Security.Cryptography;
using System.Text;
using Citationly.API.Controllers;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Repositories;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Xunit;

namespace Citationly.Tests;

public class ScimControllerDatabaseTests
{
    [Fact]
    public async Task CreateUser_UpdatesExistingUserInSameOrganizationOnly()
    {
        await using var database = await ScimTestDatabase.TryCreateAsync();
        if (database == null) return;

        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await database.SeedOrganizationAsync(orgId, "Acme", "org-a-token");
        await database.SeedUserAsync(userId, orgId, "scim:existing", "owner@example.com", "Original Name", "Viewer");

        var controller = database.CreateController("org-a-token");
        var result = await controller.CreateUser(new ScimUserRequest
        {
            UserName = "OWNER@example.com",
            DisplayName = "Updated Owner",
            Role = "Admin"
        });

        var created = Assert.IsType<CreatedResult>(result);
        Assert.NotNull(created.Value);

        await using var connection = database.CreateOpenConnection();
        var rows = (await connection.QueryAsync<(Guid Id, Guid OrganizationId, string DisplayName, string Role)>(
            """
            SELECT Id, OrganizationId, DisplayName, Role
            FROM Users
            WHERE LOWER(Email) = LOWER(@Email)
            """,
            new { Email = "owner@example.com" })).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(userId, row.Id);
        Assert.Equal(orgId, row.OrganizationId);
        Assert.Equal("Updated Owner", row.DisplayName);
        Assert.Equal("Admin", row.Role);
    }

    [Fact]
    public async Task CreateUser_ReturnsConflict_WhenEmailBelongsToDifferentOrganization()
    {
        await using var database = await ScimTestDatabase.TryCreateAsync();
        if (database == null) return;

        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await database.SeedOrganizationAsync(orgA, "Acme", "org-a-token");
        await database.SeedOrganizationAsync(orgB, "Other Co", "org-b-token");
        await database.SeedUserAsync(Guid.NewGuid(), orgB, "scim:other", "shared@example.com", "Other User", "Viewer");

        var controller = database.CreateController("org-a-token");
        var result = await controller.CreateUser(new ScimUserRequest
        {
            UserName = "shared@example.com",
            DisplayName = "Intruder",
            Role = "Admin"
        });

        Assert.IsType<ConflictObjectResult>(result);

        await using var connection = database.CreateOpenConnection();
        var orgAUserCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE OrganizationId = @OrgId AND LOWER(Email) = LOWER(@Email)",
            new { OrgId = orgA, Email = "shared@example.com" });

        Assert.Equal(0, orgAUserCount);
    }

    [Fact]
    public async Task UserMutations_ReturnNotFound_ForDifferentOrganizationUser()
    {
        await using var database = await ScimTestDatabase.TryCreateAsync();
        if (database == null) return;

        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var orgBUser = Guid.NewGuid();
        await database.SeedOrganizationAsync(orgA, "Acme", "org-a-token");
        await database.SeedOrganizationAsync(orgB, "Other Co", "org-b-token");
        await database.SeedUserAsync(orgBUser, orgB, "scim:other", "target@example.com", "Target User", "Viewer");

        var controller = database.CreateController("org-a-token");

        Assert.IsType<NotFoundResult>(await controller.GetUser(orgBUser));
        Assert.IsType<NotFoundResult>(await controller.PatchUser(orgBUser, new ScimPatchRequest
        {
            Operations = new List<ScimPatchOperation>
            {
                new() { Op = "replace", Path = "role", Value = "Admin" }
            }
        }));
        Assert.IsType<NotFoundResult>(await controller.DeleteUser(orgBUser));

        await using var connection = database.CreateOpenConnection();
        var row = await connection.QuerySingleAsync<(Guid OrganizationId, string Role)>(
            "SELECT OrganizationId, Role FROM Users WHERE Id = @Id",
            new { Id = orgBUser });

        Assert.Equal(orgB, row.OrganizationId);
        Assert.Equal("Viewer", row.Role);
    }

    private sealed class ScimTestDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;
        private readonly string _schemaConnectionString;

        private ScimTestDatabase(string adminConnectionString, string schema, string schemaConnectionString)
        {
            _adminConnectionString = adminConnectionString;
            _schema = schema;
            _schemaConnectionString = schemaConnectionString;
        }

        public static async Task<ScimTestDatabase?> TryCreateAsync()
        {
            var rawConnectionString = Environment.GetEnvironmentVariable("CITATIONLY_TEST_DATABASE")
                ?? "Host=localhost;Database=opus_db;Username=postgres;Password=postgres";
            var schema = "phase0_scim_" + Guid.NewGuid().ToString("N");

            try
            {
                var adminBuilder = new NpgsqlConnectionStringBuilder(rawConnectionString)
                {
                    Timeout = 2,
                    CommandTimeout = 5
                };

                await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");

                var schemaBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
                {
                    SearchPath = schema
                };

                var database = new ScimTestDatabase(
                    adminBuilder.ConnectionString,
                    schema,
                    schemaBuilder.ConnectionString);
                await database.CreateSchemaAsync();
                return database;
            }
            catch (NpgsqlException)
            {
                return null;
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public NpgsqlConnection CreateOpenConnection()
        {
            var connection = new NpgsqlConnection(_schemaConnectionString);
            connection.Open();
            return connection;
        }

        public ScimController CreateController(string bearerToken)
        {
            var controller = new ScimController(
                new SsoRepository(new ConnectionFactory(_schemaConnectionString)),
                new ConnectionFactory(_schemaConnectionString),
                new NoopAuditLogService());
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.Request.Headers.Authorization = $"Bearer {bearerToken}";
            return controller;
        }

        public async Task SeedOrganizationAsync(Guid organizationId, string name, string scimToken)
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                "INSERT INTO Organizations (Id, Name) VALUES (@Id, @Name)",
                new { Id = organizationId, Name = name });
            await connection.ExecuteAsync(
                """
                INSERT INTO SsoConnections (Id, OrganizationId, Provider, Domain, ScimEnabled, ScimTokenHash, IsEnabled)
                VALUES (@Id, @OrganizationId, 'OIDC', @Domain, TRUE, @ScimTokenHash, TRUE)
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Domain = $"{name.ToLowerInvariant().Replace(" ", "")}.example",
                    ScimTokenHash = HashToken(scimToken)
                });
        }

        public async Task SeedUserAsync(Guid userId, Guid organizationId, string firebaseUid, string email, string displayName, string role)
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                """
                INSERT INTO Users (Id, OrganizationId, FirebaseUid, Email, DisplayName, Role)
                VALUES (@Id, @OrganizationId, @FirebaseUid, @Email, @DisplayName, @Role)
                """,
                new { Id = userId, OrganizationId = organizationId, FirebaseUid = firebaseUid, Email = email, DisplayName = displayName, Role = role });
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync($"DROP SCHEMA IF EXISTS {_schema} CASCADE");
        }

        private async Task CreateSchemaAsync()
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                """
                CREATE TABLE Organizations (
                    Id UUID PRIMARY KEY,
                    Name VARCHAR(255) NOT NULL,
                    PlanType VARCHAR(50) NOT NULL DEFAULT 'Trial',
                    TrialEndsAt TIMESTAMP WITH TIME ZONE DEFAULT (CURRENT_TIMESTAMP + INTERVAL '7 days'),
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE Users (
                    Id UUID PRIMARY KEY,
                    OrganizationId UUID REFERENCES Organizations(Id),
                    FirebaseUid VARCHAR(128) UNIQUE NOT NULL,
                    Email VARCHAR(255) UNIQUE NOT NULL,
                    DisplayName VARCHAR(255),
                    Role VARCHAR(50) DEFAULT 'Viewer',
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE SsoConnections (
                    Id UUID PRIMARY KEY,
                    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                    Provider VARCHAR(50) NOT NULL DEFAULT 'OIDC',
                    Domain VARCHAR(255) NOT NULL DEFAULT '',
                    MetadataUrl TEXT NOT NULL DEFAULT '',
                    EntityId TEXT NOT NULL DEFAULT '',
                    ScimEnabled BOOLEAN NOT NULL DEFAULT FALSE,
                    ScimTokenHash VARCHAR(255) NOT NULL DEFAULT '',
                    IsEnabled BOOLEAN NOT NULL DEFAULT FALSE,
                    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE (OrganizationId, Provider, Domain)
                );
                """);
        }

        private static string HashToken(string token)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hashBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private sealed class ConnectionFactory : IDbConnectionFactory
        {
            private readonly string _connectionString;

            public ConnectionFactory(string connectionString)
            {
                _connectionString = connectionString;
            }

            public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
        }

        private sealed class NoopAuditLogService : IAuditLogService
        {
            public Task RecordAsync(
                string action,
                string category,
                string outcome,
                Guid? organizationId = null,
                Guid? actorUserId = null,
                string actorEmail = "",
                string actorType = "User",
                string targetType = "",
                string targetId = "",
                string metadataJson = "{}",
                string ipAddress = "",
                string userAgent = "",
                CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }
    }
}
