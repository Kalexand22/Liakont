namespace Liakont.Host.Signatures;

/// <summary>
/// Résultat d'une action de la page console des signatures (SIG10), présenté tel quel à l'opérateur.
/// <see cref="Success"/> distingue l'aboutissement d'un refus métier (permission absente, tenant non résolu,
/// demande déjà en cours, transition impossible dans l'état courant) ; <see cref="Message"/> est le message
/// opérateur en français (CLAUDE.md n°12). Aucune exception n'est propagée à la page sur un refus métier.
/// </summary>
internal sealed record SignatureActionResult(bool Success, string Message)
{
    /// <summary>Action aboutie : message opérateur.</summary>
    public static SignatureActionResult Ok(string message) => new(true, message);

    /// <summary>Refus métier : message opérateur, aucun effet.</summary>
    public static SignatureActionResult Failure(string message) => new(false, message);
}
