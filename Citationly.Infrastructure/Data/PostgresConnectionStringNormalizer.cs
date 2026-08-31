using System.Text;

namespace Citationly.Infrastructure.Data;

public static class PostgresConnectionStringNormalizer
{
    public static string Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        var trimmed = connectionString.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(trimmed);
                var userInfo = uri.UserInfo.Split(':');
                var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
                var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
                var database = uri.AbsolutePath.TrimStart('/');
                var port = uri.Port > 0 ? uri.Port : 5432;
                var host = uri.Host;

                var builder = new StringBuilder();
                builder.Append($"Host={host};Port={port};Database={database};");
                if (!string.IsNullOrEmpty(username))
                {
                    builder.Append($"Username={username};");
                }
                if (!string.IsNullOrEmpty(password))
                {
                    builder.Append($"Password={password};");
                }
                builder.Append("SSL Mode=Prefer;Trust Server Certificate=true;");
                return builder.ToString();
            }
            catch
            {
                return trimmed;
            }
        }

        return trimmed;
    }
}
