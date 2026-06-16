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
        var children = new List<NavNode>();

        // Documents / Encaissements / Traitements : surfaces de CONSULTATION (liakont.read). La matrice §3
        // (identity-permissions-liakont.md : read = « documents, transmissions, journaux ») et le guide opérateur
        // §17 (lecture consulte « les traitements », journal en lecture seule) classent le journal des traitements
        // en lecture ; l'endpoint backing le confirme (GET /runs → liakont.read ; seul POST /runs/trigger, l'action,
        // = liakont.actions). Gardées par liakont.read (finding F5a / RLF03) : un principal sans read (ex. exploitant
        // de flotte) ne les voit pas, et les pages portent la même policy. Le super-admin court-circuite.
        if (_permissions.HasPermission(LiakontPermissions.Read))
        {
            children.Add(new() { Label = "Documents", Href = "/documents" });
            children.Add(new() { Label = "Encaissements", Href = "/encaissements" });
            children.Add(new() { Label = "Traitements", Href = "/traitements" });
        }

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

        var parametrage = BuildParametrageNode();
        if (parametrage is not null)
        {
            children.Add(parametrage);
        }

        // Supervision : réservée au superviseur — SOUS-MENU (même pattern que Paramétrage) :
        // « Vue d'ensemble » (santé cross-tenant, lecture seule) et « Clients » (administration
        // d'instance OPS03 : création/suspension de tenants). ExactMatch sur la vue d'ensemble :
        // /supervision/{tenantId} existe (la surbrillance plus-long-préfixe ferait double emploi).
        if (_permissions.HasPermission(LiakontPermissions.Supervision))
        {
            children.Add(new NavNode
            {
                Label = "Supervision",
                Children =
                [
                    new NavNode { Label = "Vue d'ensemble", Href = "/supervision", ExactMatch = true },
                    new NavNode { Label = "Clients", Href = "/clients" },
                ],
            });
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
    /// Nœud « Paramétrage » (finding F5a / RLF03, contraint par FIX208) :
    /// <list type="bullet">
    /// <item><c>null</c> sans <c>liakont.read</c> (ex. exploitant de flotte) — la page <c>/parametrage</c>
    /// exige <c>liakont.read</c>, le trou « ouvrable par tout authentifié » est fermé.</item>
    /// <item>simple lien vers le hub <c>/parametrage</c> pour un porteur de <c>liakont.read</c> sans
    /// <c>liakont.settings</c> : le hub reste accessible au lecteur pour l'export d'audit par période
    /// (FIX208, capacité <c>liakont.read</c> — la masquer régresserait cette capacité d'audit).</item>
    /// <item>SOUS-MENU (une entrée par élément à paramétrer) pour un porteur de <c>liakont.settings</c> ;
    /// les sous-pages sont gardées par <c>[Authorize(Policy = liakont.settings)]</c>.</item>
    /// </list>
    /// </summary>
    private NavNode? BuildParametrageNode()
    {
        var hasSettings = _permissions.HasPermission(LiakontPermissions.Settings);

        // Visible au porteur de liakont.read (hub + export d'audit FIX208) OU de liakont.settings (paramétrage).
        // Caché sinon (ex. exploitant de flotte) : le trou « ouvrable par tout authentifié » est fermé.
        if (!hasSettings && !_permissions.HasPermission(LiakontPermissions.Read))
        {
            return null;
        }

        if (!hasSettings)
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
