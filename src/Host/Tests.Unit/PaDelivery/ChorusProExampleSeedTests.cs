namespace Liakont.Host.Tests.Unit.PaDelivery;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Liakont.Host.PaDelivery;
using Xunit;

/// <summary>
/// Garde de non-régression sur l'exemple FICTIF livré dans
/// <c>config/exemples/tenant-seed/pa-accounts.json</c> (CP10) : l'entrée <c>ChorusPro</c> doit rester
/// chargeable et ALIGNÉE sur les clés que <see cref="ChorusProAccountResolver"/> attend dans le champ
/// opaque <c>accountIdentifiers</c>. Sans ce test, l'exemple et la doc de raccordement pourraient dériver
/// silencieusement des constantes du résolveur si celles-ci évoluent. Exerce le chargement DEPUIS LE
/// DISQUE (et non un JSON inline), comme <c>ExampleProfileTests</c> côté agent.
/// </summary>
public sealed class ChorusProExampleSeedTests
{
    // Path.Combine par segments : les backslashes littéraux ne sont pas des séparateurs sur le runner Linux
    // du CI (le fichier ne serait jamais trouvé), alors que verify-fast passerait en local Windows.
    private static readonly string ExampleSeedRelativePath =
        Path.Combine("config", "exemples", "tenant-seed", "pa-accounts.json");

    private static readonly string[] RequiredChorusProKeys =
    [
        ChorusProAccountResolver.AccountIdKey,
        ChorusProAccountResolver.TechnicalLoginKey,
        ChorusProAccountResolver.ConnectionEmailKey,
        ChorusProAccountResolver.BaseUrlKey,
        ChorusProAccountResolver.TokenEndpointKey,
    ];

    [Fact]
    public void Shipped_ChorusPro_example_carries_exactly_the_resolver_keys_and_no_secret()
    {
        var json = File.ReadAllText(ResolveExamplePath(ExampleSeedRelativePath));
        using var document = JsonDocument.Parse(json);

        var chorusPro = document.RootElement.EnumerateArray()
            .SingleOrDefault(e =>
                e.TryGetProperty("pluginType", out var type)
                && string.Equals(type.GetString(), "ChorusPro", StringComparison.Ordinal));

        chorusPro.ValueKind.Should().Be(JsonValueKind.Object, "l'exemple doit contenir une entrée ChorusPro (CP10)");
        chorusPro.GetProperty("environment").GetString().Should().Be("Staging");

        // Le champ accountIdentifiers est une CHAÎNE JSON (objet sérialisé) — comme à l'import réel.
        var identifiersRaw = chorusPro.GetProperty("accountIdentifiers").GetString();
        identifiersRaw.Should().NotBeNullOrWhiteSpace();

        using var identifiers = JsonDocument.Parse(identifiersRaw!);
        var keys = identifiers.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        keys.Should().BeEquivalentTo(
            RequiredChorusProKeys,
            "les clés de l'exemple doivent correspondre EXACTEMENT aux constantes du résolveur (anti-dérive)");

        // Aucune valeur sensible dans accountIdentifiers : les secrets (client_id/secret PISTE, mot de passe
        // du compte technique) se saisissent en console, jamais en seed (CLAUDE.md n°10).
        var sensitiveKeys = new[] { "clientId", "clientSecret", "technicalPassword", "apiKey", "password", "secret" };
        keys.Should().NotContain(sensitiveKeys);

        // L'entrée elle-même ne porte aucun champ de secret (l'import ne lit jamais de clé de toute façon).
        chorusPro.TryGetProperty("apiKey", out _).Should().BeFalse("aucun secret ne se versionne (CLAUDE.md n°10)");
    }

    private static string ResolveExamplePath(string relativePath)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Exemple de seed « {relativePath} » introuvable en remontant depuis {AppContext.BaseDirectory}.");
    }
}
