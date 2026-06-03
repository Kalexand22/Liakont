namespace Stratum.Modules.Identity.Tests.Integration;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Infrastructure.Repositories;
using Stratum.Modules.Identity.Tests.Integration.Fixtures;
using Xunit;

[Collection("Identity")]
public sealed class PostgresUserRepositoryTests
{
    private readonly IdentityDatabaseFixture _fixture;

    private readonly PostgresUserRepository _repo;

    private readonly PostgresRoleRepository _roleRepo;

    public PostgresUserRepositoryTests(IdentityDatabaseFixture fixture)
    {
        _fixture = fixture;
        var connFactory = fixture.CreateConnectionFactory();
        _repo = new PostgresUserRepository(connFactory);
        _roleRepo = new PostgresRoleRepository(connFactory);
    }

    [Fact]
    public async Task InsertAndGetByIdShouldRoundTrip()
    {
        var user = User.CreateFromOidc("kc-user_getbyid", "user_getbyid", "getbyid@example.com", "Get ById");

        await _repo.Insert(user);
        var loaded = await _repo.GetById(user.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(user.Id);
        loaded.Username.Value.Should().Be("user_getbyid");
        loaded.Email.Value.Should().Be("getbyid@example.com");
        loaded.DisplayName.Should().Be("Get ById");
        loaded.IsActive.Should().BeTrue();
        loaded.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdShouldReturnNullForNonExistentUser()
    {
        var result = await _repo.GetById(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task InsertAndGetByUsernameShouldRoundTrip()
    {
        var user = User.CreateFromOidc("kc-user_byname", "user_byname", "byname@example.com", null);

        await _repo.Insert(user);
        var loaded = await _repo.GetByUsername("user_byname");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByUsernameShouldReturnNullForNonExistentUsername()
    {
        var result = await _repo.GetByUsername("nobody_here_xyz987");

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateShouldPersistDeactivation()
    {
        var user = User.CreateFromOidc("kc-user_deact", "user_deact", "deact@example.com", null);
        await _repo.Insert(user);

        user.Deactivate();
        await _repo.Update(user);

        var loaded = await _repo.GetById(user.Id);

        loaded!.IsActive.Should().BeFalse();
        loaded.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateShouldPersistRoles()
    {
        var user = User.CreateFromOidc("kc-user_roles", "user_roles", "roles@example.com", null);
        await _repo.Insert(user);

        var role = Role.Create("Operator", "Operator role");
        await _roleRepo.Insert(role);

        user.AssignRole(role);
        await _repo.Update(user);

        var loaded = await _repo.GetById(user.Id);

        loaded!.Roles.Should().ContainSingle(r => r.Name == "Operator");
    }

    [Fact]
    public async Task UpdateShouldReplacePreviousRoles()
    {
        var user = User.CreateFromOidc("kc-user_rolereplace", "user_rolereplace", "rolereplace@example.com", null);
        var role1 = Role.Create("RoleA_rep", null);
        var role2 = Role.Create("RoleB_rep", null);
        await _roleRepo.Insert(role1);
        await _roleRepo.Insert(role2);
        user.AssignRole(role1);
        await _repo.Insert(user);

        user.RevokeRole(role1.Id);
        user.AssignRole(role2);
        await _repo.Update(user);

        var loaded = await _repo.GetById(user.Id);

        loaded!.Roles.Should().ContainSingle(r => r.Name == "RoleB_rep");
        loaded.Roles.Should().NotContain(r => r.Name == "RoleA_rep");
    }
}
