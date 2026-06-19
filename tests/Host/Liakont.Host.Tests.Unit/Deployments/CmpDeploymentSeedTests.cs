namespace Liakont.Host.Tests.Unit.Deployments;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// Validation NOMMÉE du seed de déploiement CMP (lot CMP02), sur les fichiers RÉELS de
/// <c>deployments/cmp/</c>. Périmètre : CORRECTION DES FICHIERS DE SEED (forme JSON + valeurs +
/// cohérence), sans toucher au code produit (<c>src/</c>) ni à l'agent (<c>agent/</c>) — acceptance
/// CMP02. Vérifie, en lisant chaque fichier avec la même TOLÉRANCE de parse que le lecteur de seed
/// (<c>JsonCommentHandling.Skip</c> + virgules traînantes), que :
/// <list type="bullet">
///   <item>le profil <c>tenant-cmp.json</c> est un JSON bien formé portant les champs que l'import
///   exige (SIREN valide, raison sociale, adresse) sous leurs noms canoniques, et laisse les
///   paramètres fiscaux <c>null</c> (en attente expert-comptable, jamais devinés) ;</item>
///   <item>les comptes PA (<c>pa-accounts-cmp.json</c>) ne portent AUCUN secret en clair (clé en
///   placeholder — INV-TENANTSETTINGS-007 / CLAUDE.md n°10) ;</item>
///   <item>la config agent (<c>agent-cmp.json.example</c>) est en mode fixtures (démo), HTTPS, sans
///   secret en clair ;</item>
///   <item>les fixtures de démo couvrent tous les cas du scénario ISATECH et n'utilisent que des
///   régimes COUVERTS par la table de mapping (aucun régime non mappé).</item>
/// </list>
/// PÉRIMÈTRE ASSUMÉ : ce test fait une validation STRUCTURELLE (lecture <c>JsonDocument</c> des
/// propriétés), pas un aller-retour de désérialisation dans les records internes
/// (<c>TenantProfileSeed</c>/<c>PaAccountSeed</c>, non accessibles hors du module et de ses propres
/// tests). L'import en base de bout en bout — désérialisation dans les records puis écriture — est
/// couvert de façon générique par <c>TenantSeedAdminEndpointTests</c> (suite d'intégration). Ce test
/// s'exécute dans verify-fast (rapide, sans conteneur) et cible les fichiers CMP eux-mêmes.
/// </summary>
public sealed class CmpDeploymentSeedTests
{
    // Même TOLÉRANCE de parse que TenantSeedReader.SerializerOptions (commentaires + virgules
    // traînantes des seeds versionnés). La casse insensible des noms de propriété est une option
    // de JsonSerializer côté lecteur réel, non exercée ici : les fichiers de seed emploient les
    // noms canoniques exacts, et c'est précisément ce que ce test vérifie.
    private static readonly JsonDocumentOptions SeedDocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [Fact]
    public void TenantProfile_Seed_Is_Importable_Shaped_With_Fiscal_Params_Pending()
    {
        using var doc = ParseSeed("tenant-cmp.json");
        var root = doc.RootElement;

        var siren = root.GetProperty("siren").GetString();
        siren.Should().NotBeNullOrWhiteSpace();
        IsValidSiren(siren!).Should().BeTrue($"le SIREN « {siren} » du seed CMP doit être un SIREN valide (9 chiffres, Luhn)");

        root.GetProperty("raisonSociale").GetString().Should().NotBeNullOrWhiteSpace();

        var address = root.GetProperty("address");
        address.GetProperty("street").GetString().Should().NotBeNullOrWhiteSpace();
        address.GetProperty("postalCode").GetString().Should().NotBeNullOrWhiteSpace();
        address.GetProperty("city").GetString().Should().NotBeNullOrWhiteSpace();
        address.GetProperty("country").GetString().Should().Be("FR");

        var fiscal = root.GetProperty("fiscal");
        fiscal.GetProperty("vatOnDebits").ValueKind.Should().Be(JsonValueKind.Null, "vatOnDebits reste null tant que l'expert-comptable n'a pas tranché");
        fiscal.GetProperty("operationCategory").ValueKind.Should().Be(JsonValueKind.Null, "operationCategory reste null tant que l'expert-comptable n'a pas tranché");

        var hours = root.GetProperty("schedule").GetProperty("hours").EnumerateArray().Select(h => h.GetString()).ToArray();
        hours.Should().Contain("03:00");
    }

