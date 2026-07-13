using System.Reflection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Citationly.Infrastructure.Database;

// Runs the embedded, idempotent schema-init.sql (CREATE ... IF NOT EXISTS / CREATE OR REPLACE
// only — never destructive) against the configured database on every API startup. This is
// what lets a brand-new production database self-create its schema on first boot instead of
// requiring someone to manually run a migration tool by hand; it's a no-op against a database
// that already has the schema.
public static class SchemaMigrator
{
    public static async Task RunAsync(string connectionString, ILogger logger)
    {
        string sql;
        var assembly = typeof(SchemaMigrator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("schema-init.sql", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            logger.LogError("Embedded schema-init.sql resource not found — skipping schema bootstrap.");
            return;
        }

        await using (var stream = assembly.GetManifestResourceStream(resourceName)!)
        using (var reader = new StreamReader(stream))
        {
            sql = await reader.ReadToEndAsync();
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();

            logger.LogInformation("Database schema bootstrap completed successfully.");
        }
        catch (Exception ex)
        {
            // Log and continue rather than crashing the whole process — a failed bootstrap
            // (e.g. bad connection string) should surface as visible request-level errors
            // via the global exception handler, not silently kill startup.
            logger.LogError(ex, "Database schema bootstrap failed. The API will start, but requests that touch the database will likely fail until this is resolved.");
        }
    }
}
