namespace Liakont.Modules.Ged.Contracts.Queries;

/// <summary>
/// Un lien document↔entité COURANT (vue <c>current_document_entity_links</c>, F19 §3.4.5) restitué à la fiche
/// document (GED09b) : l'entité liée, dans son rôle métier déclaré. Une entité dont le TYPE est confidentiel
/// (sans le droit <c>liakont.ged.confidential</c>) est EXCLUE server-side (§6.5, confidentialité héritée du
/// type d'entité) — elle n'apparaît jamais ici (anti-oracle).
/// </summary>
/// <param name="Role">Rôle métier déclaré (paramétrage tenant), ex. « destinataire », « site ».</param>
/// <param name="EntityTypeCode">Clé machine du type d'entité (paramétrage tenant).</param>
/// <param name="EntityTypeLabel">Libellé opérateur (FR) du type d'entité.</param>
/// <param name="DisplayName">Libellé opérateur (FR) de l'instance d'entité.</param>
/// <param name="IdentityValue">Valeur de clé d'identité (ex. SIRET) si l'entité en porte une, sinon <see langword="null"/>.</param>
public sealed record GedManagedEntityLink(
    string Role,
    string EntityTypeCode,
    string EntityTypeLabel,
    string DisplayName,
    string? IdentityValue);
