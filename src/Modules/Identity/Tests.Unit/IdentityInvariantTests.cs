namespace Stratum.Modules.Identity.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.Entities;
using Xunit;

/// <summary>
/// Unit tests for domain-level invariants.
/// INV-IDENTITY-003: No duplicate role on a user (User.AssignRole).
/// INV-IDENTITY-004: System roles cannot be renamed (Role.Rename).
/// INV-IDENTITY-007: Username format (Username value object).
/// Authentication is now delegated to Keycloak — password invariants removed.
/// </summary>
public sealed class IdentityInvariantTests
{
    private static User CreateTestUser(string username = "alice", string email = "alice@example.com")
        => User.CreateFromOidc($"kc-{username}", username, email, username);

    // ── INV-IDENTITY-003 ──────────────────────────────────────────────────

    [Fact]
    public void AssignRoleShouldThrowInv003WhenRoleAlreadyAssigned()
    {
        var user = CreateTestUser("bob", "bob@example.com");
        var role = Role.Create("Admin", null);
        user.AssignRole(role);

        var act = () => user.AssignRole(role);

        act.Should().Throw<InvalidOperationException>().WithMessage("*INV-IDENTITY-003*");
    }

    [Fact]
    public void AssignRoleShouldSucceedWithDifferentRoles()
    {
        var user = CreateTestUser("carol", "carol@example.com");
        var admin = Role.Create("Admin", null);
        var manager = Role.Create("Manager", null);

        user.AssignRole(admin);
        user.AssignRole(manager);

        user.Roles.Should().HaveCount(2);
    }

    [Fact]
    public void RevokeRoleShouldRemoveRole()
    {
        var user = CreateTestUser("dave", "dave@example.com");
        var role = Role.Create("Admin", null);
        user.AssignRole(role);

        user.RevokeRole(role.Id);

        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public void RevokeRoleShouldThrowWhenRoleNotPresent()
    {
        var user = CreateTestUser("eve", "eve@example.com");

        var act = () => user.RevokeRole(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    // ── INV-IDENTITY-004 ──────────────────────────────────────────────────

    [Fact]
    public void RenameRoleShouldThrowInv004ForSystemRole()
    {
        var role = Role.Create("System", null, isSystem: true);

        var act = () => role.Rename("NewName");

        act.Should().Throw<InvalidOperationException>().WithMessage("*INV-IDENTITY-004*");
    }

    [Fact]
    public void RenameRoleShouldSucceedForNonSystemRole()
    {
        var role = Role.Create("Manager", null, isSystem: false);

        role.Rename("SeniorManager");

        role.Name.Should().Be("SeniorManager");
    }

    [Fact]
    public void RenameRoleShouldThrowWhenNewNameIsEmpty()
    {
        var role = Role.Create("Manager", null, isSystem: false);

        var act = () => role.Rename(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // ── User behaviour ────────────────────────────────────────────────────

    [Fact]
    public void DeactivateShouldSetIsActiveFalse()
    {
        var user = CreateTestUser("frank", "frank@example.com");

        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void RecordLoginShouldUpdateLastLoginAt()
    {
        var user = CreateTestUser("grace", "grace@example.com");

        user.RecordLogin();

        user.LastLoginAt.Should().NotBeNull();
        user.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void CreateFromOidcShouldRequireExternalId()
    {
        var act = () => User.CreateFromOidc(string.Empty, "user1", "user1@example.com", null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateFromOidcShouldSetExternalIdAndEmptyPasswordHash()
    {
        var user = User.CreateFromOidc("kc-sub-123", "alice", "alice@example.com", "Alice");

        user.ExternalId.Should().Be("kc-sub-123");
        user.PasswordHash.Should().BeEmpty();
        user.IsActive.Should().BeTrue();
    }
}
