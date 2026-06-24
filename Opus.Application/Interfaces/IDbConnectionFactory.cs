using System.Data;

namespace Opus.Application.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
