namespace Liakont.Modules.Ged.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Domain.Catalog;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lecture Dapper du catalogue d'axes GED (<c>ged_catalog.axis_definitions</c> + <c>axis_values</c>), résolue sur
/// la base DU TENANT (isolation = la connexion, F19 §3.2/§3.8). Les colonnes <c>snake_case</c> sont ALIASÉES vers
/// les propriétés (Dapper n'active pas <c>MatchNamesWithUnderscores</c> dans ce dépôt — sans alias la propriété
/// resterait silencieusement vide).
/// </summary>
internal sealed class PostgresAxisCatalog : IAxisCatalog
{
    private const string ResolveSql = """
        SELECT id            AS Id,
               code          AS Code,
               data_type     AS DataType,
               value_scale   AS ValueScale,
               is_multi_value AS IsMultiValue,
               is_active     AS IsActive
        FROM ged_catalog.axis_definitions
        WHERE code = @Code
        """;

    // Vocabulaire ACTIF d'un axe enum (ordre déclaré), pour la validation d'appartenance (règle 2).
    private const string EnumValuesSql = """
        SELECT code
        FROM ged_catalog.axis_values
        WHERE axis_id = @AxisId AND is_active = true
        ORDER BY ordinal, code
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresAxisCatalog(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AxisDefinition?> ResolveAsync(string axisCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisCode);

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<AxisRow>(new CommandDefinition(
            ResolveSql,
            new { Code = axisCode },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var dataType = AxisDataTypes.Parse(row.DataType);

        IReadOnlyList<string> allowedEnumValues = Array.Empty<string>();
        if (dataType == AxisDataType.Enum)
        {
            var codes = await connection.QueryAsync<string>(new CommandDefinition(
                EnumValuesSql,
                new { AxisId = row.Id },
                cancellationToken: cancellationToken));
            allowedEnumValues = codes.ToList();
        }

        return new AxisDefinition
        {
            Id = row.Id,
            Code = row.Code,
            DataType = dataType,
            ValueScale = row.ValueScale,
            IsMultiValue = row.IsMultiValue,
            IsActive = row.IsActive,
            AllowedEnumValues = allowedEnumValues,
        };
    }

    private sealed class AxisRow
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string DataType { get; set; } = string.Empty;

        public int? ValueScale { get; set; }

        public bool IsMultiValue { get; set; }

        public bool IsActive { get; set; }
    }
}
