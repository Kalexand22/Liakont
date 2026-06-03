namespace Stratum.Modules.Identity.Web;

using Stratum.Common.UI.Models;

public sealed class SecurityNavSectionProvider : INavSectionProvider
{
    public NavSection GetSection() => new(
        Title: "Sécurité",
        Icon: "bi-shield-lock",
        Order: 25,
        Items:
        [
            new NavItem("Utilisateurs", "/admin/identity/users"),
            new NavItem("Rôles", "/admin/roles"),
        ]);
}
