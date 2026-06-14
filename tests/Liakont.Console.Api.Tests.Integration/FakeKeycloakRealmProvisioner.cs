namespace Liakont.Console.Api.Tests.Integration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Fake du provisioner de realm Keycloak, substitué dans le DI du harness : répond « créé » sans
/// aucun appel réel (le flux de création de tenant — base + registre + realm — reste exerçable
/// in-process avec un Keycloak admin « configuré » mais factice). Enregistre les demandes pour
/// les assertions (notamment le company_id passé au realm).
/// </summary>
public sealed class FakeKeycloakRealmProvisioner : IKeycloakRealmProvisioner
{
    private readonly object _gate = new();
    private readonly List<KeycloakRealmProvisionRequest> _provisioned = [];

    public IReadOnlyList<KeycloakRealmProvisionRequest> Provisioned
    {
        get
        {
            lock (_gate)
            {
                return _provisioned.ToList();
            }
        }
    }

    public Task<KeycloakProvisionResult> ProvisionRealmAsync(
        KeycloakRealmProvisionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _provisioned.Add(request);
        }

        return Task.FromResult(KeycloakProvisionResult.Created(
            request.RealmName,
            $"http://127.0.0.1:1/realms/{request.RealmName}",
            request.ClientSecret));
    }

    public Task DeleteRealmAsync(string realmName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AddTenantRedirectUriAsync(
        string primaryRealmName, string tenantSubdomain, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
