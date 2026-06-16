namespace Liakont.Modules.Mandats.Domain.Entities;

using System.Globalization;

/// <summary>
/// Séquence de numérotation fiscale (BT-1) <b>par mandant</b> de l'autofacturation 389 (F15 §1.4/§3, ADR-0025
/// §1/§5). Clé <c>(company_id, mandant_id)</c>, tenant-scopée (CLAUDE.md n°9, INV-BT1-4) : la loi impose une
/// séquence <b>distincte par mandant</b> (BOI-TVA-DECLA-30-20-20-10 §120/§130 ; Annexe 7 G1.42/G1.45 « racine
/// propre au mandataire »).
/// <para>
/// <see cref="NextValue"/> est en <c>bigint</c> (<see cref="long"/>) — JAMAIS un float ; le mandat ne porte
/// aucun montant (CLAUDE.md n°1). Le <see cref="Prefix"/> est du <b>paramétrage tenant</b> (seedé depuis
/// <c>Mandant.NumberingPrefix</c> à la création de la séquence ; aucune valeur réelle en code — INV-MANDATS-5),
/// figé sur la séquence pour que la numérotation reste cohérente même si le préfixe du mandant change ensuite.
/// </para>
/// <para>
/// « Chronologique » ET « continue » (§1.4) sont DEUX invariants distincts (ordre cohérent vs sans trou) :
/// l'ordre n'est pas garanti sous allocation concurrente d'un même mandant — d'où le <b>verrou de séquence par
/// mandant</b> pris par l'allocateur (<c>ISelfBilledNumberAllocator</c>, MND05) qui sérialise <see cref="Allocate"/>.
/// </para>
/// </summary>
public sealed class MandatSequence
{
    private MandatSequence()
    {
        Prefix = string.Empty;
    }

    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9, INV-BT1-4).</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Mandant dont la séquence porte la numérotation (clé tenant-scopée avec <see cref="CompanyId"/>).</summary>
    public Guid MandantId { get; private set; }

    /// <summary>
    /// Racine/préfixe propre au mandant (Annexe 7 G1.42/G1.45 + BOFiP §130) — paramétrage tenant, figé à la
    /// création de la séquence (jamais une valeur réelle en code, INV-MANDATS-5).
    /// </summary>
    public string Prefix { get; private set; }

    /// <summary>Prochain numéro à allouer (<c>bigint</c>, jamais float — CLAUDE.md n°1). Démarre à 1.</summary>
    public long NextValue { get; private set; }

    /// <summary>Date de création (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Date de dernière allocation (UTC), <c>null</c> tant qu'aucun numéro n'a été alloué.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Démarre une séquence pour un mandant (premier numéro = 1). <paramref name="prefix"/> est obligatoire
    /// (préfixe déclaré du mandant) — aucune valeur par défaut inventée (CLAUDE.md n°2).
    /// </summary>
    public static MandatSequence Start(Guid companyId, Guid mandantId, string prefix)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Le tenant (company_id) est obligatoire.", nameof(companyId));
        }

        if (mandantId == Guid.Empty)
        {
            throw new ArgumentException("Le mandant (mandant_id) est obligatoire.", nameof(mandantId));
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException(
                "Le préfixe de numérotation du mandant est obligatoire (aucune valeur par défaut inventée, CLAUDE.md n°2).",
                nameof(prefix));
        }

        return new MandatSequence
        {
            CompanyId = companyId,
            MandantId = mandantId,
            Prefix = prefix,
            NextValue = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    /// <summary>Reconstitue une séquence depuis la base (chemin de chargement) — sans réinitialiser le compteur.</summary>
    public static MandatSequence Reconstitute(
        Guid companyId,
        Guid mandantId,
        string prefix,
        long nextValue,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new MandatSequence
        {
            CompanyId = companyId,
            MandantId = mandantId,
            Prefix = prefix,
            NextValue = nextValue,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>
    /// Alloue le prochain BT-1 fiscal : retourne la valeur brute (bigint) et son rendu formaté
    /// (<see cref="Prefix"/> + valeur), puis avance <see cref="NextValue"/> de 1 (continuité §1.4). L'appelant
    /// (allocateur MND05) persiste l'avancement et l'allocation dans la MÊME transaction, sous verrou de mandant.
    /// </summary>
    public MandatSequenceAllocation Allocate()
    {
        var value = NextValue;
        var formatted = Format(value);
        NextValue = checked(NextValue + 1);
        UpdatedAt = DateTimeOffset.UtcNow;
        return new MandatSequenceAllocation(value, formatted);
    }

    /// <summary>Rend un numéro fiscal : concaténation du préfixe tenant et de la valeur (aucun zéro de remplissage inventé).</summary>
    public string Format(long value) => Prefix + value.ToString(CultureInfo.InvariantCulture);
}
