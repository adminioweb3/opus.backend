using System.Data;
using Dapper;

namespace Citationly.Infrastructure.Data;

// Dapper 2.1.79 (the version pinned across this project) reads Postgres `date` columns into
// DateOnly fine via the underlying ADO reader, but has no built-in support for writing a
// DateOnly value as a query parameter — SqlMapper.LookupDbType has no case for it and throws
// NotSupportedException on every INSERT/UPDATE that binds a DateOnly property (e.g. every
// "ScanDate" column across the snapshot/scan-summary tables). Registering explicit handlers
// once at startup (see Program.cs) fixes every repository at once instead of patching each one.
public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => DateOnly.FromDateTime(Convert.ToDateTime(value)),
    };
}

public class NullableDateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly? value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
    }

    public override DateOnly? Parse(object value) => value switch
    {
        null or DBNull => null,
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => DateOnly.FromDateTime(Convert.ToDateTime(value)),
    };
}
