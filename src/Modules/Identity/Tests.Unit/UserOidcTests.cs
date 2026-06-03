namespace Stratum.Modules.Identity.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.Entities;
using Xunit;

public sealed class UserOidcTests
{
    [Fact]
    public void CreateFromOidc_sets_external_id_and_no_password()
    {
        var user = User.CreateFromOidc("ext-123", "jdoe", "jdoe@example.com", "John Doe");

        user.ExternalId.Should().Be("ext-123");
        user.Username.Value.Should().Be("jdoe");
        user.Email.Value.Should().Be("jdoe@example.com");
        user.DisplayName.Should().Be("John Doe");
        user.PasswordHash.Should().Be(string.Empty, "OIDC users have no local password");
        user.IsActive.Should().BeTrue();
        user.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public void CreateFromOidc_throws_on_empty_external_id()
    {
        var act = () => User.CreateFromOidc(string.Empty, "jdoe", "jdoe@example.com", null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateFromOidc_returns_true_when_email_changes()
    {
        var user = User.CreateFromOidc("ext-1", "jdoe", "old@example.com", "John");

        var changed = user.UpdateFromOidc("new@example.com", "John");

        changed.Should().BeTrue();
        user.Email.Value.Should().Be("new@example.com");
        user.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateFromOidc_returns_true_when_display_name_changes()
    {
        var user = User.CreateFromOidc("ext-1", "jdoe", "jdoe@example.com", "John");

        var changed = user.UpdateFromOidc("jdoe@example.com", "Jane");

        changed.Should().BeTrue();
        user.DisplayName.Should().Be("Jane");
    }

    [Fact]
    public void UpdateFromOidc_returns_false_when_nothing_changes()
    {
        var user = User.CreateFromOidc("ext-1", "jdoe", "jdoe@example.com", "John");

        var changed = user.UpdateFromOidc("jdoe@example.com", "John");

        changed.Should().BeFalse();
    }

    [Fact]
    public void Reconstitute_preserves_external_id()
    {
        var user = User.Reconstitute(
            Guid.NewGuid(), "jdoe", "jdoe@example.com", "John", string.Empty,
            null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>(),
            externalId: "ext-456");

        user.ExternalId.Should().Be("ext-456");
    }
}
