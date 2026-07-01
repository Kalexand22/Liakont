namespace Liakont.Modules.Ged.Domain.Mapping;

using System;
using System.Collections.Generic;

/// <summary>
/// Profil de mapping déclaratif d'un <c>documentType</c> (F19 §4.5, généralisation de <c>MappingTable</c> du
/// domaine TVA) : tenant-scopé (isolation = la connexion, la base EST le tenant — F19 §3.2), <b>versionné</b> et
/// <b>validé humainement</b>. Comme <c>MappingTable</c>, il n'est APPLIQUÉ que s'il est <see cref="IsValidated"/>
/// (jamais un profil non validé) ; toute mutation le fait retomber « non validé » (<see cref="Invalidate"/>).
/// La structure est re-validée à la construction ET au rechargement (<see cref="Reconstitute"/>) : un profil
/// corrompu (type/version vide, code d'axe dupliqué, sélecteur mal formé) n'est jamais chargé en silence.
/// </summary>
public sealed class GedMappingProfile
{
    /// <summary>Version initiale d'un profil neuf.</summary>
    public const string InitialProfileVersion = "1";

    private GedMappingProfile(
        Guid id,
        string documentType,
        string profileVersion,
        string? storagePolicy,
        string? validatedBy,
        DateOnly? validatedDate,
        IReadOnlyList<AxisMappingRule> axisRules,
        IReadOnlyList<EntityMappingRule> entityRules,
        IReadOnlyList<RelationMappingRule> relationRules,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        Id = id;
        DocumentType = documentType;
        ProfileVersion = profileVersion;
        StoragePolicy = storagePolicy;
        ValidatedBy = validatedBy;
        ValidatedDate = validatedDate;
        AxisRules = axisRules;
        EntityRules = entityRules;
        RelationRules = relationRules;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Identifiant du profil.</summary>
    public Guid Id { get; }

    /// <summary>Type de document source (brut) auquel s'applique le profil.</summary>
    public string DocumentType { get; }

    /// <summary>Version du profil (traçabilité — chaque document rangé pointe la version appliquée).</summary>
    public string ProfileVersion { get; private set; }

    /// <summary>Politique de rangement déclarée (informative, ex. « WormPlusIndex »), ou <see langword="null"/>.</summary>
    public string? StoragePolicy { get; }

    /// <summary>Identité du valideur (expert-comptable/opérateur) ; <see langword="null"/> = NON VALIDÉ.</summary>
    public string? ValidatedBy { get; private set; }

    /// <summary>Date de validation ; <see langword="null"/> = NON VALIDÉ.</summary>
    public DateOnly? ValidatedDate { get; private set; }

    /// <summary>Règles d'axe (ordre de déclaration).</summary>
    public IReadOnlyList<AxisMappingRule> AxisRules { get; }

    /// <summary>Règles d'entité.</summary>
    public IReadOnlyList<EntityMappingRule> EntityRules { get; }

    /// <summary>Règles de relation.</summary>
    public IReadOnlyList<RelationMappingRule> RelationRules { get; }

    /// <summary>Horodatage de création.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Horodatage de dernière mutation, ou <see langword="null"/>.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Indicateur de validation (miroir de <c>MappingTable.IsValidated</c>) : un profil n'est APPLIQUÉ par
    /// <see cref="GedMapper"/> que si <see cref="ValidatedBy"/> et <see cref="ValidatedDate"/> sont renseignés.
    /// </summary>
    public bool IsValidated => !string.IsNullOrWhiteSpace(ValidatedBy) && ValidatedDate.HasValue;

    /// <summary>
    /// Crée un profil neuf (chemin d'écriture). Re-valide la structure (lève <see cref="InvalidGedMappingProfileException"/>).
    /// </summary>
    /// <param name="documentType">Type de document source.</param>
    /// <param name="profileVersion">Version du profil.</param>
    /// <param name="storagePolicy">Politique de rangement (facultative).</param>
    /// <param name="validatedBy">Identité du valideur, ou <see langword="null"/> (profil non validé).</param>
    /// <param name="validatedDate">Date de validation, ou <see langword="null"/>.</param>
    /// <param name="axisRules">Règles d'axe.</param>
    /// <param name="entityRules">Règles d'entité.</param>
    /// <param name="relationRules">Règles de relation.</param>
    /// <param name="createdAt">Horodatage de création (fourni par l'appelant).</param>
    /// <returns>Le profil validé structurellement.</returns>
    public static GedMappingProfile Create(
        string documentType,
        string profileVersion,
        string? storagePolicy,
        string? validatedBy,
        DateOnly? validatedDate,
        IReadOnlyList<AxisMappingRule> axisRules,
        IReadOnlyList<EntityMappingRule> entityRules,
        IReadOnlyList<RelationMappingRule> relationRules,
        DateTimeOffset createdAt)
    {
        return Build(
            Guid.NewGuid(),
            documentType,
            profileVersion,
            storagePolicy,
            validatedBy,
            validatedDate,
            axisRules,
            entityRules,
            relationRules,
            createdAt,
            updatedAt: null);
    }

    /// <summary>
    /// Reconstitue un profil depuis la base (chemin de chargement). Re-valide la structure (lève
    /// <see cref="InvalidGedMappingProfileException"/> sur donnée corrompue).
    /// </summary>
    /// <param name="id">Identifiant.</param>
    /// <param name="documentType">Type de document source.</param>
    /// <param name="profileVersion">Version du profil.</param>
    /// <param name="storagePolicy">Politique de rangement.</param>
    /// <param name="validatedBy">Identité du valideur, ou <see langword="null"/>.</param>
    /// <param name="validatedDate">Date de validation, ou <see langword="null"/>.</param>
    /// <param name="axisRules">Règles d'axe.</param>
    /// <param name="entityRules">Règles d'entité.</param>
    /// <param name="relationRules">Règles de relation.</param>
    /// <param name="createdAt">Horodatage de création.</param>
    /// <param name="updatedAt">Horodatage de dernière mutation.</param>
    /// <returns>Le profil reconstitué.</returns>
    public static GedMappingProfile Reconstitute(
        Guid id,
        string documentType,
        string profileVersion,
        string? storagePolicy,
        string? validatedBy,
        DateOnly? validatedDate,
        IReadOnlyList<AxisMappingRule> axisRules,
        IReadOnlyList<EntityMappingRule> entityRules,
        IReadOnlyList<RelationMappingRule> relationRules,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return Build(
            id,
            documentType,
            profileVersion,
            storagePolicy,
            validatedBy,
            validatedDate,
            axisRules,
            entityRules,
            relationRules,
            createdAt,
            updatedAt);
    }

    /// <summary>
    /// Retombe « non validé » (miroir de <c>MappingTable</c> : toute mutation suspend l'application) : l'agent
    /// n'applique plus le profil tant qu'il n'a pas été re-validé humainement.
    /// </summary>
    /// <param name="mutatedAt">Horodatage de la mutation.</param>
    public void Invalidate(DateTimeOffset mutatedAt)
    {
        ValidatedBy = null;
        ValidatedDate = null;
        UpdatedAt = mutatedAt;
    }

    private static GedMappingProfile Build(
        Guid id,
        string documentType,
        string profileVersion,
        string? storagePolicy,
        string? validatedBy,
        DateOnly? validatedDate,
        IReadOnlyList<AxisMappingRule> axisRules,
        IReadOnlyList<EntityMappingRule> entityRules,
        IReadOnlyList<RelationMappingRule> relationRules,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            throw new InvalidGedMappingProfileException("le type de document est vide.");
        }

        if (string.IsNullOrWhiteSpace(profileVersion))
        {
            throw new InvalidGedMappingProfileException("la version du profil est vide.");
        }

        ArgumentNullException.ThrowIfNull(axisRules);
        ArgumentNullException.ThrowIfNull(entityRules);
        ArgumentNullException.ThrowIfNull(relationRules);

        if ((validatedBy is null) != (validatedDate is null))
        {
            throw new InvalidGedMappingProfileException(
                "la validation est incohérente : le valideur et la date de validation doivent être tous deux renseignés ou tous deux absents.");
        }

        ValidateAxisRules(axisRules);
        ValidateEntityRules(entityRules);
        ValidateRelationRules(relationRules);

        return new GedMappingProfile(
            id,
            documentType.Trim(),
            profileVersion.Trim(),
            string.IsNullOrWhiteSpace(storagePolicy) ? null : storagePolicy.Trim(),
            validatedBy,
            validatedDate,
            axisRules,
            entityRules,
            relationRules,
            createdAt,
            updatedAt);
    }

