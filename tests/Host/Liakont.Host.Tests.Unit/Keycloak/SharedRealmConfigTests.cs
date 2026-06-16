namespace Liakont.Host.Tests.Unit.Keycloak;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests STRUCTURELS du realm Keycloak unique partagé (RLM01, ADR-0021) appliqués aux DEUX fichiers de
/// realm — <c>deploy/docker/keycloak/realm-export.json</c> (dev) ET
/// <c>tests/Liakont.Tests.E2E/Fixtures/keycloak-e2e-realm.json</c> (E2E, le vrai chemin de login testé).
/// <para>
/// Principe anti-faux-vert (lot RLM) : toute config de realm vaut pour les deux fichiers, jamais un seul.
/// Ces tests prouvent la CONFIG (mapper d'attribut, immuabilité User Profile, backfill par-utilisateur,
/// 2FA, email, registration, 2e tenant au company_id distinct) sans Keycloak ; la preuve COMPORTEMENTALE
/// (vrai jeton, 2FA réel, Account API refuse l'édition de company_id) relève de la GATE humaine (E2E).
/// </para>
/// </summary>
public sealed class SharedRealmConfigTests
{
    private const string DefaultCompanyId = "00000000-0000-4000-a000-000000000001";
    private const string SecondTenantCompanyId = "00000000-0000-4000-a000-000000000002";

    // Miroir de Liakont.Host.Security.SuperAdminRoles.Names : ces rôles sont exemptés de company_id
    // (acteur cross-tenant / hors périmètre tenant, ADR-0021 §2b). Si SuperAdminRoles change, ce test
    // doit changer aussi — c'est volontaire (il garde l'invariant « tenant user ⇒ company_id »).
    private static readonly HashSet<string> SuperAdminRoles =
        new(StringComparer.OrdinalIgnoreCase) { "Admin", "SystemAdmin", "stratum-admin" };

    public static IEnumerable<object[]> RealmFiles()
    {
        yield return ["deploy/docker/keycloak/realm-export.json"];
        yield return ["tests/Liakont.Tests.E2E/Fixtures/keycloak-e2e-realm.json"];
    }

    [Theory]
    [MemberData(nameof(RealmFiles))]
    public void CompanyId_Is_An_Attribute_Mapper_Never_Hardcoded(string realmPath)
    {
        using var realm = LoadRealm(realmPath);
        var mapper = FindCompanyIdMapper(realm.RootElement);

        mapper.GetProperty("protocolMapper").GetString()
            .Should().Be(
                "oidc-usermodel-attribute-mapper",
                "en realm partagé un mapper hardcodé donnerait la MÊME valeur à tous les jetons (isolation nulle)");

        var config = mapper.GetProperty("config");
        config.GetProperty("user.attribute").GetString().Should().Be("company_id");

        // Le claim DOIT être émis dans les jetons : un mapper d'attribut avec id/access.token.claim=false
        // casserait silencieusement l'isolation (claim absent) en laissant le type correct (anti-faux-vert).
        config.GetProperty("id.token.claim").GetString().Should().Be("true");
        config.GetProperty("access.token.claim").GetString().Should().Be("true");

        // Aucun mapper hardcodé résiduel pour company_id (D1 — racine du faux-vert).
        AllProtocolMappers(realm.RootElement)
            .Where(m => m.GetProperty("protocolMapper").GetString() == "oidc-hardcoded-claim-mapper")
            .Select(m => m.GetProperty("config").TryGetProperty("claim.name", out var c) ? c.GetString() : null)
            .Should().NotContain("company_id");
    }

    [Theory]
    [MemberData(nameof(RealmFiles))]
    public void RegistrationIsDisabled_And_Email_Is_Required_Unique(string realmPath)
    {
        using var realm = LoadRealm(realmPath);
        var root = realm.RootElement;

        root.GetProperty("registrationAllowed").GetBoolean().Should().BeFalse("INV-0021-10 : pas d'auto-inscription");
        root.GetProperty("loginWithEmailAllowed").GetBoolean().Should().BeTrue("INV-0021-8");
        root.GetProperty("duplicateEmailsAllowed").GetBoolean().Should().BeFalse("INV-0021-8 : email unique");
    }

