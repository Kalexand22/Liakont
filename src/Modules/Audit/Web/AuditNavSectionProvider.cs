namespace Stratum.Modules.Audit.Web;

using Stratum.Common.UI.Models;

public sealed class AuditNavSectionProvider : INavSectionProvider
{
    public NavSection GetSection() => new(
        Title: "Audit",
        Icon: "bi-journal-text",
        Order: 70,
        Items:
        [
            new NavItem("Journal d'audit", "/admin/audit"),
            new NavItem("Politiques", "/admin/audit/policies"),
        ]);
}