    private static void ValidateAxisRules(IReadOnlyList<AxisMappingRule> axisRules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in axisRules)
        {
            if (rule is null)
            {
                throw new InvalidGedMappingProfileException("une règle d'axe est nulle.");
            }

            if (string.IsNullOrWhiteSpace(rule.AxisCode))
            {
                throw new InvalidGedMappingProfileException("une règle d'axe a un code d'axe vide.");
            }

            if (!seen.Add(rule.AxisCode))
            {
                throw new InvalidGedMappingProfileException($"le code d'axe « {rule.AxisCode} » est dupliqué dans le profil.");
            }

            ValidateSelector(rule.Source, $"règle d'axe « {rule.AxisCode} »");
        }
    }

    private static void ValidateEntityRules(IReadOnlyList<EntityMappingRule> entityRules)
    {
        foreach (var rule in entityRules)
        {
            if (rule is null)
            {
                throw new InvalidGedMappingProfileException("une règle d'entité est nulle.");
            }

            if (string.IsNullOrWhiteSpace(rule.EntityType))
            {
                throw new InvalidGedMappingProfileException("une règle d'entité a un type d'entité vide.");
            }

            ValidateSelector(rule.ExternalIdSource, $"règle d'entité « {rule.EntityType} » (identifiant externe)");
            if (rule.DisplaySource is not null)
            {
                ValidateSelector(rule.DisplaySource, $"règle d'entité « {rule.EntityType} » (libellé)");
            }
        }
    }

    private static void ValidateRelationRules(IReadOnlyList<RelationMappingRule> relationRules)
    {
        foreach (var rule in relationRules)
        {
            if (rule is null)
            {
                throw new InvalidGedMappingProfileException("une règle de relation est nulle.");
            }

            if (string.IsNullOrWhiteSpace(rule.Kind))
            {
                throw new InvalidGedMappingProfileException("une règle de relation a une nature vide.");
            }

            if (string.IsNullOrWhiteSpace(rule.TargetType))
            {
                throw new InvalidGedMappingProfileException($"la règle de relation « {rule.Kind} » a un type cible vide.");
            }

            ValidateSelector(rule.TargetExternalIdSource, $"règle de relation « {rule.Kind} »");
        }
    }

    private static void ValidateSelector(string selector, string context)
    {
        try
        {
            GedSelector.Validate(selector);
        }
        catch (InvalidGedSelectorException ex)
        {
            throw new InvalidGedMappingProfileException($"{context} : {ex.Reason}", ex);
        }
    }
}
