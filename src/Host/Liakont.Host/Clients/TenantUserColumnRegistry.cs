namespace Liakont.Host.Clients;

using Liakont.Host.Security.Abstractions;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Colonnes de l'écran « Utilisateurs » d'un client (RB4), pilotant <c>DeclaredListPage</c> (tri,
/// recherche, export) — aucune grille « maison » (mémoire console-web : DeclaredListPage obligatoire).
/// Les rôles et le statut Actif/Désactivé sont rendus par les ColumnTemplates de la page.
/// </summary>
internal sealed class TenantUserColumnRegistry : ColumnRegistryBase<TenantUserLine>
{
    protected override void Configure()
    {
        Column("Username", "Identifiant", "Utilisateur", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("DisplayName", "Nom", "Utilisateur", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Email", "E-mail", "Utilisateur", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("Roles", "Rôles", "Utilisateur", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column("Enabled", "Statut", "Utilisateur", ColumnDataType.Text, defaultVisible: true, sortOrder: 4);
    }
}
