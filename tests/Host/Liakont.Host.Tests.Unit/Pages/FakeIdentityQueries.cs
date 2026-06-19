namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

/// <summary>Double de <see cref="IIdentityQueries"/> renvoyant des jeux fixés (tests bUnit des pages Identity, RB6 P2).</summary>
internal sealed class FakeIdentityQueries : IIdentityQueries
{
    private readonly IReadOnlyList<UserDto> _users;
    private readonly UserDto? _user;
    private readonly RoleDetailDto? _role;
    private readonly IReadOnlyList<RoleDto> _roles;
    private readonly IReadOnlyList<RoleUserDto> _roleUsers;

    public FakeIdentityQueries(
        IReadOnlyList<UserDto>? users = null,
        UserDto? user = null,
        RoleDetailDto? role = null,
        IReadOnlyList<RoleDto>? roles = null,
        IReadOnlyList<RoleUserDto>? roleUsers = null)
    {
        _users = users ?? [];
        _user = user;
        _role = role;
        _roles = roles ?? [];
        _roleUsers = roleUsers ?? [];
    }

    public Task<IReadOnlyList<UserDto>> ListUsers(CancellationToken ct = default) =>
        Task.FromResult(_users);

    public Task<UserDto?> GetUserById(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_user);

    public Task<UserDto?> GetUserByUsername(string username, CancellationToken ct = default) =>
        Task.FromResult(_user);

    public Task<UserDto?> GetUserByEmail(string email, CancellationToken ct = default) =>
        Task.FromResult(_user);

    public Task<IReadOnlyList<string>> GetUserPermissions(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<bool> UserHasPermission(Guid userId, string permission, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<GrantConditionDto>> GetUserGrantsForPermission(Guid userId, string permission, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GrantConditionDto>>([]);

    public Task<IReadOnlyList<RoleDto>> GetRoles(CancellationToken ct = default) =>
        Task.FromResult(_roles);

    public Task<RoleDetailDto?> GetRoleById(Guid roleId, CancellationToken ct = default) =>
        Task.FromResult(_role);

    public Task<IReadOnlyList<RoleUserDto>> GetUsersForRole(Guid roleId, CancellationToken ct = default) =>
        Task.FromResult(_roleUsers);
}
