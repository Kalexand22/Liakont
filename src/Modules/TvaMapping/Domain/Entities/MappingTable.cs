namespace Liakont.Modules.TvaMapping.Domain.Entities;

using Liakont.Modules.TvaMapping.Domain.Services;

/// <summary>
/// Table de mapping TVA d'un tenant (F03 §4.1, item TVA01 §1) : régime source → {catégorie, taux,
/// VATEX}, validée humainement par l'expert-comptable. Une table par tenant. La validation
/// structurelle (catégories UNCL5305, E à 0 % → VATEX, doublons, cohérence du taux) s'applique à la
/// création comme au chargement — voir <see cref="MappingTableValidator"/> ; une table invalide
/// lève <see cref="InvalidMappingTableException"/> (jamais de comportement silencieux, CLAUDE.md n°3).
/// </summary>
public sealed class MappingTable
{
    private MappingTable()
    {
        MappingVersion = string.Empty;
        Rules = [];
    }

    /// <summary>Identifiant technique de la table.</summary>
    public Guid Id { get; private set; }

    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9).</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Version de la table (traçabilité : chaque document émis pointe la version — F03 §5).</summary>
    public string MappingVersion { get; private set; }

    /// <summary>
    /// Identité du valideur (expert-comptable). <c>null</c> tant que la table n'a pas été validée
    /// humainement — voir <see cref="IsValidated"/>.
    /// </summary>
    public string? ValidatedBy { get; private set; }

    /// <summary>Date de validation humaine. <c>null</c> tant que la table n'a pas été validée.</summary>
    public DateOnly? ValidatedDate { get; private set; }

    /// <summary>Comportement pour un régime source absent de la table (F03 §4.1 : toujours <c>block</c>).</summary>
    public MappingDefaultBehavior DefaultBehavior { get; private set; }

    /// <summary>Règles de mapping, dans l'ordre de déclaration.</summary>
    public IReadOnlyList<MappingRule> Rules { get; private set; }

    /// <summary>Date de création (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Date de dernière modification (UTC), <c>null</c> si jamais modifiée.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Indicateur de validation (item TVA01 §5) : une table dont <see cref="ValidatedBy"/> ou
    /// <see cref="ValidatedDate"/> n'est pas renseigné est chargeable (dev/démo) mais « NON VALIDÉE »
    /// — cet état est exposé partout (GET /settings, console, supervision) et le garde-fou d'envoi
    /// en production (PIP01) s'en sert pour refuser un envoi réel.
    /// </summary>
    public bool IsValidated => !string.IsNullOrWhiteSpace(ValidatedBy) && ValidatedDate.HasValue;

    /// <summary>
    /// Crée une nouvelle table (chemin d'écriture). Valide la structure : lève
    /// <see cref="InvalidMappingTableException"/> si une règle viole une contrainte structurelle.
    /// </summary>
    public static MappingTable Create(
        Guid companyId,
        string mappingVersion,
        string? validatedBy,
        DateOnly? validatedDate,
        MappingDefaultBehavior defaultBehavior,
        IReadOnlyList<MappingRule> rules)
    {
        var ruleList = NormalizeRules(rules);
        MappingTableValidator.EnsureValid(mappingVersion, defaultBehavior, ruleList);

        return new MappingTable
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MappingVersion = mappingVersion.Trim(),
            ValidatedBy = NormalizeValidatedBy(validatedBy),
            ValidatedDate = validatedDate,
            DefaultBehavior = defaultBehavior,
            Rules = ruleList,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    /// <summary>
    /// Reconstitue une table depuis la base (chemin de chargement). Re-valide la structure : une
    /// table persistée corrompue (édition manuelle, régression) lève
    /// <see cref="InvalidMappingTableException"/> au chargement plutôt que d'être servie fausse.
    /// </summary>
    public static MappingTable Reconstitute(
        Guid id,
        Guid companyId,
        string mappingVersion,
        string? validatedBy,
        DateOnly? validatedDate,
        MappingDefaultBehavior defaultBehavior,
        IReadOnlyList<MappingRule> rules,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        var ruleList = NormalizeRules(rules);
        MappingTableValidator.EnsureValid(mappingVersion, defaultBehavior, ruleList);

        return new MappingTable
        {
            Id = id,
            CompanyId = companyId,
            MappingVersion = mappingVersion,
            ValidatedBy = validatedBy,
            ValidatedDate = validatedDate,
            DefaultBehavior = defaultBehavior,
            Rules = ruleList,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    private static MappingRule[] NormalizeRules(IReadOnlyList<MappingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.ToArray();
    }

    private static string? NormalizeValidatedBy(string? validatedBy)
    {
        if (validatedBy is null)
        {
            return null;
        }

        var trimmed = validatedBy.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
