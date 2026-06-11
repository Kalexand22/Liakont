namespace Liakont.Host.Alertes;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;

/// <summary>
/// Implémentation de <see cref="IAlertesConsoleService"/> (FIX210). LECTURE : dispositif via le Contract
/// Supervision (<see cref="IAlertDeviceQueries"/>) + seuils et profil via les queries TenantSettings (tenant
/// résolu par les handlers). MUTATIONS : commandes TenantSettings (seuils, contact). Aucune logique métier ni
/// règle fiscale ici (journal append-only et validation : du ressort des handlers — CLAUDE.md n°2/4/19).
/// Les seuils des règles GELÉES (non éditables) sont préservés : relus puis réémis tels quels (repli sur les
/// défauts F12 §5.2 si le tenant n'a pas encore de seuils).
/// </summary>
internal sealed class AlertesConsoleService : IAlertesConsoleService
{
    // Défauts produit F12 §5.2 (dupliqués ici car la valeur du Domain TenantSettings n'est pas accessible hors
    // de ce module — même pattern que les règles SUP01b ; la source reste F12 §5.2). Utilisés pour pré-remplir
    // le formulaire et préserver les seuils gelés quand le tenant n'a pas encore de ligne de seuils.
    private const int DefaultAgentSilentHours = 24;
    private const int DefaultMissedRunHours = 36;
    private const int DefaultPushQueueMaxItems = 50;
    private const int DefaultPushQueueMaxAgeHours = 6;
    private const int DefaultBlockedDocumentsDays = 5;
    private const int DefaultPaRejectionsDays = 2;

    private readonly ISender _sender;
    private readonly IAlertDeviceQueries _deviceQueries;

    public AlertesConsoleService(ISender sender, IAlertDeviceQueries deviceQueries)
    {
        _sender = sender;
        _deviceQueries = deviceQueries;
    }

    public async Task<AlertesViewModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var device = await _deviceQueries.GetDeviceStatusAsync(cancellationToken).ConfigureAwait(false);
        var thresholds = await _sender.Send(new GetAlertThresholdsQuery(), cancellationToken).ConfigureAwait(false);
        var profile = await _sender.Send(new GetTenantProfileQuery(), cancellationToken).ConfigureAwait(false);

        var form = new AlertesFormModel
        {
            AgentSilentHours = thresholds?.AgentSilentHours ?? DefaultAgentSilentHours,
            BlockedDocumentsDays = thresholds?.BlockedDocumentsDays ?? DefaultBlockedDocumentsDays,
            PaRejectionsDays = thresholds?.PaRejectionsDays ?? DefaultPaRejectionsDays,
            AlertTenantContact = thresholds?.AlertTenantContact ?? false,
            ContactEmailAlerte = profile?.ContactEmailAlerte,
        };

        return new AlertesViewModel
        {
            Device = device,
            Form = form,
            ProfileExists = profile is not null,
        };
    }

    public async Task SaveThresholdsAsync(AlertesThresholdInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Préserver les seuils des règles gelées (non éditables) : relire l'existant, sinon défauts F12 §5.2.
        var existing = await _sender.Send(new GetAlertThresholdsQuery(), cancellationToken).ConfigureAwait(false);

        var command = new SetAlertThresholdsCommand
        {
            AgentSilentHours = input.AgentSilentHours,
            BlockedDocumentsDays = input.BlockedDocumentsDays,
            PaRejectionsDays = input.PaRejectionsDays,
            AlertTenantContact = input.AlertTenantContact,
            MissedRunHours = existing?.MissedRunHours ?? DefaultMissedRunHours,
            PushQueueMaxItems = existing?.PushQueueMaxItems ?? DefaultPushQueueMaxItems,
            PushQueueMaxAgeHours = existing?.PushQueueMaxAgeHours ?? DefaultPushQueueMaxAgeHours,
        };

        await _sender.Send(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveContactAsync(string? contactEmailAlerte, CancellationToken cancellationToken = default)
    {
        await _sender.Send(
            new SetAlertContactEmailCommand { ContactEmailAlerte = contactEmailAlerte },
            cancellationToken).ConfigureAwait(false);
    }
}
