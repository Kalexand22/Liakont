namespace Liakont.Agent.Core.Tests.Extraction;

using System;
using FluentAssertions;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Garde de LECTURE SEULE STRICTE (CLAUDE.md n°5) : seules les requêtes SELECT/WITH sont autorisées ;
/// toute requête d'écriture est refusée AVANT exécution (défense en profondeur, en plus du login db_datareader).
/// </summary>
public class SourceQueryGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM t")]
    [InlineData("  select 1")]
    [InlineData("WITH cte AS (SELECT 1) SELECT * FROM cte")]
    public void EnsureSelectOnly_accepts_read_queries(string sql)
    {
        Action act = () => SourceQueryGuard.EnsureSelectOnly(sql);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("UPDATE t SET x = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("DROP TABLE t")]
    public void EnsureSelectOnly_rejects_write_queries(string sql)
    {
        Action act = () => SourceQueryGuard.EnsureSelectOnly(sql);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("LECTURE SEULE");
    }

    [Fact]
    public void EnsureSelectOnly_rejects_empty()
    {
        Action act = () => SourceQueryGuard.EnsureSelectOnly("   ");

        act.Should().Throw<ArgumentException>();
    }
}
