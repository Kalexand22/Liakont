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
        // NavItem du socle vendored n'a pas de champ « badge » et n'est pas modifié (CLAUDE.md n°11). Le compteur
        // n'est montré qu'aux OPÉRATEURS (liakont.actions) : la file de réconciliation est une fonction opérateur
        // (l'endpoint API04 renvoie 403 à un simple lecteur). On garde l'affichage AU RENDU (permissions chargées),
        // pas au calcul du contexte (ouverture de circuit : les claims de permission ne sont pas garantis chargés).
        if (_console.ReconciliationAvailable)
        {
            var pending = _console.ReconciliationPendingCount;
            var showCount = pending > 0 && _permissions.HasPermission(LiakontPermissions.Actions);
            var label = showCount ? $"Réconciliation ({pending})" : "Réconciliation";
            items.Add(new NavItem(label, "/reconciliation"));
        }

        items.Add(new NavItem("Paramétrage", "/parametrage"));

        // Gestion des agents (parc + clés API, WEB09) : réservée au paramétrage (gestion de secrets). Le lien
        // n'apparaît qu'aux porteurs de liakont.settings — la page elle-même refuse l'accès sans cette
        // permission. La console dispatche les commandes PIV05 IN-PROCESS : la garde liakont.settings du
        // chemin console est donc portée par la PAGE (côté serveur, circuit Blazor) ; les endpoints API05,
        // eux, portent la garde du chemin HTTP (RequireAuthorization). Les handlers PIV05 n'imposent que le
        // scope tenant.
        if (_permissions.HasPermission(LiakontPermissions.Settings))
        {
            // « Agents d'extraction » (pas « Agents ») : la nav Stratum a déjà une entrée « Agents »
            // (/admin/agents, utilisateurs de la console) — libellé socle non modifiable (CLAUDE.md n°11).
            items.Add(new NavItem("Agents d'extraction", "/agents"));
        }

        // Supervision : réservée au superviseur (vues cross-tenant en lecture seule, module Supervision).
        if (_permissions.HasPermission(LiakontPermissions.Supervision))
        {
            items.Add(new NavItem("Supervision", "/supervision"));
        }

        // Flotte : méta-supervision cross-INSTANCE réservée à IT Innovations (OPS04). Le niveau au-dessus de
        // la supervision (qui est cross-tenant DANS une instance) ; la page refuse l'accès sans la permission.
        if (_permissions.HasPermission(LiakontPermissions.Fleet))
        {
            items.Add(new NavItem("Flotte", "/flotte"));
        }

        return new NavSection(
            Title: "Liakont",
            Icon: "bi-receipt",
            Order: 5,
            Items: items);
    }
}
