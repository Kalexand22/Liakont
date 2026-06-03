namespace Liakont.Host.Navigation;

using Microsoft.Extensions.Localization;
using Stratum.Common.UI.Models;

internal sealed class HostNavSectionProvider(IStringLocalizer<HostResources> localizer) : INavSectionProvider
{
    public NavSection GetSection() => new(
        Title: localizer["Nav_Home"],
        Icon: "bi-grid-1x2",
        Order: 0,
        Items: [new NavItem(localizer["Nav_Dashboard"], "/", ExactMatch: true)]);
}
