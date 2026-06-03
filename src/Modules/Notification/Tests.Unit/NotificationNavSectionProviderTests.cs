namespace Stratum.Modules.Notification.Tests.Unit;

using FluentAssertions;
using Microsoft.Extensions.Localization;
using Stratum.Modules.Notification.Web;
using Xunit;

public sealed class NotificationNavSectionProviderTests
{
    [Fact]
    public void GetSection_ReturnsNotificationsSection_WithSlaItem()
    {
        var localizer = new StubLocalizer();

        var provider = new NotificationNavSectionProvider(localizer);
        var section = provider.GetSection();

        section.Title.Should().Be("Nav_Notifications");
        section.Icon.Should().Be("bi-bell");
        section.Order.Should().Be(50);
        section.Items.Should().HaveCount(7);
        section.Items[0].Label.Should().Be("Nav_Templates");
        section.Items[0].Href.Should().Be("/admin/notifications/templates");
        section.Items[1].Label.Should().Be("Nav_RoutingRules");
        section.Items[1].Href.Should().Be("/admin/notifications/routing");
        section.Items[2].Label.Should().Be("Nav_Webhooks");
        section.Items[2].Href.Should().Be("/admin/notifications/webhooks");
        section.Items[3].Label.Should().Be("Nav_Simulation");
        section.Items[3].Href.Should().Be("/admin/notifications/preview");
        section.Items[4].Label.Should().Be("Nav_Sla");
        section.Items[4].Href.Should().Be("/admin/sla");
        section.Items[5].Label.Should().Be("Nav_Services");
        section.Items[5].Href.Should().Be("/admin/catalog/services");
        section.Items[6].Label.Should().Be("Nav_Integrations");
        section.Items[6].Href.Should().Be("/admin/integrations");
    }

    private sealed class StubLocalizer : IStringLocalizer<NotificationResources>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
