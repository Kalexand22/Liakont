namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Startup;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;
using Xunit;

/// <summary>
/// FIX203b : l'amorçage de l'admin ne doit JAMAIS faire planter le Host (cas 23505), et doit
/// promouvoir un utilisateur déjà présent de façon idempotente.
/// </summary>
public sealed class AdminUserSeederTests
{
    private static AdminSeedOptions Options(string externalId = "ad000000-0000-4000-b000-000000000001") => new()
    {
        Username = "sysadmin",
        ExternalId = externalId,
        Email = "sysadmin@liakont.local",
        DisplayName = "Administrateur systeme (dev)",
    };

    private static UserDto ExistingUser(string? externalId, params string[] roles) => new()
    {
        Id = Guid.NewGuid(),
        Username = "sysadmin",
        Email = "sysadmin@liakont.local",
        DisplayName = "Administrateur systeme (dev)",
        ExternalId = externalId,
        IsActive = true,
        Roles = roles,
    };

    [Fact]
    public async Task SeedAsync_Should_Not_Crash_When_CreateUser_Violates_Unique_ExternalId()
    {
        // Cas réel (bug-inbox) : aucun user par ce username, mais l'ExternalId existe déjà
        // (auto-provisionné OIDC sous un autre nom) → INSERT viole ix_users_external_id (23505).
        var queries = new FakeIdentityQueries(userByUsername: null);
        var logger = new CapturingLogger();
        var sender = new FakeSender
        {
            ThrowOn = req => req is CreateUserCommand
                ? new InvalidOperationException("duplicate key value violates unique constraint \"ix_users_external_id\" (23505)")
                : null,
        };

        var act = async () => await AdminUserSeeder.SeedAsync(sender, queries, Options(), logger);

        await act.Should().NotThrowAsync("le Host ne doit JAMAIS planter sur l'amorçage admin (FIX203b)");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
        sender.Sent.OfType<AssignUserRoleCommand>().Should().BeEmpty("la création a échoué — pas d'assignation de rôle");
    }

    [Fact]
    public async Task SeedAsync_Should_Create_And_Assign_Admin_Role_When_User_Absent()
    {
        var queries = new FakeIdentityQueries(userByUsername: null);
        var sender = new FakeSender();

        await AdminUserSeeder.SeedAsync(sender, queries, Options(), new CapturingLogger());

        sender.Sent.OfType<CreateUserCommand>().Should().ContainSingle();
        sender.Sent.OfType<AssignUserRoleCommand>().Should().Contain(a => a.RoleName == "Admin");
    }

    [Fact]
    public async Task SeedAsync_Should_Promote_Existing_User_Without_Creating()
    {
        // Utilisateur existant sans rôle Admin → doit envoyer AssignUserRoleCommand.
        var queries = new FakeIdentityQueries(userByUsername: ExistingUser(externalId: "ad000000-0000-4000-b000-000000000001"));
        var sender = new FakeSender();

        await AdminUserSeeder.SeedAsync(sender, queries, Options(), new CapturingLogger());

        sender.Sent.OfType<CreateUserCommand>().Should().BeEmpty("l'utilisateur existe déjà");
        sender.Sent.OfType<AssignUserRoleCommand>().Should().Contain(a => a.RoleName == "Admin", "promotion idempotente");
    }

    [Fact]
    public async Task SeedAsync_Should_Backfill_ExternalId_For_PreOidc_Existing_User()
    {
        var queries = new FakeIdentityQueries(userByUsername: ExistingUser(externalId: null));
        var sender = new FakeSender();

        await AdminUserSeeder.SeedAsync(sender, queries, Options(), new CapturingLogger());

        sender.Sent.OfType<LinkExternalIdCommand>().Should().NotBeEmpty("ExternalId manquant doit être rattaché");
    }

    [Fact]
    public async Task SeedAsync_Should_Not_Reassign_When_User_Already_Has_Admin_Role()
    {
        // Garde amont : Roles contient déjà "Admin" → pas d'envoi de AssignUserRoleCommand.
        var queries = new FakeIdentityQueries(userByUsername: ExistingUser(externalId: "ad000000-0000-4000-b000-000000000001", "Admin"));
        var sender = new FakeSender();

        await AdminUserSeeder.SeedAsync(sender, queries, Options(), new CapturingLogger());

        sender.Sent.OfType<AssignUserRoleCommand>().Should().BeEmpty("la garde Roles.Any(Admin) évite l'envoi redondant");
    }

    [Fact]
    public async Task SeedAsync_Should_Skip_When_ExternalId_Empty()
    {
        var queries = new FakeIdentityQueries(userByUsername: null);
        var sender = new FakeSender();

        await AdminUserSeeder.SeedAsync(sender, queries, Options(externalId: string.Empty), new CapturingLogger());

        sender.Sent.Should().BeEmpty("sans ExternalId, l'amorçage admin est ignoré");
    }

    private sealed class FakeSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Guid CreatedUserId { get; init; } = Guid.NewGuid();

        public Func<object, Exception?>? ThrowOn { get; init; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            if (ThrowOn?.Invoke(request!) is { } ex)
            {
                throw ex;
            }

            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            if (ThrowOn?.Invoke(request) is { } ex)
            {
                throw ex;
            }

            return Task.FromResult((TResponse)(object)CreatedUserId);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIdentityQueries : IIdentityQueries
    {
        private readonly UserDto? _userByUsername;

        public FakeIdentityQueries(UserDto? userByUsername)
        {
            _userByUsername = userByUsername;
        }

        public Task<UserDto?> GetUserByUsername(string username, CancellationToken ct = default) =>
            Task.FromResult(_userByUsername);

        public Task<IReadOnlyList<UserDto>> ListUsers(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UserDto?> GetUserById(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UserDto?> GetUserByEmail(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetUserPermissions(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> UserHasPermission(Guid userId, string permission, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GrantConditionDto>> GetUserGrantsForPermission(Guid userId, string permission, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RoleDto>> GetRoles(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RoleDetailDto?> GetRoleById(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RoleUserDto>> GetUsersForRole(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, EventId Id, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