    [Fact]
    public void PaAccounts_Seed_Has_B2Brouter_Staging_Without_Plaintext_Secret()
    {
        using var doc = ParseSeed("pa-accounts-cmp.json");
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var accounts = doc.RootElement.EnumerateArray().ToArray();
        accounts.Should().NotBeEmpty();

        foreach (var account in accounts)
        {
            account.GetProperty("pluginType").GetString().Should().NotBeNullOrWhiteSpace();
            var env = account.GetProperty("environment").GetString();
            env.Should().BeOneOf("Staging", "Production");

            if (account.TryGetProperty("apiKey", out var apiKey))
            {
                IsPlaceholderSecret(apiKey.GetString()).Should().BeTrue(
                    "la clé API d'un compte PA du seed n'est JAMAIS en clair (placeholder uniquement — INV-TENANTSETTINGS-007)");
            }
        }

        var hasB2BrouterStaging = accounts.Any(a =>
            a.GetProperty("pluginType").GetString() == "B2Brouter"
            && a.GetProperty("environment").GetString() == "Staging");
        hasB2BrouterStaging.Should().BeTrue("la démo ISATECH s'exerce sur un compte B2Brouter staging");
    }

    [Fact]
    public void AgentConfig_Example_Is_Fixtures_Mode_Https_Without_Plaintext_Secret()
    {
        using var doc = ParseSeed("agent-cmp.json.example");
        var root = doc.RootElement;

        var platformUrl = root.GetProperty("platformUrl").GetString();
        platformUrl.Should().StartWith("https://", "la plateforme est jointe en HTTPS (CLAUDE.md n°10)");

        IsPlaceholderSecret(root.GetProperty("apiKey").GetString()).Should().BeTrue(
            "la clé API de l'agent dans l'exemple est un placeholder, jamais une clé réelle en clair");

        var extraction = root.GetProperty("extraction");
        extraction.GetProperty("adapter").GetString().Should().Be("EncheresV6");

        extraction.GetProperty("fixturesPath").GetString().Should().NotBeNullOrWhiteSpace();
        extraction.TryGetProperty("odbcConnectionString", out _).Should().BeFalse(
            "en mode fixtures l'exemple ne déclare PAS de clé odbcConnectionString active (mode ODBC mutuellement exclusif, documenté en commentaire _pervasive)");
    }

