namespace Stratum.Modules.Identity.Tests.Integration;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Infrastructure.Queries;
using Stratum.Modules.Identity.Infrastructure.Repositories;
using Stratum.Modules.Identity.Tests.Integration.Fixtures;
using Xunit;

[Collection("Identity")]
public sealed class PostgresIdentityQueriesTests
{
    private readonly PostgresUserRepository _userRepo;

    private readonly PostgresRoleRepository _roleRepo;

    private readonly PostgresGrantRepository _grantRepo;

    private readonly PostgresIdentityQueries _queries;

    public PostgresIdentityQueriesTests(IdentityDatabaseFixture fixture)
    {
        var connFactory = fixture.CreateConnectionFactory();
        _userRepo = new PostgresUserRepository(connFactory);
        _roleRepo = new PostgresRoleRepository(connFactory);
        _grantRepo = new PostgresGrantRepository(connFactory);
        _queries = new PostgresIdentityQueries(connFactory);
    }

    [Fact]
    public async Task GetUserByIdShouldReturnDtoForExistingUser()
    {
        var user = User.CreateFromOidc("kc-qry_byid", "qry_byid", "qry_byid@example.com", "Query ById");
        await _userRepo.Insert(user);

        var dto = await _queries.GetUserById(user.Id);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(user.Id);
        dto.Username.Should().Be("qry_byid");
        dto.Email.Should().Be("qry_byid@example.com");
        dto.IsActive.Should().BeTrue();
        dto.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserByIdShouldReturnNullForNonExistentUser()
    {
        var dto = await _queries.GetUserById(Guid.NewGuid());

        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByUsernameShouldReturnDtoForExistingUser()
    {
        var user = User.CreateFromOidc("kc-qry_byuname", "qry_byuname", "qry_byuname@example.com", null);
        await _userRepo.Insert(user);

        var dto = await _queries.GetUserByUsername("qry_byuname");

        dto.Should().NotBeNull();
        dto!.Username.Should().Be("qry_byuname");
    }

    [Fact]
    public async Task GetUserByUsernameShouldReturnNullForNonExistentUsername()
    {
        var dto = await _queries.GetUserByUsername("nobody_xyz987_qry");

        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetRolesShouldIncludeInsertedRole()
    {
        var uniqueName = $"QryRole_{Guid.NewGuid():N}";
        var role = Role.Create(uniqueName, "A query test role");
        await _roleRepo.Insert(role);

        var roles = await _queries.GetRoles();

        roles.Should().Contain(r => r.Name == uniqueName && r.Description == "A query test role");
    }

    [Fact]
    public async Task GetUserPermissionsShouldReturnGrantedPermissions()
    {
        var role = Role.Create("PermRole_qry", null);
        await _roleRepo.Insert(role);
        await _grantRepo.Insert(Grant.Create(role.Id, "sales.quotes.read", "sales"));
        await _grantRepo.Insert(Grant.Create(role.Id, "sales.quotes.write", "sales"));

        var user = User.CreateFromOidc("kc-qry_perms", "qry_perms", "qry_perms@example.com", null);
        user.AssignRole(role);
        await _userRepo.Insert(user);
        await _userRepo.Update(user);

        var permissions = await _queries.GetUserPermissions(user.Id);

        permissions.Should().Contain("sales.quotes.read");
        permissions.Should().Contain("sales.quotes.write");
    }

    [Fact]
    public async Task UserHasPermissionShouldReturnTrueForGrantedPermission()
    {
        var role = Role.Create("HasPermRole_qry", null);
        await _roleRepo.Insert(role);
        await _grantRepo.Insert(Grant.Create(role.Id, "identity.admin", "identity"));

        var user = User.CreateFromOidc("kc-qry_hasperm", "qry_hasperm", "qry_hasperm@example.com", null);
        user.AssignRole(role);
        await _userRepo.Insert(user);
        await _userRepo.Update(user);

        var result = await _queries.UserHasPermission(user.Id, "identity.admin");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UserHasPermissionShouldReturnFalseForNotGrantedPermission()
    {
        var user = User.CreateFromOidc("kc-qry_noperm", "qry_noperm", "qry_noperm@example.com", null);
        await _userRepo.Insert(user);

        var result = await _queries.UserHasPermission(user.Id, "some.ungranted.permission");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserByIdShouldIncludeRoleNames()
    {
        var role = Role.Create("QryUserRole_dt", null);
        await _roleRepo.Insert(role);

        var user = User.CreateFromOidc("kc-qry_withrole", "qry_withrole", "qry_withrole@example.com", null);
        user.AssignRole(role);
        await _userRepo.Insert(user);
        await _userRepo.Update(user);

        var dto = await _queries.GetUserById(user.Id);

        dto!.Roles.Should().Contain("QryUserRole_dt");
    }

    [Fact]
    public async Task GetUserGrantsForPermissionShouldReturnGrantWithCondition()
    {
        var role = Role.Create("CondGrantRole_qry", null);
        await _roleRepo.Insert(role);
        await _grantRepo.Insert(Grant.Create(role.Id, "party.update", "party", "record.company_id == actor.company_id"));

        var user = User.CreateFromOidc("kc-qry_condgrant", "qry_condgrant", "qry_condgrant@example.com", null);
        user.AssignRole(role);
        await _userRepo.Insert(user);
        await _userRepo.Update(user);

        var grants = await _queries.GetUserGrantsForPermission(user.Id, "party.update");

        grants.Should().ContainSingle();
        grants[0].Permission.Should().Be("party.update");
        grants[0].Condition.Should().Be("record.company_id == actor.company_id");
    }

    [Fact]
    public async Task GetUserGrantsForPermissionShouldReturnGrantWithNullCondition()
    {
        var role = Role.Create("NoCondGrantRole_qry", null);
        await _roleRepo.Insert(role);
        await _grantRepo.Insert(Grant.Create(role.Id, "party.read.nocond", "party"));

        var user = User.CreateFromOidc("kc-qry_nocondgrant", "qry_nocondgrant", "qry_nocondgrant@example.com", null);
        user.AssignRole(role);
        await _userRepo.Insert(user);
        await _userRepo.Update(user);

        var grants = await _queries.GetUserGrantsForPermission(user.Id, "party.read.nocond");

        grants.Should().ContainSingle();
        grants[0].Condition.Should().BeNull();
    }
}
