namespace Liakont.Host.Navigation;

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Stratum.Common.UI.Models;

internal sealed class HostNavSectionProvider(
    IStringLocalizer<HostResources> localizer,
    ILiakontConsoleContext console,
    IHttpContextAccessor httpContextAccessor) : INavSectionProvider
{
    public NavSection GetSection()
    {
        // RB1 — « Accueil › Tableau de bord » est tenant-scopé (cartes du tenant courant : documents,
        // table TVA…). Masqué pour un super-admin cross-tenant (section sans item → omise par le shell) ;
        // il est redirigé vers la Supervision (vue d'ensemble cross-tenant) par Home.razor.
        IReadOnlyList<NavItem> items = CrossTenantDetection.IsCrossTenant(console, httpContextAccessor)
            ? []
            : [new NavItem(localizer["Nav_Dashboard"], "/", ExactMatch: true)];

        return new(
            Title: localizer["Nav_Home"],
            Icon: "bi-grid-1x2",
            Order: 0,
            Items: items);
    }
}
