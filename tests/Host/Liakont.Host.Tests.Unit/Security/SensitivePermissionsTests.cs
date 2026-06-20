namespace Liakont.Host.Tests.Unit.Security;

using FluentAssertions;
using Liakont.Host.Security;
using Xunit;

/// <summary>
/// Vérifie que l'ensemble des permissions sensibles (<see cref="SensitivePermissions"/>) est EXACTEMENT
/// {<c>liakont.actions</c>, <c>liakont.settings</c>} — la source autoritative étant ADR-0017 §Négatif +
/// RDF10. Aucune permission inventée : <c>read</c>, <c>supervision</c>, <c>fleet</c> ne sont pas sensibles.
/// </summary>
public sealed class SensitivePermissionsTests
{
    [Theory]
    [InlineData(LiakontPermissions.Actions, true)]
    [InlineData(LiakontPermissions.Settings, true)]
    [InlineData(LiakontPermissions.Read, false)]
    [InlineData(LiakontPermissions.Supervision, false)]
    [InlineData(LiakontPermissions.Fleet, false)]
    public void IsSensitive_Matches_The_Authoritative_Set(string permission, bool expected)
    {
        SensitivePermissions.IsSensitive(permission).Should().Be(expected);
    }

    [Fact]
    public void IsSensitive_Is_Case_Insensitive()
    {
        SensitivePermissions.IsSensitive("LIAKONT.ACTIONS").Should().BeTrue();
    }

    [Fact]
    public void All_Contains_Exactly_The_Two_Sensitive_Permissions()
    {
        SensitivePermissions.All.Should().BeEquivalentTo(
            new[] { LiakontPermissions.Actions, LiakontPermissions.Settings });
    }
}
