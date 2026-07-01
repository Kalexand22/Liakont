namespace Liakont.Modules.Reference.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Reference.Infrastructure;
using Xunit;

/// <summary>
/// Entrées du journal append-only (ADR-0038 §5) : la nature (Create/Update/Remove) et les valeurs avant/après
/// sérialisées sont dérivées correctement de l'état avant/après. L'auteur est propagé tel quel.
/// </summary>
public sealed class CountryAliasChangeLogFactoryTests
{
    [Fact]
    public void ForUpsert_without_a_previous_value_is_a_Create_with_no_before()
    {
        var operatorId = Guid.NewGuid();

        var entry = CountryAliasChangeLogFactory.ForUpsert("BEL", beforeIsoCode: null, afterIsoCode: "BE", operatorId, "Alice");

        entry.ChangeType.Should().Be(CountryAliasChangeType.Create);
        entry.SourceCode.Should().Be("BEL");
        entry.BeforeJson.Should().BeNull();
        entry.AfterJson.Should().Contain("BE");
        entry.OperatorId.Should().Be(operatorId);
        entry.OperatorName.Should().Be("Alice");
    }

    [Fact]
    public void ForUpsert_with_a_previous_value_is_an_Update_carrying_both_sides()
    {
        var entry = CountryAliasChangeLogFactory.ForUpsert("BEL", beforeIsoCode: "BE", afterIsoCode: "FR", Guid.NewGuid(), "Bob");

        entry.ChangeType.Should().Be(CountryAliasChangeType.Update);
        entry.BeforeJson.Should().Contain("BE");
        entry.AfterJson.Should().Contain("FR");
    }

    [Fact]
    public void ForRemove_is_a_Remove_with_a_before_and_no_after()
    {
        var entry = CountryAliasChangeLogFactory.ForRemove("ENG", "GB", Guid.NewGuid(), operatorName: null);

        entry.ChangeType.Should().Be(CountryAliasChangeType.Remove);
        entry.BeforeJson.Should().Contain("GB");
        entry.AfterJson.Should().BeNull();
        entry.OperatorName.Should().BeNull();
    }
}
