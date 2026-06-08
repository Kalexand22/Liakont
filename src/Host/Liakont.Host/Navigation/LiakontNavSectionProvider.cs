namespace Liakont.Host.Navigation;

using System.Collections.Generic;
using Liakont.Host.Security;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Models;

/// <summary>
/// Fournit la section de navigation maître « Liakont » (WEB01) : Documents, Encaissements, Traitements,
/// Réconciliation (conditionnelle), Paramétrage, Supervision (conditionnelle). SCOPED car la visibilité
/// dépend du tenant courant (pool PDF) et du rôle de l'utilisateur (permission de supervision). Les
/// pages cibles sont livrées par les items WEB suivants ; WEB01 pose la structure de navigation.
/// </summary>
internal sealed class LiakontNavSectionProvider : INavSectionProvider
{
    private readonly IPermissionService _permissions;
    private readonly ILiakontConsoleContext _console;

    public LiakontNavSectionProvider(IPermissionService permissions, ILiakontConsoleContext console)
    {
        _permissions = permissions;
        _console = console;
    }

    public NavSection GetSection()
    {
        var items = new List<NavItem>
        {
            new("Documents", "/documents"),
            new("Encaissements", "/encaissements"),
            new("Traitements", "/traitements"),
        };

        // Réconciliation : visible uniquement si l'agent du tenant alimente un pool de PDF non rattachés. Le
        // nombre d'éléments en attente (propositions + orphelins) est embarqué dans le libellé — le modèle
        // NavItem du socle vendored n'a pas de champ « badge » et n'est pas modifié (CLAUDE.md n°11).
        if (_console.ReconciliationAvailable)
        {
            var pending = _console.ReconciliationPendingCount;
            var label = pending > 0 ? $"Réconciliation ({pending})" : "Réconciliation";
            items.Add(new NavItem(label, "/reconciliation"));
        }

        items.Add(new NavItem("Paramétrage", "/parametrage"));

        // Supervision : réservée au superviseur (vues cross-tenant en lecture seule, module Supervision).
        if (_permissions.HasPermission(LiakontPermissions.Supervision))
        {
            items.Add(new NavItem("Supervision", "/supervision"));
        }

        return new NavSection(
            Title: "Liakont",
            Icon: "bi-receipt",
            Order: 5,
            Items: items);
    }
}
