namespace Stratum.Modules.Identity.Contracts.Queries;

using Stratum.Modules.Identity.Contracts.DTOs;

public interface IIdentityQueries
{
    Task<IReadOnlyList<UserDto>> ListUsers(CancellationToken ct = default);

    Task<UserDto?> GetUserById(Guid userId, CancellationToken ct = default);

    Task<UserDto?> GetUserByUsername(string username, CancellationToken ct = default);

    Task<UserDto?> GetUserByEmail(string email, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetUserPermissions(Guid userId, CancellationToken ct = default);

    Task<bool> UserHasPermission(Guid userId, string permission, CancellationToken ct = default);

    Task<IReadOnlyList<GrantConditionDto>> GetUserGrantsForPermission(Guid userId, string permission, CancellationToken ct = default);

    Task<IReadOnlyList<RoleDto>> GetRoles(CancellationToken ct = default);

    Task<RoleDetailDto?> GetRoleById(Guid roleId, CancellationToken ct = default);

    Task<IReadOnlyList<RoleUserDto>> GetUsersForRole(Guid roleId, CancellationToken ct = default);
}
