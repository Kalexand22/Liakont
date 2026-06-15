namespace Liakont.Host.Tests.Unit.Keycloak;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    // Le brokering SSO (RLM05, ADR-0021 §4) est configuré sur les DEUX realms dev + E2E (mandat « deux
    // realms ») PLUS le realm appliance (prod : cible de la recette opérateur) — voir BrokeringRealmFiles.
    private const string ApplianceRealmPath = "deploy/docker/appliance/keycloak/realm-liakont.json";

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

    // Les assertions de brokering portent sur les TROIS fichiers de realm (dev + E2E + appliance).
    public static IEnumerable<object[]> BrokeringRealmFiles()
    {
        foreach (var realm in RealmFiles())
        {
            yield return realm;
        }

        yield return [ApplianceRealmPath];
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

    // ── Brokering SSO (RLM05, ADR-0021 §4) ────────────────────────────────────
    // La VRAIE garde anti-prise-de-contrôle est dans la config realm (pas seulement dans la logique
    // applicative) : ces tests prouvent STATIQUEMENT, sans credentials ni Keycloak, que le first-broker
    // login lie par email VÉRIFIÉ à un compte déjà provisionné et n'auto-crée JAMAIS. La recette SSO
    // RÉELLE (Google/Microsoft, credentials opérateur) est portée par l'opérateur au déploiement.
    [Theory]
    [MemberData(nameof(BrokeringRealmFiles))]
    public void Identity_Providers_Do_Not_Trust_External_Email(string realmPath)
    {
        using var realm = LoadRealm(realmPath);
        var providers = IdentityProviders(realm.RootElement).ToList();

        // Au moins Google + Microsoft sont câblés (extensible) — la mécanique de brokering est livrée.
        providers.Select(p => p.GetProperty("alias").GetString())
            .Should().Contain(["google", "microsoft"], "le brokering Google + Microsoft doit être câblé (ADR-0021 §4)");

        foreach (var provider in providers)
        {
            var alias = provider.GetProperty("alias").GetString();

            // trustEmail=false : ne jamais faire confiance à l'email externe pour matcher un compte
            // (sinon prise de contrôle cross-IdP) — la liaison passe par « Verify existing account by email ».
            provider.GetProperty("trustEmail").GetBoolean()
                .Should().BeFalse($"le provider « {alias} » ne doit PAS faire confiance à l'email externe (INV-0021-5)");

            // linkOnly=false : le provider peut servir de méthode de login (pas seulement de liaison manuelle) ;
            // l'anti-auto-création est porté par le flow (idp-create-user-if-unique DISABLED), pas par linkOnly.
            provider.GetProperty("firstBrokerLoginFlowAlias").GetString()
                .Should().Be("first broker login", $"le provider « {alias} » doit utiliser le first-broker-login durci");
        }
    }

    [Theory]
    [MemberData(nameof(BrokeringRealmFiles))]
    public void Identity_Provider_Secrets_Are_Placeholders_Never_Clear(string realmPath)
    {
        using var realm = LoadRealm(realmPath);

        foreach (var provider in IdentityProviders(realm.RootElement))
        {
            var alias = provider.GetProperty("alias").GetString();
            var secret = provider.GetProperty("config").GetProperty("clientSecret").GetString();

            // CLAUDE.md n°10/18 : aucun secret de provider versionné. Le secret est un placeholder d'env
            // (${VAR}), substitué à l'exécution — jamais une valeur en clair dans le dépôt.
            secret.Should().NotBeNullOrWhiteSpace();
            Regex.IsMatch(secret!, @"^\$\{[^}]*\}$")
                .Should().BeTrue($"le clientSecret du provider « {alias} » doit être un placeholder ${{...}}, jamais un secret en clair (n°10/18)");
        }
    }

    [Theory]
    [MemberData(nameof(BrokeringRealmFiles))]
    public void First_Broker_Login_Links_By_Verified_Email_And_Never_Auto_Creates(string realmPath)
    {
        using var realm = LoadRealm(realmPath);

        // « JAMAIS d'auto-création » (INV-0021-5) : l'authenticator qui crée un compte si l'email est
        // inconnu est DISABLED — un email externe inconnu ne peut PAS auto-créer un compte orphelin de company_id.
        var createIfUnique = ExecutionsByAuthenticator(realm.RootElement, "idp-create-user-if-unique");
        createIfUnique.Should().ContainSingle("le first-broker-login doit déclarer idp-create-user-if-unique (pour le neutraliser)");
        Requirement(createIfUnique[0])
            .Should().Be("DISABLED", "email externe inconnu ⇒ PAS d'auto-création (INV-0021-5)");

        // « Verify existing account by email » câblée et OBLIGATOIRE : un email non vérifié ne peut pas lier.
        var emailVerification = ExecutionsByAuthenticator(realm.RootElement, "idp-email-verification");
        emailVerification.Should().ContainSingle("l'étape « Verify existing account by email » doit être câblée");
        Requirement(emailVerification[0])
            .Should().Be("REQUIRED", "email externe non vérifié ⇒ liaison refusée (INV-0021-5)");

        // La liaison à un compte existant est confirmée (idp-confirm-link) — on lie, on ne crée pas.
        ExecutionsByAuthenticator(realm.RootElement, "idp-confirm-link")
            .Should().ContainSingle("la liaison à un compte existant (idp-confirm-link) doit être câblée");
    }

    [Theory]
    [MemberData(nameof(BrokeringRealmFiles))]
    public void No_Identity_Provider_Mapper_Injects_CompanyId(string realmPath)
    {
        using var realm = LoadRealm(realmPath);

        // Anti-prise-de-contrôle : company_id vient du compte LOCAL lié (mapper d'attribut du client),
        // jamais de l'IdP externe. Aucun identityProviderMapper ne doit écrire l'attribut company_id.
        if (!realm.RootElement.TryGetProperty("identityProviderMappers", out var mappers))
        {
            return;
        }

        foreach (var mapper in mappers.EnumerateArray())
        {
            if (!mapper.TryGetProperty("config", out var config))
            {
                continue;
            }

            var target = config.TryGetProperty("user.attribute", out var attr) ? attr.GetString() : null;
            target.Should().NotBe("company_id", "l'IdP externe ne doit JAMAIS pouvoir injecter company_id (anti-takeover, ADR-0021 §4)");
        }
    }

    [Theory]
    [MemberData(nameof(BrokeringRealmFiles))]
    public void Brokered_Login_Reuses_Client_CompanyId_And_Roles_Mappers(string realmPath)
    {
        using var realm = LoadRealm(realmPath);

        // INV-0021-6 : un login brokered se termine en login OIDC standard vers le client `liakont` ; il
        // n'existe AUCUN chemin OIDC séparé (un seul OnTokenValidated). Comme company_id (mapper d'attribut)
        // ET les rôles realm sont mappés au niveau CLIENT, tout login à `liakont` — brokered comme mot de
        // passe — émet ces claims ; le Host projette ensuite rôle→permission (ADR-0017). On prouve ici que
        // les deux mappers source des claims sont bien au niveau client (donc partagés par les deux chemins).
        var mappers = AllProtocolMappers(realm.RootElement).ToList();

        mappers.Any(m =>
            m.GetProperty("protocolMapper").GetString() == "oidc-usermodel-attribute-mapper"
            && m.GetProperty("config").TryGetProperty("claim.name", out var c) && c.GetString() == "company_id")
            .Should().BeTrue("le client doit émettre company_id pour TOUT login (brokered inclus) — INV-0021-6");

        mappers.Any(m => m.GetProperty("protocolMapper").GetString() == "oidc-usermodel-realm-role-mapper")
            .Should().BeTrue("le client doit émettre les rôles realm pour TOUT login (brokered inclus) — base de la projection permission, INV-0021-6");
    }

    [Theory]
    [MemberData(nameof(BrokeringRealmFiles))]
    public void Browser_Flow_Preserves_Password_And_Conditional_Otp(string realmPath)
    {
        using var realm = LoadRealm(realmPath);

        // Filet de sécurité : fournir authenticationFlows dans un export REMPLACE les flows par défaut de
        // Keycloak. Le chemin de login mot de passe + 2FA (le VRAI chemin testé en E2E) doit donc rester
        // intact dans la reproduction. On vérifie sa colonne vertébrale : username/password puis OTP conditionnel.
        ExecutionsByAuthenticator(realm.RootElement, "auth-username-password-form")
            .Should().ContainSingle("le login mot de passe ne doit pas être cassé par l'ajout des flows (reproduction du browser flow)");
        ExecutionsByAuthenticator(realm.RootElement, "auth-otp-form")
            .Should().ContainSingle("le 2FA (OTP conditionnel) ne doit pas être cassé par l'ajout des flows");
    }

    [Theory]
    [MemberData(nameof(BrokeringRealmFiles))]
    public void Browser_Flow_Wiring_Is_Correct_Password_Then_Conditional_Otp(string realmPath)
    {
        using var realm = LoadRealm(realmPath);
        var root = realm.RootElement;

        // "browser" → sous-flow "forms" en ALTERNATIVE
        var browserExecutions = FlowExecutions(root, "browser");
        var formsSubflow = browserExecutions
            .Where(e => e.TryGetProperty("authenticatorFlow", out var af) && af.GetBoolean()
                        && e.TryGetProperty("flowAlias", out var fa) && fa.GetString() == "forms")
            .ToList();
        formsSubflow.Should().ContainSingle(
            "le browser flow doit référencer le sous-flow « forms » (chemin mot de passe + 2FA)");
        Requirement(formsSubflow[0])
            .Should().Be("ALTERNATIVE", "le sous-flow « forms » doit être ALTERNATIVE dans le browser flow");

        // "forms" → auth-username-password-form en REQUIRED + sous-flow "Browser - Conditional OTP" en CONDITIONAL
        var formsExecutions = FlowExecutions(root, "forms");

        var passwordExec = formsExecutions
            .Where(e => e.TryGetProperty("authenticator", out var a) && a.GetString() == "auth-username-password-form")
            .ToList();
        passwordExec.Should().ContainSingle("le flow « forms » doit câbler auth-username-password-form");
        Requirement(passwordExec[0])
            .Should().Be("REQUIRED", "auth-username-password-form doit être REQUIRED dans « forms »");

        var conditionalOtpSubflow = formsExecutions
            .Where(e => e.TryGetProperty("authenticatorFlow", out var af) && af.GetBoolean()
                        && e.TryGetProperty("flowAlias", out var fa) && fa.GetString() == "Browser - Conditional OTP")
            .ToList();
        conditionalOtpSubflow.Should().ContainSingle(
            "le flow « forms » doit référencer le sous-flow « Browser - Conditional OTP »");
        Requirement(conditionalOtpSubflow[0])
            .Should().Be("CONDITIONAL", "le sous-flow « Browser - Conditional OTP » doit être CONDITIONAL dans « forms »");

        // "Browser - Conditional OTP" → conditional-user-configured en REQUIRED + auth-otp-form en REQUIRED
        var conditionalOtpExecutions = FlowExecutions(root, "Browser - Conditional OTP");

        var condUserConfigured = conditionalOtpExecutions
            .Where(e => e.TryGetProperty("authenticator", out var a) && a.GetString() == "conditional-user-configured")
            .ToList();
        condUserConfigured.Should().ContainSingle(
            "le flow « Browser - Conditional OTP » doit câbler conditional-user-configured");
        Requirement(condUserConfigured[0])
            .Should().Be("REQUIRED", "conditional-user-configured doit être REQUIRED dans « Browser - Conditional OTP »");

        var otpFormExec = conditionalOtpExecutions
            .Where(e => e.TryGetProperty("authenticator", out var a) && a.GetString() == "auth-otp-form")
            .ToList();
        otpFormExec.Should().ContainSingle(
            "le flow « Browser - Conditional OTP » doit câbler auth-otp-form");
        Requirement(otpFormExec[0])
            .Should().Be("REQUIRED", "auth-otp-form doit être REQUIRED dans « Browser - Conditional OTP »");
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

    private static IEnumerable<JsonElement> IdentityProviders(JsonElement realmRoot) =>
        realmRoot.TryGetProperty("identityProviders", out var idps)
            ? idps.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static IEnumerable<JsonElement> AllAuthenticationExecutions(JsonElement realmRoot) =>
        realmRoot.TryGetProperty("authenticationFlows", out var flows)
            ? flows.EnumerateArray().SelectMany(f => f.GetProperty("authenticationExecutions").EnumerateArray())
            : Enumerable.Empty<JsonElement>();

    private static List<JsonElement> FlowExecutions(JsonElement realmRoot, string flowAlias) =>
        realmRoot.TryGetProperty("authenticationFlows", out var flows)
            ? flows.EnumerateArray()
                .Where(f => f.TryGetProperty("alias", out var a) && a.GetString() == flowAlias)
                .SelectMany(f => f.GetProperty("authenticationExecutions").EnumerateArray())
                .ToList()
            : [];

    private static List<JsonElement> ExecutionsByAuthenticator(JsonElement realmRoot, string authenticator) =>
        AllAuthenticationExecutions(realmRoot)
            .Where(e => e.TryGetProperty("authenticator", out var a) && a.GetString() == authenticator)
            .ToList();

    private static string? Requirement(JsonElement execution) =>
        execution.GetProperty("requirement").GetString();

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
