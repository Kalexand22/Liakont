namespace Liakont.Host.Alertes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
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
        var matrix = await _sender.Send(new GetAlertRoutingMatrixQuery(), cancellationToken).ConfigureAwait(false);

        // Défauts produit F12 §5.2 issus de l'UNIQUE source partagée (AlertRuleCatalog, module Supervision) :
        // le défaut AFFICHÉ et le défaut PRÉSERVÉ à l'enregistrement ne peuvent plus diverger.
        var form = new AlertesFormModel
        {
            AgentSilentHours = thresholds?.AgentSilentHours ?? AlertRuleCatalog.DefaultAgentSilentHours,
            BlockedDocumentsDays = thresholds?.BlockedDocumentsDays ?? AlertRuleCatalog.DefaultBlockedDocumentsDays,
            PaRejectionsDays = thresholds?.PaRejectionsDays ?? AlertRuleCatalog.DefaultPaRejectionsDays,
            AlertTenantContact = thresholds?.AlertTenantContact ?? false,
            ContactEmailAlerte = profile?.ContactEmailAlerte,
        };

        return new AlertesViewModel
        {
            Device = device,
            Form = form,
            ProfileExists = profile is not null,
            Routing = ToRoutingForm(matrix),
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
            MissedRunHours = existing?.MissedRunHours ?? AlertRuleCatalog.DefaultMissedRunHours,
            PushQueueMaxItems = existing?.PushQueueMaxItems ?? AlertRuleCatalog.DefaultPushQueueMaxItems,
            PushQueueMaxAgeHours = existing?.PushQueueMaxAgeHours ?? AlertRuleCatalog.DefaultPushQueueMaxAgeHours,
        };

        await _sender.Send(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveContactAsync(string? contactEmailAlerte, CancellationToken cancellationToken = default)
    {
        await _sender.Send(
            new SetAlertContactEmailCommand { ContactEmailAlerte = contactEmailAlerte },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveRoutingAsync(AlertesRoutingFormModel routing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routing);

        var rules = new List<AlertRoutingRuleInput>();
        foreach (var row in routing.Rows)
        {
            var (ruleKey, severity) = AlertesRoutingSelector.Decode(row.Selector);
            var recipients = SplitRecipients(row.RecipientsCsv);

            // Ligne entièrement vide (ajoutée puis non remplie) : ignorée silencieusement. Toute autre saisie
            // est transmise telle quelle et VALIDÉE par le domaine (sélecteur requis, destinataires valides).
            if (ruleKey is null && severity is null && recipients.Length == 0)
            {
                continue;
            }

            rules.Add(new AlertRoutingRuleInput
            {
                RuleKey = ruleKey,
                Severity = severity,
                Recipients = recipients,
            });
        }

        await _sender.Send(new SetAlertRoutingMatrixCommand { Rules = rules }, cancellationToken).ConfigureAwait(false);
    }

    private static AlertesRoutingFormModel ToRoutingForm(IReadOnlyList<AlertRoutingRuleDto> matrix)
    {
        var form = new AlertesRoutingFormModel();
        foreach (var entry in matrix)
        {
            form.Rows.Add(new AlertesRoutingRow
            {
                Selector = AlertesRoutingSelector.Encode(entry.RuleKey, entry.Severity),
                RecipientsCsv = string.Join(", ", entry.Recipients),
            });
        }

        return form;
    }

    private static string[] SplitRecipients(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
