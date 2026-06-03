namespace Stratum.Modules.Identity.Tests.Unit;

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Domain.Repositories;
using Stratum.Modules.Identity.Infrastructure;
using Xunit;

public sealed class UserSyncServiceTests
{
    private const string DefaultSub = "kc-sub-123";
    private const string DefaultEmail = "user@example.com";
    private const string DefaultUsername = "jdoe";
    private const string DefaultDisplayName = "John Doe";

    private readonly FakeUserRepository _userRepo = new();
    private readonly FakeRoleRepository _roleRepo = new();
    private readonly UserSyncService _sut;

    public UserSyncServiceTests()
    {
        // Seed default role
        var userRole = Role.Create("User", "Default user role");
        _roleRepo.SeedRole(userRole);

        _sut = new UserSyncService(
            _userRepo,
            _roleRepo,
            NullLogger<UserSyncService>.Instance,
            Options.Create(new UserSyncOptions { DefaultRoleName = "User" }));
    }

    [Fact]
    public async Task First_login_creates_new_user_with_external_id()
    {
        var principal = BuildPrincipal(DefaultSub, DefaultEmail, DefaultUsername, DefaultDisplayName);

        var userId = await _sut.SyncFromOidcClaimsAsync(principal);

        var user = _userRepo.InsertedUsers.Single();
        user.Id.Should().Be(userId);
        user.ExternalId.Should().Be(DefaultSub);
        user.Email.Value.Should().Be(DefaultEmail);
        user.Username.Value.Should().Be(DefaultUsername);
        user.DisplayName.Should().Be(DefaultDisplayName);
        user.Roles.Should().HaveCount(1);
        user.Roles[0].Name.Should().Be("User");
    }

    [Fact]
    public async Task Subsequent_login_returns_same_user_id()
    {
        var principal = BuildPrincipal(DefaultSub, DefaultEmail, DefaultUsername, DefaultDisplayName);

        var firstId = await _sut.SyncFromOidcClaimsAsync(principal);
        var secondId = await _sut.SyncFromOidcClaimsAsync(principal);

        secondId.Should().Be(firstId);
        _userRepo.InsertedUsers.Should().HaveCount(1, "should not create duplicate user");
    }

    [Fact]
    public async Task Subsequent_login_updates_email_if_changed()
    {
        var principal1 = BuildPrincipal(DefaultSub, DefaultEmail, DefaultUsername, DefaultDisplayName);
        await _sut.SyncFromOidcClaimsAsync(principal1);

        var principal2 = BuildPrincipal(DefaultSub, "new@example.com", DefaultUsername, DefaultDisplayName);
        await _sut.SyncFromOidcClaimsAsync(principal2);

        var user = _userRepo.InsertedUsers.Single();
        user.Email.Value.Should().Be("new@example.com");
        _userRepo.UpdateCount.Should().Be(1);
    }

