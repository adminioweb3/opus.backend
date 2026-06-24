using System.Data;

namespace Citationly.Application.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
