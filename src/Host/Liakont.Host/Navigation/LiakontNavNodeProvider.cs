namespace Liakont.Host.Navigation;

using System.Collections.Generic;
using Liakont.Host.Security;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Models;

/// <summary>
/// Fournit l'arbre de navigation maître « Liakont » (WEB01, hiérarchisé au lot polish UX/UI) :
/// Documents, Encaissements, Traitements, Réconciliation (conditionnelle), Paramétrage (SOUS-MENU :
/// une entrée par élément à paramétrer + la vue d'ensemble), Supervision (conditionnelle).
/// <see cref="INavNodeProvider"/> (et non une section plate) : le shell rend les sous-menus et la
/// palette de recherche collecte les feuilles récursivement. SCOPED car la visibilité dépend du
/// tenant courant (pool PDF) et du rôle de l'utilisateur (permissions settings / supervision).
/// </summary>
internal sealed class LiakontNavNodeProvider : INavNodeProvider
{
    private readonly IPermissionService _permissions;
    private readonly ILiakontConsoleContext _console;

    public LiakontNavNodeProvider(IPermissionService permissions, ILiakontConsoleContext console)
    {
        _permissions = permissions;
        _console = console;
    }

    public NavNode GetNavNode()
    {
        var children = new List<NavNode>
        {
            new() { Label = "Documents", Href = "/documents" },
            new() { Label = "Encaissements", Href = "/encaissements" },
            new() { Label = "Traitements", Href = "/traitements" },
        };

        // Réconciliation : visible uniquement si l'agent du tenant alimente un pool de PDF non rattachés. Le
        // nombre d'éléments en attente (propositions + orphelins) est embarqué dans le libellé — le modèle
        // NavNode du socle vendored n'a pas de champ « badge » et n'est pas modifié (CLAUDE.md n°11). Le compteur
        // n'est montré qu'aux OPÉRATEURS (liakont.actions) : la file de réconciliation est une fonction opérateur
        // (l'endpoint API04 renvoie 403 à un simple lecteur). On garde l'affichage AU RENDU (permissions chargées),
        // pas au calcul du contexte (ouverture de circuit : les claims de permission ne sont pas garantis chargés).
        if (_console.ReconciliationAvailable)
        {
            var pending = _console.ReconciliationPendingCount;
            var showCount = pending > 0 && _permissions.HasPermission(LiakontPermissions.Actions);
            var label = showCount ? $"Réconciliation ({pending})" : "Réconciliation";
            children.Add(new NavNode { Label = label, Href = "/reconciliation" });
        }

        children.Add(BuildParametrageNode());

        // Supervision : réservée au superviseur (vues cross-tenant en lecture seule, module Supervision).
        if (_permissions.HasPermission(LiakontPermissions.Supervision))
        {
            children.Add(new NavNode { Label = "Supervision", Href = "/supervision" });
        }

        // Flotte : méta-supervision cross-INSTANCE réservée à IT Innovations (OPS04). Le niveau AU-DESSUS de
        // la supervision (qui est cross-tenant DANS une instance) ; la page /flotte refuse l'accès sans la permission.
        if (_permissions.HasPermission(LiakontPermissions.Fleet))
        {
            children.Add(new NavNode { Label = "Flotte", Href = "/flotte" });
        }

        return new NavNode
        {
            Label = "Liakont",
            Icon = "bi-receipt",
            Order = 5,
            Children = children,
        };
    }

    /// <summary>
    /// Nœud « Paramétrage » : SOUS-MENU pour les porteurs de <c>liakont.settings</c> (une entrée par
    /// élément à paramétrer — les pages cibles refusent de toute façon l'accès sans la permission),
    /// simple lien vers la vue d'ensemble sinon (le hub /parametrage reste consultable en lecture).
    /// </summary>
    private NavNode BuildParametrageNode()
    {
        if (!_permissions.HasPermission(LiakontPermissions.Settings))
        {
            return new NavNode { Label = "Paramétrage", Href = "/parametrage" };
        }

        // ExactMatch sur la vue d'ensemble : seule la route exacte /parametrage l'active (les
        // sous-pages activent leur propre feuille — surbrillance exclusive FIX209).
        return new NavNode
        {
            Label = "Paramétrage",
            Children =
            [
                new NavNode { Label = "Vue d'ensemble", Href = "/parametrage", ExactMatch = true },
                new NavNode { Label = "Paramètres fiscaux", Href = "/parametrage/fiscal" },
                new NavNode { Label = "Table TVA", Href = "/parametrage/table-tva" },
                new NavNode { Label = "Comptes PA", Href = "/parametrage/comptes-pa" },
                new NavNode { Label = "Alertes & supervision", Href = "/parametrage/alertes" },

                // « Agents d'extraction » (pas « Agents ») : la nav Stratum a déjà une entrée « Agents »
                // (/admin/agents, utilisateurs de la console) — libellé socle non modifiable (CLAUDE.md n°11).
                // Déplacé SOUS Paramétrage (gestion de secrets = geste de paramétrage, même garde settings).
                new NavNode { Label = "Agents d'extraction", Href = "/agents" },
            ],
        };
    }
}
