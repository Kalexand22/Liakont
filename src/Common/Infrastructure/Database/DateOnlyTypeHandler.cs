namespace Stratum.Common.Infrastructure.Database;

using System.Data;
using Dapper;

/// <summary>
/// Dapper type handler for <see cref="DateOnly"/> ↔ PostgreSQL <c>date</c> mapping.
/// Converts DateOnly to DateTime for parameter binding and back for result reading.
/// </summary>
internal sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value)
    {
        return value switch
        {
            DateTime dt => DateOnly.FromDateTime(dt),
            DateTimeOffset dto => DateOnly.FromDateTime(dto.DateTime),
            _ => throw new InvalidOperationException($"Cannot convert {value.GetType()} to DateOnly."),
        };
    }
}