    [Theory]
    [MemberData(nameof(RealmFiles))]
    public void OtpPolicy_Is_Configured_For_2fa(string realmPath)
    {
        using var realm = LoadRealm(realmPath);
        var root = realm.RootElement;

        root.GetProperty("otpPolicyType").GetString().Should().Be("totp", "INV-0021-7 : 2FA TOTP imposé");
        root.GetProperty("otpPolicyAlgorithm").GetString().Should().Be("HmacSHA1");
        root.GetProperty("otpPolicyDigits").GetInt32().Should().Be(6);
        root.GetProperty("otpPolicyPeriod").GetInt32().Should().Be(30);
    }

    [Theory]
    [MemberData(nameof(RealmFiles))]
    public void ConfigureTotp_Is_A_Realm_Default_RequiredAction(string realmPath)
    {
        using var realm = LoadRealm(realmPath);
        var root = realm.RootElement;

        // RLF05 / F6-A (durcissement realm-level de F6) : CONFIGURE_TOTP doit être une required action
        // PAR DÉFAUT du realm (defaultAction=true), pas seulement seedée par compte (dev) ou ajoutée par
        // le provisioner (F6). Conséquence Keycloak : TOUT chemin de création d'utilisateur (console admin
        // Keycloak, brokering RLM05, scripts) hérite automatiquement de l'enrôlement 2FA au 1er login — le
        // « 2FA forcé » ne dépend plus du seul chemin de provisioning (cf. recette GATE_REALM_UNIQUE, F6).
        root.TryGetProperty("requiredActions", out var requiredActions).Should().BeTrue(
            "le realm doit déclarer ses required actions au niveau realm pour rendre CONFIGURE_TOTP action par défaut");

        var configureTotp = requiredActions.EnumerateArray()
            .Where(a => a.GetProperty("alias").GetString() == "CONFIGURE_TOTP")
            .ToList();

        configureTotp.Should().ContainSingle("CONFIGURE_TOTP doit être déclaré une seule fois au niveau realm");

        var action = configureTotp[0];
        action.GetProperty("enabled").GetBoolean().Should().BeTrue("la required action CONFIGURE_TOTP doit être activée");
        action.GetProperty("defaultAction").GetBoolean().Should().BeTrue(
            "INV-0021-7 durci (F6-A) : CONFIGURE_TOTP action PAR DÉFAUT du realm — tout user créé hors provisioner enrôle aussi le 2FA");
    }

    [Theory]
    [MemberData(nameof(RealmFiles))]
    public void CompanyId_Is_Immutable_In_User_Profile(string realmPath)
    {
        using var realm = LoadRealm(realmPath);
        using var userProfileConfig = LoadDeclarativeUserProfile(realm.RootElement);

        var companyIdAttr = userProfileConfig.RootElement.GetProperty("attributes")
            .EnumerateArray()
            .Single(a => a.GetProperty("name").GetString() == "company_id");

        var edit = companyIdAttr.GetProperty("permissions").GetProperty("edit")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        // edit = [admin] uniquement : l'utilisateur NE PEUT PAS éditer company_id (INV-0021-3, immuabilité).
        edit.Should().BeEquivalentTo(["admin"], "INV-0021-3 : company_id éditable admin-only, jamais par l'utilisateur");
    }

    [Theory]
    [MemberData(nameof(RealmFiles))]
    public void Every_Tenant_User_Has_A_NonEmpty_CompanyId_And_SuperAdmin_Has_None(string realmPath)
    {
        using var realm = LoadRealm(realmPath);

        foreach (var user in realm.RootElement.GetProperty("users").EnumerateArray())
        {
            var username = user.GetProperty("username").GetString();
            var roles = ReadRoles(user);
            var companyId = ReadCompanyId(user);
            var isSuperAdmin = roles.Any(SuperAdminRoles.Contains);

            if (isSuperAdmin)
            {
                // ADR-0021 §2b : le super-admin d'instance est HORS périmètre tenant → pas de company_id.
                companyId.Should().BeNull($"le super-admin « {username} » ne doit pas porter de company_id");
            }
            else
            {
                // INV-0021-2a (énumération négative) : aucun utilisateur de tenant sans company_id.
                companyId.Should().NotBeNullOrWhiteSpace(
                    $"l'utilisateur de tenant « {username} » doit porter un company_id non vide (anti §4.24)");
            }
        }
    }

