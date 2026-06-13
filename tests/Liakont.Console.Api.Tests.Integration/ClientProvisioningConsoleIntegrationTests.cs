namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Host.Clients;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// Provisioning de client de bout en bout par le SERVICE CONSOLE (OPS03 lot C), in-process sur une
/// base RÉELLE (Testcontainers ; realm Keycloak via le FAKE du harness — aucun appel réel) :
/// création complète (base migrée + registre avec company_id + realm fake), import de seed (profil
/// visible, AUCUN secret), suspension/réactivation par le service (effet immédiat via le cache
/// invalidé), ISOLATION (le nouveau tenant ne voit rien des autres), et liste composée (statuts
/// réels, jamais inventés).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class ClientProvisioningConsoleIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public ClientProvisioningConsoleIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_Full_Creation_Path_Provisions_Base_Registry_And_Realm_Then_Seeds_And_Suspends()
    {
        // Identifiant UNIQUE par run : le conteneur Postgres est partagé par la collection.
        var tenantId = $"cli-{Guid.NewGuid():N}"[..16];
        var seedDir = CreateSeedDirectory();

        try
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IClientConsoleService>();

            // 1. Création : base créée + migrée, registre porteur d'un company_id, realm (fake) provisionné.
            // Le realm naît SANS utilisateur — le premier utilisateur vient de l'assistant (lot A), pas du socle.
            var creation = await service.CreateTenantAsync(tenantId, "Client Console SARL", "contact@console.test");
            creation.Status.Should().Be(ClientActionStatus.Succeeded);
            creation.AlreadyProvisioned.Should().BeFalse();

            var registered = await GetRegistryRowAsync(tenantId);
            registered.CompanyId.Should().NotBeNull("le company_id est fixé au provisioning et persisté au registre");

            // Le realm (fake) a reçu LE MÊME company_id : la cohérence claim ↔ seed est structurelle.
            var realmRequest = _factory.KeycloakRealms.Provisioned.Single(r => r.TenantId == tenantId);
            realmRequest.CompanyId.Should().Be(registered.CompanyId!.Value.ToString());

            // Rejouer la création est une REPRISE idempotente, pas une erreur.
            var replay = await service.CreateTenantAsync(tenantId, "Client Console SARL", "contact@console.test");
            replay.Status.Should().Be(ClientActionStatus.Succeeded);
            replay.AlreadyProvisioned.Should().BeTrue();

            // 2. Seed (companyId du registre — l'opérateur n'a rien à recopier) : profil importé, AUCUN secret.
            var seeded = await service.ImportSeedAsync(tenantId, Path.GetFileName(seedDir));
            seeded.Status.Should().Be(ClientActionStatus.Succeeded);
            seeded.Imported!.ProfileImported.Should().BeTrue();

            // 3. ISOLATION : le nouveau tenant ne voit AUCUNE donnée d'un autre tenant.
            await using (var conn = OpenTenantDb(registered.DatabaseName))
            {
                await conn.OpenAsync();
                var docCount = await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM documents.documents");
                docCount.Should().Be(0, "database-per-tenant : la base du nouveau client naît vide");

                var profileSiren = await conn.ExecuteScalarAsync<string>(
                    "SELECT siren FROM tenantsettings.tenant_profiles LIMIT 1");
                profileSiren.Should().Be("123456782", "le profil importé est celui du seed");
            }

            // 4. Suspension par le service (SetTenantStatusCommand + invalidation du cache lot B)…
            var suspended = await service.SetStatusAsync(tenantId, suspendre: true);
            suspended.Status.Should().Be(ClientActionStatus.Succeeded);

            // …reflétée par la LISTE composée (statut réel, jamais inventé)…
            var lines = await service.ListAsync();
            lines.Single(l => l.TenantId == tenantId).Statut.Should().Be(ClientStatut.Suspendu);

            // …puis réactivation.
            (await service.SetStatusAsync(tenantId, suspendre: false)).Status.Should().Be(ClientActionStatus.Succeeded);
            (await service.ListAsync()).Single(l => l.TenantId == tenantId).Statut.Should().Be(ClientStatut.Actif);
        }
        finally
        {
            Directory.Delete(seedDir, recursive: true);
        }
    }

    [Fact]
    public async Task A_Seed_Directory_Outside_The_Configured_Root_Is_Refused()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IClientConsoleService>();

        var refused = await service.ImportSeedAsync(ConsoleApiFactory.TenantSeed, ".." + Path.DirectorySeparatorChar + "etc");

        refused.Status.Should().Be(ClientActionStatus.ValidationFailed, "jamais de traversée de disque pilotée par l'UI");
    }

    [Fact]
    public async Task A_Tenant_Without_Profile_Is_Listed_As_ProfilNonCree_Never_Hidden()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IClientConsoleService>();

        var lines = await service.ListAsync();

        // tenant-userprov : registre + base migrée, AUCUN profil (état réel affiché, pas une erreur).
        lines.Single(l => l.TenantId == ConsoleApiFactory.TenantUserProv).Statut
            .Should().Be(ClientStatut.ProfilNonCree);
    }

    /// <summary>Dossier de seed temporaire SOUS la racine configurée du harness (TenantSeeds:RootPath).</summary>
    private string CreateSeedDirectory()
    {
        var root = Path.GetFullPath(_factory.TenantSeedsRootPath);
        Directory.CreateDirectory(root);
        var dir = Path.Combine(root, "seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        const string profileJson = """
            {
              "siren": "123456782",
              "raisonSociale": "Client Console SARL",
              "address": { "street": "1 rue de l'Exemple", "postalCode": "35000", "city": "Rennes", "country": "FR" },
              "contactEmailAlerte": "alertes@console.test"
            }
            """;
        File.WriteAllText(Path.Combine(dir, "tenant-profile.json"), profileJson);
        return dir;
    }

    private async Task<(string DatabaseName, Guid? CompanyId)> GetRegistryRowAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(_factory.SystemConnectionString);
        await conn.OpenAsync();
        var row = await conn.QuerySingleAsync<(string, Guid?)>(
            "SELECT database_name, company_id FROM outbox.tenants WHERE id = @Id",
            new { Id = tenantId });
        return row;
    }

    private NpgsqlConnection OpenTenantDb(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(_factory.SystemConnectionString) { Database = databaseName };
        return new NpgsqlConnection(builder.ToString());
    }
}
