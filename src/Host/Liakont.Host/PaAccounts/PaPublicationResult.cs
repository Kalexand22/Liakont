namespace Liakont.Host.PaAccounts;

/// <summary>
/// Résultat de l'action « Publier le SIREN / activer la transmission » (FIX201), présenté tel quel à
/// l'opérateur. <see cref="Success"/> distingue l'aboutissement (réglage publié côté PA) d'un refus métier
/// (permission manquante, profil incomplet, aucun compte actif, PA injoignable). <see cref="Message"/> est
/// le message opérateur en français, avec l'action corrective quand c'est pertinent (CLAUDE.md n°12). Ne
/// porte jamais de secret.
/// </summary>
public sealed record PaPublicationResult(bool Success, string Message)
{
    /// <summary>Publication aboutie : message opérateur récapitulant l'activation.</summary>
    public static PaPublicationResult Ok(string message) => new(true, message);

    /// <summary>Refus métier (permission, profil, compte, PA injoignable) : message opérateur + action corrective.</summary>
    public static PaPublicationResult Failure(string message) => new(false, message);
}
