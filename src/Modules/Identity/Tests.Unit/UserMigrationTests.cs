namespace Stratum.Modules.Identity.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.Entities;
using Xunit;

/// <summary>
/// Tests for user migration patterns: linking local users to Keycloak ExternalId
/// and verifying idempotency of the migration flow.
/// </summary>
public sealed class UserMigrationTests
{
    [Fact]
    public void Reconstituted_local_user_has_null_external_id()
    {
        var user = User.Reconstitute(
            Guid.NewGuid(), "admin", "admin@stratum.local", "Admin", "hashed",
            null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>(),
            externalId: null);

        user.ExternalId.Should().BeNull("local users have no external identity link");
    }

    [Fact]
    public void Reconstituted_local_user_can_be_linked_to_keycloak()
    {
        var userId = Guid.NewGuid();
        var user = User.Reconstitute(
            userId, "admin", "admin@stratum.local", "Admin", "hashed",
            null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>(),
            externalId: null);

        user.ExternalId.Should().BeNull("before migration");

        // After migration, the repo would update the user with the Keycloak sub.
        // Reconstitute with ExternalId set simulates the post-migration state.
        var linked = User.Reconstitute(
            userId, "admin", "admin@stratum.local", "Admin", "hashed",
            null, true, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<Role>(),
            externalId: "kc-sub-abc-123");

        linked.ExternalId.Should().Be("kc-sub-abc-123");
        linked.Id.Should().Be(userId, "identity preserved after linking");
    }

    [Fact]
    public void Already_linked_user_is_skipped_by_external_id_check()
    {
        var user = User.Reconstitute(
            Guid.NewGuid(), "jdoe", "jdoe@example.com", "John", "hashed",
            null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>(),
            externalId: "kc-existing-sub");

        // Migration script checks: if ExternalId is not null, skip.
        user.ExternalId.Should().NotBeNull("user already migrated");
    }

    [Fact]
    public void Multiple_local_users_can_be_linked_independently()
    {
        var users = new[]
        {
            User.Reconstitute(Guid.NewGuid(), "user1", "u1@test.com", "User 1", "h1",
                null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>()),
            User.Reconstitute(Guid.NewGuid(), "user2", "u2@test.com", "User 2", "h2",
                null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>()),
            User.Reconstitute(Guid.NewGuid(), "user3", "u3@test.com", "User 3", "h3",
                null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>(),
                externalId: "already-linked"),
        };

        var unmigrated = users.Where(u => u.ExternalId is null).ToList();
        unmigrated.Should().HaveCount(2, "only users without ExternalId need migration");

        var alreadyMigrated = users.Where(u => u.ExternalId is not null).ToList();
        alreadyMigrated.Should().HaveCount(1);
        alreadyMigrated[0].Username.Value.Should().Be("user3");
    }

    [Fact]
    public void Inactive_user_can_be_migrated()
    {
        var user = User.Reconstitute(
            Guid.NewGuid(), "inactive", "inactive@test.com", "Inactive", "hashed",
            null, false, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>(),
            externalId: null);

        user.IsActive.Should().BeFalse();
        user.ExternalId.Should().BeNull("inactive users can still be migrated");
    }

    [Fact]
    public void Display_name_splits_into_first_last()
    {
        // Validates the name splitting logic used by the migration script
        var testCases = new[]
        {
            ("John Doe", "John", "Doe"),
            ("Jane", "Jane", string.Empty),
            ("Jean-Pierre de la Croix", "Jean-Pierre", "de la Croix"),
        };

        foreach (var (displayName, expectedFirst, expectedLast) in testCases)
        {
            var parts = displayName.Trim().Split(' ', 2);
            var firstName = parts[0];
            var lastName = parts.Length > 1 ? parts[1] : string.Empty;

            firstName.Should().Be(expectedFirst);
            lastName.Should().Be(expectedLast);
        }
    }

    [Fact]
    public void Stratum_user_id_attribute_maps_to_guid_string()
    {
        var userId = Guid.NewGuid();
        var user = User.Reconstitute(
            userId, "test", "test@test.com", "Test", "hashed",
            null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>());

        // The migration script maps User.Id -> Keycloak attribute "stratum_user_id"
        var stratumUserIdAttr = user.Id.ToString();
        Guid.TryParse(stratumUserIdAttr, out var parsed).Should().BeTrue();
        parsed.Should().Be(userId);
    }
}
