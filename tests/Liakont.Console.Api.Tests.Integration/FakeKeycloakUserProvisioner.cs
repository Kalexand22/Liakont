namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Fake du provisioner d'utilisateur Keycloak (OPS03 lot A), substitué dans le DI du harness :
/// aucun appel Keycloak réel ; enregistre les comptes créés (id séquentiel = futur <c>sub</c>),
/// rôles et attributs pour les assertions. Thread-safe par verrou simple (la collection xUnit
/// sérialise déjà les classes de tests, mais le Host peut paralléliser des requêtes).
/// </summary>
public sealed class FakeKeycloakUserProvisioner : IKeycloakUserProvisioner
{
    private readonly object _gate = new();
    private readonly List<CreatedUser> _users = [];
    private int _sequence;

    public IReadOnlyList<CreatedUser> Users
    {
        get
        {
            lock (_gate)
            {
                return _users.ToList();
            }
        }
    }

    public Task<string?> FindUserIdByUsernameAsync(string realmName, string username, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var match = _users.FirstOrDefault(u =>
                u.RealmName == realmName
                && string.Equals(u.Spec.Username, username, StringComparison.OrdinalIgnoreCase)
                && !u.Deleted);
            return Task.FromResult(match?.Id);
        }
    }

    public Task<string> CreateUserAsync(string realmName, KeycloakUserSpec spec, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var id = $"kc-user-{++_sequence}";
            _users.Add(new CreatedUser(id, realmName, spec));
            return Task.FromResult(id);
        }
    }

    public Task SetUserAttributesAsync(
        string realmName, string userId, IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var user = _users.Single(u => u.Id == userId);
            foreach (var (key, value) in attributes)
            {
                user.Attributes[key] = value;
            }
        }

        return Task.CompletedTask;
    }

    public Task ResetPasswordAsync(string realmName, string userId, string password, bool temporary, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var user = _users.Single(u => u.Id == userId);
            user.LastPassword = password;
            user.LastPasswordTemporary = temporary;
        }

        return Task.CompletedTask;
    }

    public Task EnsureRealmRoleAsync(string realmName, string roleName, string description, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AssignRealmRolesAsync(string realmName, string userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _users.Single(u => u.Id == userId).Roles.AddRange(roleNames);
        }

        return Task.CompletedTask;
    }

    public Task DeleteUserAsync(string realmName, string userId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(u => u.Id == userId);
            if (user is not null)
            {
                user.Deleted = true;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Compte IdP factice créé par un test (assertions : attributs, rôles, suppression-compensation).</summary>
    public sealed class CreatedUser
    {
        public CreatedUser(string id, string realmName, KeycloakUserSpec spec)
        {
            Id = id;
            RealmName = realmName;
            Spec = spec;
        }

        public string Id { get; }

        public string RealmName { get; }

        public KeycloakUserSpec Spec { get; }

        public Dictionary<string, string> Attributes { get; } = [];

        public List<string> Roles { get; } = [];

        public string? LastPassword { get; set; }

        public bool LastPasswordTemporary { get; set; }

        public bool Deleted { get; set; }
    }
}