    [Fact]
    public void MappingTable_Is_Block_By_Default_And_Not_Validated()
    {
        using var doc = ParseSeed("tva-mapping-cmp-v1.json");
        var root = doc.RootElement;

        root.GetProperty("defaultBehavior").GetString().Should().Be("Block", "tout régime non listé doit BLOQUER (jamais d'envoi par défaut)");
        root.GetProperty("validatedDate").ValueKind.Should().Be(JsonValueKind.Null, "la table reste NON VALIDÉE tant que l'expert-comptable CMP n'a pas tranché");
        root.GetProperty("rules").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public void DemoFixtures_Cover_All_Isatech_Scenario_Cases()
    {
        using var doc = ParseSeed(Path.Combine("fixtures-demo", "encheresv6-demo.json"));
        var root = doc.RootElement;

        var regimeCodes = root.GetProperty("regimes").EnumerateArray()
            .Select(r => r.GetProperty("code_regime").GetString())
            .ToArray();
        regimeCodes.Should().Contain("5").And.Contain("6");

        var bordereaux = root.GetProperty("bordereaux").EnumerateArray().ToArray();
        bordereaux.Length.Should().BeGreaterThan(2);

        bordereaux.Any(b => IsKind(b, "B") && LineRegimes(b).Contains("5"))
            .Should().BeTrue("le scénario doit inclure une vente normale (régime 5)");
        bordereaux.Any(b => IsKind(b, "B") && LineRegimes(b).Contains("6"))
            .Should().BeTrue("le scénario doit inclure une vente au régime de la marge (régime 6)");
        bordereaux.Any(IsLetteredCreditNote)
            .Should().BeTrue("le scénario doit inclure un avoir lettré sur sa facture d'origine");
        bordereaux.Any(HasCompanyBuyer)
            .Should().BeTrue("le scénario doit inclure un acheteur professionnel (B2B)");
        bordereaux.Any(b => LineTypes(b).Contains("3"))
            .Should().BeTrue("le scénario doit inclure au moins un encaissement (ligne type 3)");

        var mappedRegimes = MappedSourceRegimes();
        var fixtureRegimes = bordereaux
            .SelectMany(LineRegimes)
            .Concat(regimeCodes)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct();
        foreach (var regime in fixtureRegimes)
        {
            mappedRegimes.Should().Contain(regime!, $"le régime « {regime} » des fixtures doit être couvert par tva-mapping-cmp-v1.json");
        }
    }

    [Fact]
    public void DemoPdfPool_Has_Pdfs_Matching_Bordereau_Numbers()
    {
        var poolDir = Path.Combine(CmpSeedDir(), "fixtures-demo", "pdf-pool");
        Directory.Exists(poolDir).Should().BeTrue();

        var pdfs = Directory.GetFiles(poolDir, "*.pdf");
        pdfs.Should().NotBeEmpty("la réconciliation de démo a besoin de bordereaux PDF factices");

        using var fixtures = ParseSeed(Path.Combine("fixtures-demo", "encheresv6-demo.json"));
        var noBaTokens = fixtures.RootElement.GetProperty("bordereaux").EnumerateArray()
            .Select(b => b.GetProperty("no_ba").GetString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        foreach (var pdf in pdfs)
        {
            ReadPdfHeader(pdf).Should().Be("%PDF-", $"« {Path.GetFileName(pdf)} » doit être un PDF");

            var stem = Path.GetFileNameWithoutExtension(pdf);
            var matchesABordereau = noBaTokens.Any(t => stem.Contains(t!, StringComparison.OrdinalIgnoreCase));
            matchesABordereau.Should().BeTrue($"le PDF « {Path.GetFileName(pdf)} » doit porter le numéro d'un bordereau de la démo");
        }
    }

    private static bool IsKind(JsonElement bordereau, string kind) =>
        bordereau.GetProperty("bordereau_ou_avoir").GetString() == kind;

    private static bool IsLetteredCreditNote(JsonElement bordereau) =>
        IsKind(bordereau, "A")
        && bordereau.TryGetProperty("no_ba_lettrage", out var l)
        && !string.IsNullOrWhiteSpace(l.GetString());

    private static bool HasCompanyBuyer(JsonElement bordereau) =>
        bordereau.TryGetProperty("acheteur_societe", out var s)
        && s.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(s.GetString());

    private static string[] LineRegimes(JsonElement bordereau) =>
        bordereau.GetProperty("lignes").EnumerateArray()
            .Select(l => l.TryGetProperty("code_regime", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null)
            .Where(c => c is not null)
            .ToArray()!;

    private static string[] LineTypes(JsonElement bordereau) =>
        bordereau.GetProperty("lignes").EnumerateArray()
            .Select(l => l.GetProperty("type_ligne").GetString())
            .Where(t => t is not null)
            .ToArray()!;

    private static string[] MappedSourceRegimes()
    {
        using var doc = ParseSeed("tva-mapping-cmp-v1.json");
        return doc.RootElement.GetProperty("rules").EnumerateArray()
            .Select(r => r.GetProperty("sourceRegimeCode").GetString())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToArray()!;
    }

    private static string ReadPdfHeader(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[5];
        var read = stream.Read(head, 0, 5);
        return Encoding.ASCII.GetString(head, 0, read);
    }

    private static JsonDocument ParseSeed(string relativePath)
    {
        var path = Path.Combine(CmpSeedDir(), relativePath);
        File.Exists(path).Should().BeTrue($"fichier de seed introuvable : {path}");
        return JsonDocument.Parse(File.ReadAllText(path), SeedDocOptions);
    }

    private static string CmpSeedDir() => Path.Combine(FindRepoRoot(), "deployments", "cmp");

    private static bool IsPlaceholderSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value!.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("${", StringComparison.Ordinal));

    private static bool IsValidSiren(string siren)
    {
        if (siren.Length != 9 || !siren.All(char.IsDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var digit = siren[i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
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

        throw new InvalidOperationException("Racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }
}
