namespace Stratum.Modules.Job.Web;

using Stratum.Common.UI.Models;

public sealed class JobNavSectionProvider : INavSectionProvider
{
    public NavSection GetSection() => new(
        Title: "Jobs",
        Icon: "bi-clock-history",
        Order: 75,
        Items:
        [
            new NavItem("Planifications", "/admin/jobs"),
            new NavItem("Exécutions", "/admin/jobs/executions"),
        ]);
}
