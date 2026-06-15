namespace Liakont.Agent.Core.Tests.Extraction;

using System;
using FluentAssertions;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Normalisation des paramètres de requête (<see cref="SourceQuery"/>) : les bornes <see cref="DateTime"/>
/// sont tronquées à la seconde avant liaison ODBC (évite le débordement ODBC 22008 du pilote SQL Server
/// sur un DateTime .NET à précision 100 ns) ; les autres valeurs passent inchangées.
/// </summary>
public class SourceQueryTests
{
    [Fact]
    public void Normalize_parameter_truncates_datetime_to_seconds()
    {
        DateTime withSubSecond = new DateTime(2026, 6, 14, 20, 50, 1, DateTimeKind.Utc).AddTicks(1234567);

        object result = SourceQuery.NormalizeParameterValue(withSubSecond);

        result.Should().BeOfType<DateTime>();
        ((DateTime)result).Should().Be(new DateTime(2026, 6, 14, 20, 50, 1, DateTimeKind.Utc));
    }

    [Fact]
    public void Normalize_parameter_passes_through_non_datetime_values()
    {
        SourceQuery.NormalizeParameterValue("texte").Should().Be("texte");
        SourceQuery.NormalizeParameterValue(42).Should().Be(42);
    }
}
