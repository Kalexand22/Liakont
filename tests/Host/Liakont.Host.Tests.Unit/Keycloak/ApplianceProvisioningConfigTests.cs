namespace Liakont.Host.Tests.Unit.Keycloak;

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Garde STATIQUE du câblage du provisioning d'utilisateur de tenant dans l'appliance de PROD (RLF06,
/// finding F7). Le service <c>liakont</c> du compose appliance DOIT poser
/// <c>Keycloak__PrimaryRealmName</c> + les credentials Admin Keycloak du Host
/// (<c>AdminBaseUrl</c>/<c>AdminUsername</c>/<c>AdminPassword</c>). Sans eux,
/// <see cref="Stratum.Common.Infrastructure.Keycloak.KeycloakAdminOptions.IsConfigured"/> est
/// <c>false</c> (« IdP non configuré ») et le realm partagé est « non configuré » → le provisioning
/// d'utilisateur est mort en prod (gap PRÉ-EXISTANT avant F2).
/// <para>
/// Anti-faux-vert : prouvé sur le FICHIER de déploiement (sans Docker) ; le chemin réel relève de la
/// GATE humaine (recette). Anti-secret-versionné (CLAUDE.md n°10/18) : le mot de passe admin DOIT être
/// une référence d'environnement <c>${...}</c>, jamais un littéral committé.
/// </para>
/// </summary>
public sealed class ApplianceProvisioningConfigTests
{
    private const string ComposePath = "deploy/docker/appliance/docker-compose.yml";

    [Fact]
    public void Appliance_Liakont_Service_Wires_PrimaryRealm_And_Admin_Credentials()
    {
        var block = LiakontServiceBlock();

        // Realm partagé prod (cohérent avec Keycloak__Authority .../realms/liakont et
        // Keycloak__RealmTenantMap__liakont). Sans lui : « Realm partagé non configuré ».
        block.Should().Contain(
            "Keycloak__PrimaryRealmName: liakont",
            "le provisioning en profil partagé cible Keycloak:PrimaryRealmName (KeycloakTenantUserProvisioner)");

        // Les trois clés de KeycloakAdminOptions.IsConfigured.
        // AdminBaseUrl = RACINE serveur, JAMAIS une URL de realm : le service y ajoute /realms/master/...
        // (KeycloakAdminTokenService) — un suffixe /realms/... donnerait .../realms/<x>/realms/master/...
        // → 404, provisioning mort. Une régression vers une URL de realm DOIT virer au rouge (invariant
        // central que le commentaire du compose martèle).
        var adminBaseUrlLine = ServiceLine(block, "Keycloak__AdminBaseUrl:");
        adminBaseUrlLine.Should().NotContain(
            "/realms",
            "AdminBaseUrl doit être la racine du serveur Keycloak, pas une URL de realm");
        adminBaseUrlLine.Should().MatchRegex(
            @"Keycloak__AdminBaseUrl:\s*""?https?://[^/""\s]+(:\d+)?/?""?\s*$",
            "AdminBaseUrl doit être une racine http(s)://hôte[:port] sans segment de chemin");

        // Username + password : références d'environnement (forme obligatoire ${VAR:?…} OU simple ${VAR}),
        // jamais un littéral. Réutilisent le compte d'amorçage admin déjà requis par le service keycloak.
        ServiceLine(block, "Keycloak__AdminUsername:").Should().MatchRegex(
            @"Keycloak__AdminUsername:\s*\$\{KC_BOOTSTRAP_ADMIN_USERNAME(:\?[^}]*)?\}",
            "le Host réutilise le compte d'amorçage admin (variable d'env KC_BOOTSTRAP_ADMIN_USERNAME, jamais un littéral)");
        ServiceLine(block, "Keycloak__AdminPassword:").Should().MatchRegex(
            @"Keycloak__AdminPassword:\s*\$\{KC_BOOTSTRAP_ADMIN_PASSWORD(:\?[^}]*)?\}",
            "le Host réutilise le mot de passe d'amorçage admin (variable d'env KC_BOOTSTRAP_ADMIN_PASSWORD, jamais un littéral)");
    }

    [Fact]
    public void Appliance_Admin_Password_Is_An_Env_Reference_Never_A_Literal()
    {
        // CLAUDE.md n°10/18 : le secret vient d'une variable d'environnement (.env, non versionné),
        // jamais d'une valeur en clair committée dans le compose.
        ServiceLine(LiakontServiceBlock(), "Keycloak__AdminPassword:").Should().MatchRegex(
            @"Keycloak__AdminPassword:\s*\$\{[^}]+\}",
            "le mot de passe admin doit être une référence d'environnement (syntaxe dollar-accolade), jamais un littéral en clair");
    }

    /// <summary>Ligne (unique) du bloc de service portant la clé donnée ; échoue si la clé n'est pas câblée.</summary>
    private static string ServiceLine(string block, string key)
    {
        var line = block.Split('\n').FirstOrDefault(l => l.Contains(key, StringComparison.Ordinal));
        line.Should().NotBeNull($"le service liakont doit câbler {key}");
        return line!;
    }

    /// <summary>
    /// Bloc YAML du service <c>liakont</c> uniquement (de <c>  liakont:</c> jusqu'au service suivant à
    /// l'indentation des services), pour scoper les assertions au bon service.
    /// </summary>
    private static string LiakontServiceBlock()
    {
        var compose = File.ReadAllText(RepoFile(ComposePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = compose.Split('\n');

        var start = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^  liakont:\s*$"));
        start.Should().BeGreaterThanOrEqualTo(0, $"le service `liakont` doit exister dans {ComposePath}");

        // Service suivant = prochaine clé à l'indentation 2 espaces (sibling de `liakont:`).
        var end = Array.FindIndex(lines, start + 1, l => Regex.IsMatch(l, @"^  [A-Za-z]"));
        if (end < 0)
        {
            end = lines.Length;
        }

        return string.Join('\n', lines.Skip(start).Take(end - start));
    }

    private static string RepoFile(string repoRelativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(fullPath).Should().BeTrue($"fichier introuvable : {fullPath}");
        return fullPath;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "deploy", "docker", "keycloak", "realm-export.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }
}
