namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using System.Collections.Generic;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Remplace EN BLOC la matrice de routage des alertes du tenant courant (F12 §5.3.1, FIX212). Chaque entrée
/// est validée par le domaine (<see cref="AlertRoutingRule.Create"/>) ; la mutation est journalisée
/// (piste append-only) APRÈS le commit, SANS exposer les adresses (seul le nombre d'entrées est consigné).
/// </summary>
public sealed class SetAlertRoutingMatrixHandler : IRequestHandler<SetAlertRoutingMatrixCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetAlertRoutingMatrixHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetAlertRoutingMatrixCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = _companyFilter.GetRequiredCompanyId();

        // Construire (et donc VALIDER) toutes les entrées AVANT toute écriture : une saisie invalide
        // échoue sans muter la base. Le rang est dérivé de la position dans la liste.
        var rules = new List<AlertRoutingRule>(request.Rules.Count);
        for (var i = 0; i < request.Rules.Count; i++)
        {
            var input = request.Rules[i];
            rules.Add(AlertRoutingRule.Create(companyId, input.RuleKey, input.Severity, input.Recipients, i));
        }

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            await uow.ReplaceAlertRoutingRulesAsync(companyId, rules, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "AlertRoutingMatrix",
            companyId,
            "updated",
            $"Matrice de routage des alertes mise à jour : {rules.Count} entrée(s).",
            companyId,
            new { RuleCount = rules.Count },
            cancellationToken);
    }
}
