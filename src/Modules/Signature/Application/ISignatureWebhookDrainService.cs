namespace Liakont.Modules.Signature.Application;

/// <summary>
/// Service de DRAIN de l'inbox de webhooks de signature pour le tenant COURANT (ADR-0029 §5). Exécuté en
/// asynchrone par un <c>ITenantJob</c> (fan-out <c>TenantJobRunner</c>, SOL06) : il lit les événements non
/// traités, télécharge la preuve via le fournisseur, et la RAPATRIE dans le coffre WORM via
/// <c>Archive.Contracts</c> (jamais <c>Archive.Domain</c>, jamais le plug-in — INV-YOUSIGN-6). Idempotent :
/// un événement déjà traité (ou rejoué) est sans effet.
/// </summary>
public interface ISignatureWebhookDrainService
{
    /// <summary>Draine les webhooks en attente du tenant courant. Renvoie le nombre d'événements traités.</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<int> DrainAsync(CancellationToken cancellationToken = default);
}
