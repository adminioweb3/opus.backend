using System;
using System.IO;
using Npgsql;

class Program
{
    static void Main()
    {
        var connStr = "Host=localhost;Database=opus_db;Username=postgres;Password=postgres";
        var sqlFile = @"e:\IowebReact\opus.backend\Opus.Infrastructure\Database\init.sql";
        
        var sql = File.ReadAllText(sqlFile);
        
        using var conn = new NpgsqlConnection(connStr);
        conn.Open();
        
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
        
        Console.WriteLine("Database initialized successfully!");
    }
}
