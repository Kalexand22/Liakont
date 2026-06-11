namespace Liakont.Host.PaAccounts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Onboarding de la transmission depuis la console (FIX201, décision E1) : expose l'état de publication du
/// SIREN / du <c>tax_report_setting</c> du compte Plateforme Agréée ACTIF, et permet de PUBLIER (activer la
/// transmission) en appelant <c>IPaClient.EnsureTaxReportSettingAsync</c> avec les données du profil tenant
/// et la saisie opérateur — AUCUNE valeur fiscale inventée (CLAUDE.md n°2/7). Sans publication, le
/// diagnostic pré-envoi (F04 §3.1) refuse tout envoi (« Transport not available ») : c'est le verrou que
/// cette action lève. TENANT-SCOPÉ (le contexte d'acteur EST le tenant) ; l'action est gardée par
/// <c>liakont.settings</c> et TRACÉE (audit append-only). Isole l'orchestration hors de la page Blazor
/// (la page reste présentationnelle — CLAUDE.md n°19).
/// </summary>
internal interface IPaPublicationConsoleService
{
    /// <summary>
    /// État de publication du compte PA actif du tenant courant (lecture défensive : si l'état ne peut être
    /// relu auprès de la PA, <see cref="PaPublicationState.StateAvailable"/> = <c>false</c> sans lever).
    /// </summary>
    Task<PaPublicationState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publie le SIREN / active la transmission du compte PA actif : garde <c>liakont.settings</c>, construit
    /// la demande à partir du profil (SIREN → <c>cin_scheme « 0002 »</c>) et de la saisie, appelle
    /// <c>EnsureTaxReportSettingAsync</c> (idempotent), trace l'opération, et renvoie un message opérateur.
    /// </summary>
    Task<PaPublicationResult> PublishAsync(PaPublicationFormModel form, CancellationToken cancellationToken = default);
}
