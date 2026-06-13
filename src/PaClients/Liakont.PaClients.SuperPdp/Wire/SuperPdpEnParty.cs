namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Une partie du document — vendeur (EN 16931 BG-4, schéma <c>seller</c>) ou acheteur (BG-7, schéma
/// <c>buyer</c>). Les identifiants viennent du PIVOT (SIREN, n° TVA) — jamais inventés ici (CLAUDE.md
/// n°2/7). L'adressage d'annuaire (<c>electronic_address</c>) par SIREN scheme <c>0002</c> est ✅ validé
/// en sandbox (F14 §3.2) : requis par le schéma pour le vendeur, exigé à l'ENVOI pour l'acheteur
/// (« missing buyer electronic address »).
/// </summary>
internal sealed record SuperPdpEnParty
{
    /// <summary>Raison sociale (EN 16931 BT-27 / BT-44).</summary>
    public required string Name { get; init; }

    /// <summary>Identifiant légal (EN 16931 BT-30) — SIREN au scheme ISO 6523 <c>0002</c>.</summary>
    public SuperPdpEnIdentifier? LegalRegistrationIdentifier { get; init; }

    /// <summary>N° TVA intracommunautaire (EN 16931 BT-31 / BT-48) — requis vendeur si catégorie S (BR-S-02).</summary>
    public string? VatIdentifier { get; init; }

    /// <summary>Adresse électronique d'annuaire (EN 16931 BT-34 / BT-49) — SIREN scheme <c>0002</c>.</summary>
    public SuperPdpEnIdentifier? ElectronicAddress { get; init; }

    /// <summary>Adresse postale (EN 16931 BG-5 / BG-8) — <c>country_code</c> requis par le schéma.</summary>
    public SuperPdpEnPostalAddress? PostalAddress { get; init; }
}
