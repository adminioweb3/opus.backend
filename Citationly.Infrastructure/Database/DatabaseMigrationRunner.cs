using Citationly.Application.Interfaces;
using Dapper;

namespace Citationly.Infrastructure.Database;

public sealed class DatabaseMigrationRunner
{
    private const long MigrationLockKey = 78219360420501;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public DatabaseMigrationRunner(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<IReadOnlyList<AppliedDatabaseMigration>> RunPendingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();

        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Id VARCHAR(150) PRIMARY KEY,
                Description TEXT NOT NULL,
                AppliedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_schemamigrations_applied ON SchemaMigrations (AppliedAt DESC);
            """);

        await connection.ExecuteAsync("SELECT pg_advisory_lock(@LockKey);", new { LockKey = MigrationLockKey });
        try
        {
            var applied = new List<AppliedDatabaseMigration>();
            foreach (var migration in DatabaseMigrations.All)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var alreadyApplied = await connection.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS (SELECT 1 FROM SchemaMigrations WHERE Id = @Id)",
                    new { migration.Id });
                if (alreadyApplied)
                {
                    continue;
                }

                using var transaction = connection.BeginTransaction();
                try
                {
                    await connection.ExecuteAsync(migration.Sql, transaction: transaction);
                    await connection.ExecuteAsync(
                        """
                        INSERT INTO SchemaMigrations (Id, Description)
                        VALUES (@Id, @Description)
                        """,
                        new { migration.Id, migration.Description },
                        transaction);

                    transaction.Commit();
                    applied.Add(new AppliedDatabaseMigration(migration.Id, migration.Description));
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            return applied;
        }
        finally
        {
            await connection.ExecuteAsync("SELECT pg_advisory_unlock(@LockKey);", new { LockKey = MigrationLockKey });
        }
    }
}

public sealed record AppliedDatabaseMigration(string Id, string Description);

internal sealed record DatabaseMigration(string Id, string Description, string Sql);

internal static class DatabaseMigrations
{
    public static readonly IReadOnlyList<DatabaseMigration> All =
    [
        new("202608260001_self_healing_baseline", "Apply current idempotent production schema baseline", SelfHealingMigrations.Sql)
    ];
}
