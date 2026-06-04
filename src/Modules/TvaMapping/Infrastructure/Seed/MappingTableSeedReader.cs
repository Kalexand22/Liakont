namespace Liakont.Modules.TvaMapping.Infrastructure.Seed;

using System.Text.Json;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Services;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Importe une table de mapping TVA depuis un fichier de seed JSON (item TVA04) — le format de
/// <c>config/exemples/mapping-exemple.json</c> et de <c>deployments/&lt;client&gt;/</c> (F03 §4.1).
/// Deux étapes : (1) lecture/désérialisation permissive du fichier, (2) conversion en
/// <see cref="MappingTable"/> pour un tenant donné. La conversion ne devine AUCUNE règle fiscale
/// (CLAUDE.md n°2) : tout code catégorie (UNCL5305), toute part et tout mode de taux inconnus sont
/// REJETÉS (message opérateur français), puis <see cref="MappingTable.Create"/> applique la
/// validation structurelle (E à 0 % → VATEX, doublons, cohérence du taux). Lecture pure : aucune
/// écriture en base ici (la persistance passe par <c>ITvaMappingUnitOfWork</c>).
/// </summary>
internal static class MappingTableSeedReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Lit et désérialise un fichier de seed de table de mapping. Lève
    /// <see cref="NotFoundException"/> si le fichier est absent et <see cref="ConflictException"/>
    /// si le JSON est illisible.
    /// </summary>
    public static async Task<MappingTableSeed> ReadFileAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new NotFoundException($"Fichier de seed de mapping TVA introuvable : « {filePath} ».");
        }

        var json = await File.ReadAllTextAsync(filePath, ct);
        MappingTableSeed? seed;
        try
        {
            seed = JsonSerializer.Deserialize<MappingTableSeed>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ConflictException($"Seed de mapping TVA illisible (JSON invalide) : {ex.Message}", ex);
        }

        if (seed is null)
        {
            throw new ConflictException($"Seed de mapping TVA vide : « {filePath} ».");
        }

        return seed;
    }

    /// <summary>
    /// Convertit un seed en <see cref="MappingTable"/> du tenant <paramref name="companyId"/>. Rejette
    /// toute catégorie / part / mode de taux inconnus, puis applique la validation structurelle de la
    /// table (lève <see cref="Domain.InvalidMappingTableException"/> si la table est incohérente).
    /// </summary>
    public static MappingTable ToMappingTable(MappingTableSeed seed, Guid companyId)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var ruleSeeds = seed.Rules ?? Array.Empty<MappingRuleSeed>();
        var rules = new MappingRule[ruleSeeds.Count];
        for (var i = 0; i < ruleSeeds.Count; i++)
        {
            rules[i] = ToMappingRule(ruleSeeds[i], i + 1);
        }

        return MappingTable.Create(
            companyId,
            seed.MappingVersion ?? string.Empty,
            seed.ValidatedBy,
            seed.ValidatedDate,
            ParseEnum<MappingDefaultBehavior>(seed.DefaultBehavior, "le comportement par défaut", ordinal: null),
            rules);
    }

    /// <summary>Lit un fichier de seed et le convertit directement en table du tenant.</summary>
    public static async Task<MappingTable> ImportFileAsync(string filePath, Guid companyId, CancellationToken ct = default)
    {
        var seed = await ReadFileAsync(filePath, ct);
        return ToMappingTable(seed, companyId);
    }

    private static MappingRule ToMappingRule(MappingRuleSeed seed, int ordinal)
    {
        // Les codes d'énumération sont tranchés ici (rejet de l'inconnu, CLAUDE.md n°2) ; la cohérence
        // structurelle (E → VATEX, doublons, taux) est ensuite vérifiée par MappingTable.Create.
        return new MappingRule
        {
            SourceRegimeCode = seed.SourceRegimeCode ?? string.Empty,
            Label = seed.Label,
            Part = ParseEnum<MappingPart>(seed.Part, "la part", ordinal),
            SourceFlags = seed.SourceFlags,
            Category = VatCategoryParser.Parse(seed.Category),
            Vatex = seed.Vatex,
            Note = seed.Note,
            RateMode = ParseEnum<RateMode>(seed.RateMode, "le mode de taux", ordinal),
            RateValue = seed.RateValue,
        };
    }

    private static TEnum ParseEnum<TEnum>(string? value, string fieldLabel, int? ordinal)
        where TEnum : struct, Enum
    {
        var trimmed = value?.Trim();
        var where = ordinal is { } n ? $"règle #{n} : " : string.Empty;
        var allowed = string.Join(", ", Enum.GetNames<TEnum>());

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException(
                $"{where}{fieldLabel} est obligatoire. Valeurs admises : {allowed}.", nameof(value));
        }

        // Correspondance EXACTE (insensible à la casse) avec un nom admis. On n'utilise pas
        // Enum.TryParse seul : il accepterait une valeur numérique (« 0 »), ce qui reviendrait à
        // accepter une valeur hors liste — jamais devinée (même discipline que VatCategoryParser).
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        throw new ArgumentException(
            $"{where}{fieldLabel} inconnu : « {trimmed} ». Valeurs admises : {allowed} — aucune n'est devinée.",
            nameof(value));
    }
}
