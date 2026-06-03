namespace Stratum.Modules.Identity.Tests.Integration;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Infrastructure.Repositories;
using Stratum.Modules.Identity.Tests.Integration.Fixtures;
using Xunit;

[Collection("Identity")]
public sealed class PostgresRoleRepositoryTests
{
    private readonly PostgresRoleRepository _repo;

    public PostgresRoleRepositoryTests(IdentityDatabaseFixture fixture)
    {
        _repo = new PostgresRoleRepository(fixture.CreateConnectionFactory());
    }

    [Fact]
    public async Task InsertAndGetByIdShouldRoundTrip()
    {
        var role = Role.Create("Reviewer", "Can review documents");

        await _repo.Insert(role);
        var loaded = await _repo.GetById(role.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(role.Id);
        loaded.Name.Should().Be("Reviewer");
        loaded.Description.Should().Be("Can review documents");
        loaded.IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdShouldReturnNullForNonExistentRole()
    {
        var result = await _repo.GetById(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task InsertAndGetByNameShouldRoundTrip()
    {
        var role = Role.Create("Auditor_byname", null, isSystem: false);

        await _repo.Insert(role);
        var loaded = await _repo.GetByName("Auditor_byname");

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Auditor_byname");
    }

    [Fact]
    public async Task GetByNameShouldReturnNullForNonExistentRole()
    {
        var result = await _repo.GetByName("NoSuchRole_xyz987");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllShouldIncludeInsertedRole()
    {
        var uniqueName = $"Role_{Guid.NewGuid():N}";
        var role = Role.Create(uniqueName, null);

        await _repo.Insert(role);
        var all = await _repo.GetAll();

        all.Should().Contain(r => r.Name == uniqueName);
    }

    [Fact]
    public async Task SystemRoleShouldBePersistedCorrectly()
    {
        var role = Role.Create("SystemRole_test", "A system role", isSystem: true);

        await _repo.Insert(role);
        var loaded = await _repo.GetById(role.Id);

        loaded!.IsSystem.Should().BeTrue();
    }
}
