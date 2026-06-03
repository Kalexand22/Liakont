namespace Stratum.Modules.Identity.Web;

using Stratum.Common.UI.Models;

public sealed class IdentityNavSectionProvider : INavSectionProvider
{
    public NavSection GetSection() => new(
        Title: "Annuaire",
        Icon: "bi-people",
        Order: 20,
        Items:
        [
            new NavItem("Agents", "/admin/agents"),
            new NavItem("Équipes", "/admin/agents/teams"),
            new NavItem("Délégations", "/admin/agents/delegations"),
        ]);
}