    [Fact]
    public async Task Subsequent_login_updates_display_name_if_changed()
    {
        var principal1 = BuildPrincipal(DefaultSub, DefaultEmail, DefaultUsername, DefaultDisplayName);
        await _sut.SyncFromOidcClaimsAsync(principal1);

        var principal2 = BuildPrincipal(DefaultSub, DefaultEmail, DefaultUsername, "Jane Smith");
        await _sut.SyncFromOidcClaimsAsync(principal2);

        var user = _userRepo.InsertedUsers.Single();
        user.DisplayName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task Subsequent_login_always_persists_for_last_login_tracking()
    {
        var principal = BuildPrincipal(DefaultSub, DefaultEmail, DefaultUsername, DefaultDisplayName);
        await _sut.SyncFromOidcClaimsAsync(principal);

        await _sut.SyncFromOidcClaimsAsync(principal);

        _userRepo.UpdateCount.Should().Be(1, "always persist to record LastLoginAt");
    }

    [Fact]
    public async Task Username_collision_disambiguates_with_suffix()
    {
        // Pre-seed a local user with the same username
        var localUser = User.Reconstitute(
            Guid.NewGuid(), "jdoe", "local@example.com", "Local User", string.Empty,
            null, true, null, DateTimeOffset.UtcNow, null, Array.Empty<Role>());
        _userRepo.SeedUser(localUser);

        var principal = BuildPrincipal("new-sub-456", DefaultEmail, DefaultUsername, DefaultDisplayName);

        await _sut.SyncFromOidcClaimsAsync(principal);

        var oidcUser = _userRepo.InsertedUsers[^1];
        oidcUser.Username.Value.Should().Be("jdoe_1", "username should be disambiguated");
        oidcUser.ExternalId.Should().Be("new-sub-456");
    }

    [Fact]
    public async Task Throws_when_no_sub_claim()
    {
        var identity = new ClaimsIdentity(
            [new Claim("email", DefaultEmail)],
            "oidc");
        var principal = new ClaimsPrincipal(identity);

        var act = () => _sut.SyncFromOidcClaimsAsync(principal);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sub*");
    }

    [Fact]
    public async Task Throws_when_no_email_claim()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, DefaultSub)],
            "oidc");
        var principal = new ClaimsPrincipal(identity);

        var act = () => _sut.SyncFromOidcClaimsAsync(principal);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*email*");
    }

    [Fact]
    public async Task Uses_email_as_username_when_preferred_username_missing()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, DefaultSub),
                new Claim("email", DefaultEmail),
            ],
            "oidc");
        var principal = new ClaimsPrincipal(identity);

        await _sut.SyncFromOidcClaimsAsync(principal);

        _userRepo.InsertedUsers.Single().Username.Value.Should().Be("user");
    }

    [Fact]
    public async Task Creates_user_without_role_when_default_role_not_found()
    {
        var sut = new UserSyncService(
            _userRepo,
            _roleRepo,
            NullLogger<UserSyncService>.Instance,
            Options.Create(new UserSyncOptions { DefaultRoleName = "NonExistentRole" }));

        var principal = BuildPrincipal(DefaultSub, DefaultEmail, DefaultUsername, DefaultDisplayName);

        await sut.SyncFromOidcClaimsAsync(principal);

        _userRepo.InsertedUsers.Single().Roles.Should().BeEmpty();
    }

    private static ClaimsPrincipal BuildPrincipal(string sub, string email, string username, string? displayName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub),
            new("email", email),
            new("preferred_username", username),
        };

        if (displayName is not null)
        {
            claims.Add(new Claim("display_name", displayName));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users = [];

        public IReadOnlyList<User> InsertedUsers => _users;

        public int UpdateCount { get; private set; }

        public void SeedUser(User user) => _users.Add(user);

        public Task<User?> GetById(Guid id, CancellationToken ct = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByUsername(string username, CancellationToken ct = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Username.Value == username));

        public Task<User?> GetByExternalId(string externalId, CancellationToken ct = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.ExternalId == externalId));

        public Task<User?> GetByEmail(string email, CancellationToken ct = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Email.Value == email));

        public Task Insert(User user, CancellationToken ct = default)
        {
            _users.Add(user);
            return Task.CompletedTask;
        }

        public Task Update(User user, CancellationToken ct = default)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly List<Role> _roles = [];

        public void SeedRole(Role role) => _roles.Add(role);

        public Task<Role?> GetById(Guid id, CancellationToken ct = default)
            => Task.FromResult(_roles.FirstOrDefault(r => r.Id == id));

        public Task<Role?> GetByName(string name, CancellationToken ct = default)
            => Task.FromResult(_roles.FirstOrDefault(r => r.Name == name));

        public Task<IReadOnlyList<Role>> GetAll(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Role>>(_roles);

        public Task Insert(Role role, CancellationToken ct = default)
        {
            _roles.Add(role);
            return Task.CompletedTask;
        }
    }
}
