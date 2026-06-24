using System;
using System.IO;
using Npgsql;

class Program
{
    static void Main()
    {
        var connStr = "Host=localhost;Database=citationly_db;Username=postgres;Password=postgres";
        var sqlFile = @"e:\IowebReact\citationly.backend\Citationly.Infrastructure\Database\init.sql";
        
        var sql = File.ReadAllText(sqlFile);
        
        using var conn = new NpgsqlConnection(connStr);
        conn.Open();
        
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
        
        Console.WriteLine("Database initialized successfully!");
    }
}
