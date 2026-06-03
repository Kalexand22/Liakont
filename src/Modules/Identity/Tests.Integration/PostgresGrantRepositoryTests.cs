namespace Stratum.Modules.Identity.Tests.Integration;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Infrastructure.Repositories;
using Stratum.Modules.Identity.Tests.Integration.Fixtures;
using Xunit;

[Collection("Identity")]
public sealed class PostgresGrantRepositoryTests
{
    private readonly PostgresGrantRepository _repo;

    private readonly PostgresRoleRepository _roleRepo;

    public PostgresGrantRepositoryTests(IdentityDatabaseFixture fixture)
    {
        var connFactory = fixture.CreateConnectionFactory();
        _repo = new PostgresGrantRepository(connFactory);
        _roleRepo = new PostgresRoleRepository(connFactory);
    }

    [Fact]
    public async Task InsertAndGetByRoleIdShouldRoundTrip()
    {
        var role = Role.Create("GrantRole_rt", null);
        await _roleRepo.Insert(role);

        var grant = Grant.Create(role.Id, "party.read", "party");
        await _repo.Insert(grant);

        var grants = await _repo.GetByRoleId(role.Id);

        grants.Should().ContainSingle();
        grants[0].Permission.Should().Be("party.read");
        grants[0].ModuleSource.Should().Be("party");
        grants[0].RoleId.Should().Be(role.Id);
    }

    [Fact]
    public async Task GetByRoleIdShouldReturnEmptyForRoleWithNoGrants()
    {
        var role = Role.Create("GrantRole_empty", null);
        await _roleRepo.Insert(role);

        var grants = await _repo.GetByRoleId(role.Id);

        grants.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteShouldRemoveSpecificGrant()
    {
        var role = Role.Create("GrantRole_del", null);
        await _roleRepo.Insert(role);

        await _repo.Insert(Grant.Create(role.Id, "sales.read", "sales"));
        await _repo.Insert(Grant.Create(role.Id, "sales.write", "sales"));

        await _repo.Delete(role.Id, "sales.read");

        var remaining = await _repo.GetByRoleId(role.Id);

        remaining.Should().ContainSingle(g => g.Permission == "sales.write");
        remaining.Should().NotContain(g => g.Permission == "sales.read");
    }

    [Fact]
    public async Task MultipleGrantsSameRoleShouldAllBeReturned()
    {
        var role = Role.Create("GrantRole_multi", null);
        await _roleRepo.Insert(role);

        await _repo.Insert(Grant.Create(role.Id, "module.action1", "module"));
        await _repo.Insert(Grant.Create(role.Id, "module.action2", "module"));
        await _repo.Insert(Grant.Create(role.Id, "module.action3", "module"));

        var grants = await _repo.GetByRoleId(role.Id);

        grants.Should().HaveCount(3);
    }

    [Fact]
    public async Task InsertGrantWithConditionShouldPersistCondition()
    {
        var role = Role.Create("GrantRole_cond", null);
        await _roleRepo.Insert(role);

        var grant = Grant.Create(role.Id, "party.update", "party", "record.company_id == actor.company_id");
        await _repo.Insert(grant);

        var grants = await _repo.GetByRoleId(role.Id);

        grants.Should().ContainSingle();
        grants[0].Condition.Should().Be("record.company_id == actor.company_id");
    }

    [Fact]
    public async Task InsertGrantWithoutConditionShouldHaveNullCondition()
    {
        var role = Role.Create("GrantRole_nocond", null);
        await _roleRepo.Insert(role);

        var grant = Grant.Create(role.Id, "party.delete", "party");
        await _repo.Insert(grant);

        var grants = await _repo.GetByRoleId(role.Id);

        grants.Should().ContainSingle();
        grants[0].Condition.Should().BeNull();
    }
}
