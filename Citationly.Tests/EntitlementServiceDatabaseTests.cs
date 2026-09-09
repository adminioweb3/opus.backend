using System.Data;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Services;
using Dapper;
using Npgsql;
using Xunit;

namespace Citationly.Tests;

public class EntitlementServiceDatabaseTests
{
    [Fact]
    public async Task TryConsumeUsageAsync_DoesNotExceedPlanLimit_UnderConcurrentReservations()
    {
        await using var database = await EntitlementTestDatabase.TryCreateAsync();
        if (database == null) return;

        var orgId = Guid.NewGuid();
        await database.SeedPlanAsync(orgId, planKey: "Trial", metricKey: "ai_calls_per_day", limit: 5);

        var service = new EntitlementService(new ConnectionFactory(database.SchemaConnectionString));
        var attempts = Enumerable.Range(0, 25)
            .Select(_ => service.TryConsumeUsageAsync(orgId, "ai_calls_per_day"))
            .ToArray();

        var results = await Task.WhenAll(attempts);
        var accepted = results.Count(r => r.IsWithinLimit);

        await using var connection = database.CreateOpenConnection();
        var storedCount = await connection.ExecuteScalarAsync<long>(
            "SELECT Count FROM UsageCounters WHERE OrganizationId = @OrgId AND MetricKey = 'ai_calls_per_day'",
            new { OrgId = orgId });

        Assert.Equal(5, accepted);
        Assert.Equal(5, storedCount);
        Assert.All(results.Where(r => !r.IsWithinLimit), r => Assert.Equal(5, r.CurrentUsage));
    }

    private sealed class EntitlementTestDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;

        private EntitlementTestDatabase(string adminConnectionString, string schema, string schemaConnectionString)
        {
            _adminConnectionString = adminConnectionString;
            _schema = schema;
            SchemaConnectionString = schemaConnectionString;
        }

        public string SchemaConnectionString { get; }

        public static async Task<EntitlementTestDatabase?> TryCreateAsync()
        {
            var rawConnectionString = Environment.GetEnvironmentVariable("CITATIONLY_TEST_DATABASE")
                ?? "Host=localhost;Database=opus_db;Username=postgres;Password=postgres";
            var schema = "phase1_quota_" + Guid.NewGuid().ToString("N");

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

                var database = new EntitlementTestDatabase(
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
            var connection = new NpgsqlConnection(SchemaConnectionString);
            connection.Open();
            return connection;
        }

        public async Task SeedPlanAsync(Guid organizationId, string planKey, string metricKey, long limit)
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                """
                INSERT INTO Organizations (Id, Name, PlanType) VALUES (@OrganizationId, 'Quota Test Org', @PlanKey);
                INSERT INTO PlanLimits (PlanKey, FeatureKey, LimitValue) VALUES (@PlanKey, @MetricKey, @Limit);
                """,
                new { OrganizationId = organizationId, PlanKey = planKey, MetricKey = metricKey, Limit = limit });
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
                    PlanType VARCHAR(50) NOT NULL DEFAULT 'Trial'
                );

                CREATE TABLE Subscriptions (
                    Id UUID PRIMARY KEY,
                    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                    PlanKey VARCHAR(50) NOT NULL,
                    Status VARCHAR(50) NOT NULL DEFAULT 'inactive',
                    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE PlanLimits (
                    PlanKey VARCHAR(50) NOT NULL,
                    FeatureKey VARCHAR(100) NOT NULL,
                    LimitValue BIGINT NULL,
                    PRIMARY KEY (PlanKey, FeatureKey)
                );

                CREATE TABLE UsageCounters (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                    MetricKey VARCHAR(100) NOT NULL,
                    PeriodStart TIMESTAMP WITH TIME ZONE NOT NULL,
                    PeriodEnd TIMESTAMP WITH TIME ZONE NOT NULL,
                    Count BIGINT NOT NULL DEFAULT 0,
                    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE (OrganizationId, MetricKey, PeriodStart)
                );
                """);
        }
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
}
