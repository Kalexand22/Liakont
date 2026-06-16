namespace Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Mandat de facturation liant le tenant (mandataire) à un <see cref="Mandant"/> (F15 §1.5/§2.2,
/// ADR-0022). C'est de la <b>preuve fiscale</b> : sa persistance suit le gabarit FORT de
/// <c>MappingTable</c> (validation humaine explicite, <see cref="Invalidate"/> à chaque mutation →
/// autofacturation 389 suspendue jusqu'à revalidation, mutation + journal append-only dans la même
/// transaction). Clé métier : (<c>company_id</c>, <c>mandant_id</c>, <c>reference</c>).
/// <para>
/// MND01 ne tranche <b>aucun point fiscal</b> (statuts d'assujettissement admis, valeur du délai :
/// NON TRANCHÉS, F15 §6) : <see cref="AssujettissementStatus"/> est une valeur déclarée opaque
/// (paramétrage tenant) et <see cref="ContestationDelay"/> une durée déclarée — <c>null</c> = décision
/// en attente = <b>389 suspendu</b>, jamais un défaut inventé (INV-MANDATS-4, CLAUDE.md n°2/3).
/// </para>
/// </summary>
public sealed class Mandat
{
    private Mandat()
    {
        Reference = string.Empty;
        ClauseText = string.Empty;
    }

    /// <summary>Identifiant technique du mandat.</summary>
    public Guid Id { get; private set; }

    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9, INV-MANDATS-1).</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Mandant auquel ce mandat se rapporte (<see cref="Mandant.Id"/>).</summary>
    public Guid MandantId { get; private set; }

    /// <summary>Référence métier du mandat (clé tenant-scopée avec <see cref="CompanyId"/> et <see cref="MandantId"/>).</summary>
    public string Reference { get; private set; }

    /// <summary>Texte de la clause de mandat (paramétrage tenant — aucune donnée client en dur).</summary>
    public string ClauseText { get; private set; }

    /// <summary>
    /// Caractère du mandat : <c>true</c> = mandat écrit et préalable (acceptation tacite possible),
    /// <c>false</c> = mandat tacite (acceptation expresse exigée pour chaque facture, BOFiP §290, F15 §1.5).
    /// La bascule tacite elle-même est livrée par MND04 ; MND01 ne porte que le caractère déclaré.
    /// </summary>
    public bool EstEcrit { get; private set; }

    /// <summary>
    /// Statut d'assujettissement <b>déclaré</b> du vendeur (valeur opaque, paramétrage tenant). <c>null</c>
    /// = décision en attente = <b>389 suspendu</b> (INV-MANDATS-4). L'ensemble des statuts admis est une
    /// décision d'expert-comptable (NON TRANCHÉ, F15 §6) — aucune énumération de statuts n'est inventée ici.
    /// </summary>
    public string? AssujettissementStatus { get; private set; }

    /// <summary>
    /// Délai accordé au mandant pour contester (clause du contrat de mandat, BOFiP §390, F15 §1.5). <c>null</c>
    /// = bascule tacite impossible = <b>389 suspendu</b> (INV-MANDATS-4). La valeur du délai relève du contrat
    /// (« librement déterminé par les parties », F15 §6.4) — jamais un défaut produit.
    /// </summary>
    public TimeSpan? ContestationDelay { get; private set; }

    /// <summary>Identité du valideur (humain). <c>null</c> tant que le mandat n'a pas été validé — voir <see cref="IsValidated"/>.</summary>
    public string? ValidatedBy { get; private set; }

    /// <summary>Date de validation humaine. <c>null</c> tant que le mandat n'a pas été validé.</summary>
    public DateOnly? ValidatedDate { get; private set; }

    /// <summary>Date de révocation (UTC). <c>null</c> tant que le mandat n'est pas révoqué — voir <see cref="IsRevoked"/>.</summary>
    public DateTimeOffset? RevokedDate { get; private set; }

    /// <summary>Date de création (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Date de dernière modification (UTC), <c>null</c> si jamais modifiée.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Le mandat a été validé humainement (gabarit <c>MappingTable</c>).</summary>
    public bool IsValidated => !string.IsNullOrWhiteSpace(ValidatedBy) && ValidatedDate.HasValue;

    /// <summary>Le mandat a été révoqué.</summary>
    public bool IsRevoked => RevokedDate.HasValue;

    /// <summary>
    /// L'autofacturation 389 est <b>suspendue</b> dès que l'une des conditions n'est pas réunie
    /// (INV-MANDATS-4, ADR-0022 §4) : statut d'assujettissement <c>null</c>, OU délai de contestation
    /// <c>null</c>, OU mandat non validé, OU mandat révoqué. C'est la traduction structurelle de
    /// « bloquer plutôt qu'émettre faux » (CLAUDE.md n°3) ; la <b>garde</b> qui bloque réellement l'émission
    /// (port <c>ISelfBilledGate</c>) est livrée par MND03 — MND01 n'expose que l'état calculé.
    /// </summary>
    public bool IsSelfBillingSuspended =>
        AssujettissementStatus is null
        || ContestationDelay is null
        || !IsValidated
        || IsRevoked;

    /// <summary>
    /// Crée un nouveau mandat (chemin d'écriture). Un mandat naît <b>« NON VALIDÉE »</b> et donc 389 suspendu.
    /// Rejette toute valeur structurellement absente (référence, clause). Le statut d'assujettissement et le
    /// délai de contestation sont facultatifs (<c>null</c> = suspendu) — aucune valeur par défaut inventée.
    /// </summary>
    public static Mandat Create(
        Guid companyId,
        Guid mandantId,
        string reference,
        string clauseText,
        bool estEcrit,
        string? assujettissementStatus,
        TimeSpan? contestationDelay)
    {
        return new Mandat
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MandantId = mandantId,
            Reference = RequireText(reference, "la référence du mandat"),
            ClauseText = RequireText(clauseText, "le texte de clause du mandat"),
            EstEcrit = estEcrit,
            AssujettissementStatus = NormalizeOptional(assujettissementStatus),
            ContestationDelay = NormalizeDelay(contestationDelay),
            ValidatedBy = null,
            ValidatedDate = null,
            RevokedDate = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    /// <summary>Reconstitue un mandat depuis la base (chemin de chargement) — sans re-générer l'identité.</summary>
    public static Mandat Reconstitute(
        Guid id,
        Guid companyId,
        Guid mandantId,
        string reference,
        string clauseText,
        bool estEcrit,
        string? assujettissementStatus,
        TimeSpan? contestationDelay,
        string? validatedBy,
        DateOnly? validatedDate,
        DateTimeOffset? revokedDate,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new Mandat
        {
            Id = id,
            CompanyId = companyId,
            MandantId = mandantId,
            Reference = reference,
            ClauseText = clauseText,
            EstEcrit = estEcrit,
            AssujettissementStatus = assujettissementStatus,
            ContestationDelay = contestationDelay,
            ValidatedBy = validatedBy,
            ValidatedDate = validatedDate,
            RevokedDate = revokedDate,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>
    /// Met à jour les termes du mandat (clause, caractère écrit/tacite, statut d'assujettissement, délai de
    /// contestation). Toute mutation repasse le mandat <b>« NON VALIDÉE »</b> (<see cref="Invalidate"/>) — les
    /// envois 389 sont suspendus jusqu'à revalidation humaine (ADR-0022 §3). Refusé sur un mandat révoqué.
    /// </summary>
    public void UpdateTerms(string clauseText, bool estEcrit, string? assujettissementStatus, TimeSpan? contestationDelay)
    {
        EnsureNotRevoked();

        ClauseText = RequireText(clauseText, "le texte de clause du mandat");
        EstEcrit = estEcrit;
        AssujettissementStatus = NormalizeOptional(assujettissementStatus);
        ContestationDelay = NormalizeDelay(contestationDelay);
        Invalidate();
    }

    /// <summary>
    /// Marque le mandat comme validé humainement (workflow de validation, gabarit <c>MappingTable</c>).
    /// La date de validation est la date courante (UTC). <paramref name="validatedBy"/> est obligatoire.
    /// Refusé sur un mandat révoqué.
    /// </summary>
    public void Validate(string validatedBy)
    {
        EnsureNotRevoked();

        if (string.IsNullOrWhiteSpace(validatedBy))
        {
            throw new ArgumentException(
                "L'identité du valideur est obligatoire pour valider le mandat (ADR-0022 §3).",
                nameof(validatedBy));
        }

        ValidatedBy = validatedBy.Trim();
        ValidatedDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Révoque le mandat (à compter de maintenant, UTC). Un mandat révoqué a 389 suspendu
    /// (<see cref="IsSelfBillingSuspended"/>). Idempotence interdite : révoquer un mandat déjà révoqué est
    /// une erreur d'appel (jamais un no-op silencieux).
    /// </summary>
    public void Revoke()
    {
        EnsureNotRevoked();

        RevokedDate = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string RequireText(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Champ obligatoire absent : {label} (aucune valeur par défaut inventée, CLAUDE.md n°2).",
                label);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static TimeSpan? NormalizeDelay(TimeSpan? delay)
    {
        if (delay is null)
        {
            return null;
        }

        if (delay.Value <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Le délai de contestation, s'il est renseigné, doit être strictement positif " +
                "(null = bascule tacite impossible = 389 suspendu, jamais un délai nul inventé).",
                nameof(delay));
        }

        return delay;
    }

    private void EnsureNotRevoked()
    {
        if (IsRevoked)
        {
            throw new InvalidOperationException(
                "Le mandat est révoqué : aucune mutation n'est permise. Action opérateur : créez un nouveau mandat.");
        }
    }

    /// <summary>
    /// Efface l'état de validation : toute mutation des termes invalide la validation humaine. Le mandat
    /// repasse « NON VALIDÉE » et l'autofacturation 389 est suspendue jusqu'à revalidation (ADR-0022 §3).
    /// </summary>
    private void Invalidate()
    {
        ValidatedBy = null;
        ValidatedDate = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
