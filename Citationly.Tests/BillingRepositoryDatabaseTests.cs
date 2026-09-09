using System.Data;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Repositories;
using Dapper;
using Npgsql;
using Xunit;

namespace Citationly.Tests;

public class BillingRepositoryDatabaseTests
{
    [Fact]
    public async Task CashfreeWebhookLedger_RejectsCompletedDuplicateDelivery()
    {
        await using var database = await BillingTestDatabase.TryCreateAsync();
        if (database == null) return;

        var repository = new BillingRepository(new ConnectionFactory(database.SchemaConnectionString));
        const string eventId = "cf-event-duplicate";
        const string payloadHash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

        var firstClaimed = await repository.TryBeginCashfreeWebhookEventAsync(eventId, payloadHash, "SUBSCRIPTION_UPDATE");
        await repository.CompleteCashfreeWebhookEventAsync(eventId);
        var duplicateClaimed = await repository.TryBeginCashfreeWebhookEventAsync(eventId, payloadHash, "SUBSCRIPTION_UPDATE");

        await using var connection = database.CreateOpenConnection();
        var row = await connection.QuerySingleAsync<CashfreeWebhookEventRow>(
            """
            SELECT Status, AttemptCount
            FROM CashfreeWebhookEvents
            WHERE CashfreeEventId = @EventId
            """,
            new { EventId = eventId });

        Assert.True(firstClaimed);
        Assert.False(duplicateClaimed);
        Assert.Equal("Completed", row.Status);
        Assert.Equal(1, row.AttemptCount);
    }

    [Fact]
    public async Task CashfreeWebhookReplay_UpdatesSubscriptionOnlyOnce_ForDuplicateEvent()
    {
        await using var database = await BillingTestDatabase.TryCreateAsync();
        if (database == null) return;

        var organizationId = Guid.NewGuid();
        await database.SeedOrganizationAsync(organizationId);

        var repository = new BillingRepository(new ConnectionFactory(database.SchemaConnectionString));
        await repository.UpsertCashfreeSubscriptionAsync(new Subscription
        {
            OrganizationId = organizationId,
            CashfreeSubscriptionId = "sub_duplicate",
            PlanKey = "Pro",
            Status = "PENDING"
        });

        const string eventId = "cf-event-subscription";
        const string payloadHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        if (await repository.TryBeginCashfreeWebhookEventAsync(eventId, payloadHash, "SUBSCRIPTION_UPDATE"))
        {
            var subscription = await repository.GetCashfreeSubscriptionAsync("sub_duplicate");
            Assert.NotNull(subscription);
            subscription!.Status = "ACTIVE";
            await repository.UpsertCashfreeSubscriptionAsync(subscription);
            await repository.SyncOrganizationPlanTypeAsync(organizationId, subscription.PlanKey);
            await repository.CompleteCashfreeWebhookEventAsync(eventId);
        }

        var duplicateClaimed = await repository.TryBeginCashfreeWebhookEventAsync(eventId, payloadHash, "SUBSCRIPTION_UPDATE");
        if (duplicateClaimed)
        {
            var subscription = await repository.GetCashfreeSubscriptionAsync("sub_duplicate");
            Assert.NotNull(subscription);
            subscription!.Status = "CANCELLED";
            await repository.UpsertCashfreeSubscriptionAsync(subscription);
            await repository.SyncOrganizationPlanTypeAsync(organizationId, "Trial");
            await repository.CompleteCashfreeWebhookEventAsync(eventId);
        }

        await using var connection = database.CreateOpenConnection();
        var status = await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM Subscriptions WHERE CashfreeSubscriptionId = 'sub_duplicate'");
        var planType = await connection.ExecuteScalarAsync<string>(
            "SELECT PlanType FROM Organizations WHERE Id = @OrganizationId",
            new { OrganizationId = organizationId });

        Assert.False(duplicateClaimed);
        Assert.Equal("ACTIVE", status);
        Assert.Equal("Pro", planType);
    }

    private sealed class BillingTestDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;

        private BillingTestDatabase(string adminConnectionString, string schema, string schemaConnectionString)
        {
            _adminConnectionString = adminConnectionString;
            _schema = schema;
            SchemaConnectionString = schemaConnectionString;
        }

        public string SchemaConnectionString { get; }

        public static async Task<BillingTestDatabase?> TryCreateAsync()
        {
            var rawConnectionString = Environment.GetEnvironmentVariable("CITATIONLY_TEST_DATABASE")
                ?? "Host=localhost;Database=opus_db;Username=postgres;Password=postgres";
            var schema = "phase3_billing_" + Guid.NewGuid().ToString("N");

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

                var database = new BillingTestDatabase(
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

        public async Task SeedOrganizationAsync(Guid organizationId)
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                "INSERT INTO Organizations (Id, Name, PlanType) VALUES (@OrganizationId, 'Billing Test Org', 'Trial')",
                new { OrganizationId = organizationId });
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
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                    StripeSubscriptionId VARCHAR(255) UNIQUE,
                    CashfreeSubscriptionId VARCHAR(255) UNIQUE,
                    PlanKey VARCHAR(100) NOT NULL,
                    Status VARCHAR(50) NOT NULL DEFAULT 'trialing',
                    CurrentPeriodStart TIMESTAMP WITH TIME ZONE,
                    CurrentPeriodEnd TIMESTAMP WITH TIME ZONE,
                    CancelAtPeriodEnd BOOLEAN NOT NULL DEFAULT FALSE,
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE CashfreeWebhookEvents (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    CashfreeEventId VARCHAR(255) NOT NULL UNIQUE,
                    PayloadHash VARCHAR(64) NOT NULL,
                    EventType VARCHAR(255) NOT NULL,
                    Status VARCHAR(50) NOT NULL,
                    AttemptCount INT NOT NULL DEFAULT 1,
                    ReceivedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    LastAttemptAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CompletedAt TIMESTAMP WITH TIME ZONE,
                    FailureReason VARCHAR(2000)
                );
                """);
        }
    }

    private sealed class CashfreeWebhookEventRow
    {
        public string Status { get; init; } = string.Empty;
        public int AttemptCount { get; init; }
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
