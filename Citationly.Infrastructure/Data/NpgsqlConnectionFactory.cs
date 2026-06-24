using System.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Data;

public class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new ArgumentNullException("DefaultConnection string is missing.");
    }

    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }
}