    [Theory]
    [MemberData(nameof(RealmFiles))]
    public void A_Second_Tenant_User_Carries_A_Distinct_CompanyId(string realmPath)
    {
        using var realm = LoadRealm(realmPath);

        var companyIds = realm.RootElement.GetProperty("users").EnumerateArray()
            .Select(ReadCompanyId)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // INV-0021-2b (côté config) : deux company_id réels DIFFÉRENTS existent dans le realm — l'isolation
        // est prouvable via le VRAI mapper d'attribut, pas via un claim synthétique X-Test-Company.
        companyIds.Should().Contain(DefaultCompanyId);
        companyIds.Should().Contain(SecondTenantCompanyId);
        companyIds.Count.Should().BeGreaterThanOrEqualTo(2, "il faut au moins deux tenants pour prouver l'isolation");
    }

    [Fact]
    public void Dev_Realm_Forces_Totp_Enrollment_On_All_Users()
    {
        using var realm = LoadRealm("deploy/docker/keycloak/realm-export.json");

        foreach (var user in realm.RootElement.GetProperty("users").EnumerateArray())
        {
            var username = user.GetProperty("username").GetString();
            var requiredActions = user.TryGetProperty("requiredActions", out var ra)
                ? ra.EnumerateArray().Select(a => a.GetString()).ToList()
                : [];

            // 2FA imposé sur le login mot de passe (D5) : enrôlement TOTP forcé au 1er login.
            requiredActions.Should().Contain("CONFIGURE_TOTP", $"l'utilisateur dev « {username} » doit être forcé d'enrôler le TOTP");
        }
    }

    [Fact]
    public void E2E_Realm_PreEnrolls_Totp_For_Every_User()
    {
        using var realm = LoadRealm("tests/Liakont.Tests.E2E/Fixtures/keycloak-e2e-realm.json");

        foreach (var user in realm.RootElement.GetProperty("users").EnumerateArray())
        {
            var username = user.GetProperty("username").GetString();
            var otp = user.GetProperty("credentials").EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "otp");

            otp.ValueKind.Should().NotBe(
                JsonValueKind.Undefined,
                $"l'utilisateur E2E « {username} » doit avoir un credential TOTP pré-enrôlé (login automatisé du 2FA)");

            using var secretDoc = JsonDocument.Parse(otp.GetProperty("secretData").GetString()!);
            var secret = secretDoc.RootElement.GetProperty("value").GetString();
            secret.Should().NotBeNullOrWhiteSpace();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static JsonDocument LoadRealm(string repoRelativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(fullPath).Should().BeTrue($"fichier de realm introuvable : {fullPath}");
        return JsonDocument.Parse(File.ReadAllText(fullPath));
    }

    private static JsonDocument LoadDeclarativeUserProfile(JsonElement realmRoot)
    {
        var json = realmRoot.GetProperty("components")
            .GetProperty("org.keycloak.userprofile.UserProfileProvider")[0]
            .GetProperty("config")
            .GetProperty("kc.user.profile.config")[0]
            .GetString();

        json.Should().NotBeNullOrWhiteSpace("le composant declarative-user-profile doit être présent (D4)");
        return JsonDocument.Parse(json!);
    }

    private static JsonElement FindCompanyIdMapper(JsonElement realmRoot) =>
        AllProtocolMappers(realmRoot).Single(m =>
            m.GetProperty("config").TryGetProperty("claim.name", out var c) && c.GetString() == "company_id");

    private static IEnumerable<JsonElement> AllProtocolMappers(JsonElement realmRoot) =>
        realmRoot.GetProperty("clients").EnumerateArray()
            .Where(client => client.TryGetProperty("protocolMappers", out _))
            .SelectMany(client => client.GetProperty("protocolMappers").EnumerateArray());

    private static List<string> ReadRoles(JsonElement user) =>
        user.TryGetProperty("realmRoles", out var roles)
            ? roles.EnumerateArray().Select(r => r.GetString() ?? string.Empty).ToList()
            : [];

    private static string? ReadCompanyId(JsonElement user)
    {
        if (!user.TryGetProperty("attributes", out var attrs)
            || !attrs.TryGetProperty("company_id", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
        {
            return null;
        }

        return values[0].GetString();
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
            "Racine du dépôt introuvable depuis " + AppContext.BaseDirectory
            + " (recherche de deploy/docker/keycloak/realm-export.json).");
    }
}
