namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Store d'alertes en mémoire — reproduit la sémantique du store Postgres, dont l'anti-bruit : une
/// insertion est IGNORÉE s'il existe déjà une alerte active pour la même règle (équivalent de l'index
/// unique partiel + ON CONFLICT DO NOTHING). Conserve les instances (mutées par le moteur).
/// </summary>
internal sealed class InMemoryAlertStore : IAlertStore
{
    private readonly List<Alert> _alerts = new();

    public IReadOnlyList<Alert> All => _alerts;

    public IReadOnlyList<Alert> Active => _alerts.Where(a => a.IsActive).ToList();

    public Task<Alert?> FindActiveByRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
    {
        var active = _alerts.FirstOrDefault(a => a.RuleKey == ruleKey && a.IsActive);
        return Task.FromResult(active);
    }

    public Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var alert = _alerts.FirstOrDefault(a => a.Id == id);
        return Task.FromResult(alert);
    }

    public Task InsertAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // Anti-bruit : pas de doublon actif pour la même règle (ON CONFLICT DO NOTHING).
        if (_alerts.Any(a => a.RuleKey == alert.RuleKey && a.IsActive))
        {
            return Task.CompletedTask;
        }

        _alerts.Add(alert);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // Les instances sont conservées par référence : la mutation est déjà reflétée. On vérifie juste
        // la présence pour rester fidèle au contrat (mise à jour d'une alerte existante).
        if (_alerts.All(a => a.Id != alert.Id))
        {
            _alerts.Add(alert);
        }

        return Task.CompletedTask;
    }
}
