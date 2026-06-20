namespace Liakont.Host.PaAccounts;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using MediatR;

/// <summary>
/// Implémentation de <see cref="IPaAccountConsoleService"/> pour la page FIX01c. LECTURE via la vue
/// d'ensemble TenantSettings (<see cref="ITenantSettingsConsoleQueries.GetSettingsOverview"/> :
/// comptes AVEC capacités résolues — réutilise la composition gardée du module, garde IsRegistered +
/// dégradation si le plug-in échoue, jamais dupliquée ici) + les types de plug-ins enregistrés
/// (<see cref="IPaClientRegistry.RegisteredTypes"/>) ; MUTATIONS via les commandes TenantSettings
/// (ajout / mise à jour / désactivation) — aucune logique métier ni règle fiscale ici (chiffrement de la clé,
/// journal append-only et unicité (plug-in, environnement) : du ressort des handlers, CLAUDE.md n°2/4/10/19).
/// Pass-through pur : les exceptions métier attendues (conflit, introuvable) remontent telles quelles à la
/// page, qui les traduit en message opérateur français (CLAUDE.md n°12). La clé API saisie n'est jamais
/// journalisée par ce service (CLAUDE.md n°10).
/// </summary>
internal sealed class PaAccountConsoleService : IPaAccountConsoleService
{
    private readonly ISender _sender;
    private readonly IPaClientRegistry _registry;
    private readonly ITenantSettingsConsoleQueries _settingsQueries;

    public PaAccountConsoleService(ISender sender, IPaClientRegistry registry, ITenantSettingsConsoleQueries settingsQueries)
    {
        _sender = sender;
        _registry = registry;
        _settingsQueries = settingsQueries;
    }

    public async Task<PaAccountConsoleModel> GetModelAsync(CancellationToken cancellationToken = default)
    {
        // Comptes du tenant courant avec capacités (le service du module résout la société — jamais une
        // lecture cross-tenant, CLAUDE.md n°9). Le surplus de la vue d'ensemble (profil, fiscal, TVA) est
        // négligeable pour une page de paramétrage et évite de dupliquer la composition des capacités
        // (piège API01c : Resolve() construit un client vivant — garde + dégradation côté module).
        var overview = await _settingsQueries.GetSettingsOverview(cancellationToken).ConfigureAwait(false);
        var accounts = overview.PaAccounts;

        // Types proposés à la création = registre des plug-ins enregistrés (CLAUDE.md n°16 : jamais une liste
        // de PA concrets en dur). Triés (insensible à la casse) pour un affichage déterministe.
        var pluginTypes = _registry.RegisteredTypes
            .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PaAccountConsoleModel
        {
            Accounts = accounts,
            RegisteredPluginTypes = pluginTypes,

            // Mode d'auth par type (clé API vs OAuth2 client_id/secret), lu du registre — jamais un if (type==...).
            AuthModes = _registry.DescribeAuthModes(),
        };
    }

    public Task<Guid> CreateAsync(PaAccountFormModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Pass-through : la clé saisie (facultative) est chiffrée par le handler ; vide = aucune clé.
        var command = new AddPaAccountCommand
        {
            PluginType = model.PluginType,
            Environment = model.Environment,
            AccountIdentifiers = model.AccountIdentifiers,
            ApiKey = NullIfBlank(model.ApiKey),
            ClientId = NullIfBlank(model.ClientId),
            ClientSecret = NullIfBlank(model.ClientSecret),
            TechnicalPassword = NullIfBlank(model.TechnicalPassword),
        };

        return _sender.Send(command, cancellationToken);
    }

    public Task UpdateAsync(PaAccountFormModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.PaAccountId is not { } id)
        {
            throw new InvalidOperationException("Mise à jour d'un compte PA sans identifiant : chemin inatteignable depuis l'éditeur.");
        }

        // Clé : vide = inchangée (sémantique de la commande) ; une valeur déclenche la rotation (chiffrée par le handler).
        // Le type de plug-in n'est pas transmis : il identifie le plug-in et reste figé en édition.
        var command = new UpdatePaAccountCommand
        {
            PaAccountId = id,
            Environment = model.Environment,
            AccountIdentifiers = model.AccountIdentifiers,
            ApiKey = NullIfBlank(model.ApiKey),
            ClientId = NullIfBlank(model.ClientId),
            ClientSecret = NullIfBlank(model.ClientSecret),
            TechnicalPassword = NullIfBlank(model.TechnicalPassword),
        };

        return _sender.Send(command, cancellationToken);
    }

    public Task DeactivateAsync(Guid paAccountId, CancellationToken cancellationToken = default) =>
        _sender.Send(new DeactivatePaAccountCommand { PaAccountId = paAccountId }, cancellationToken);

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
