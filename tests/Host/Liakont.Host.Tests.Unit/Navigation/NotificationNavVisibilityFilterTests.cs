namespace Liakont.Host.Tests.Unit.Navigation;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Navigation;
using Liakont.Host.Security;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Notification.Web;
using Xunit;

/// <summary>
/// Tests du filtre de visibilité Liakont pour la section « Notifications » du socle (FIX209, décision E5).
/// Le provider socle déclare SEPT entrées ; on n'en garde QU'UNE (« Templates »), gardée par
/// <see cref="LiakontPermissions.Settings"/>. Anti-faux-vert : la section est réellement RÉDUITE à Templates
/// (Règles de routage / Webhooks / Simulation / SLA / Services / Integrations réellement absents), réellement
/// présente AVEC liakont.settings et réellement vide sans elle (donc omise par BuildNavTree).
/// </summary>
public sealed class NotificationNavVisibilityFilterTests
{
    private const string TemplatesHref = "/admin/notifications/templates";

    [Fact]
    public void GetSection_With_Settings_Keeps_Only_Templates()
    {
        var section = new NotificationNavVisibilityFilter(
            new StubNotificationLocalizer(),
            new FakePermissionService([LiakontPermissions.Settings])).GetSection();

        section.Items.Should().ContainSingle("seule l'entrée Templates est conservée");
        section.Items.Single().Href.Should().Be(TemplatesHref);

        // Les six autres entrées socle ne doivent plus figurer dans la nav Liakont.
        var hrefs = section.Items.Select(i => i.Href).ToList();
        hrefs.Should().NotContain("/admin/notifications/routing", "« Règles de routage » est supprimé (E5)");
        hrefs.Should().NotContain("/admin/notifications/webhooks");
        hrefs.Should().NotContain("/admin/notifications/preview");
        hrefs.Should().NotContain("/admin/sla");
        hrefs.Should().NotContain("/admin/catalog/services");
        hrefs.Should().NotContain("/admin/integrations");
    }

    [Fact]
    public void GetSection_Without_Settings_Hides_The_Whole_Section()
    {
        var section = new NotificationNavVisibilityFilter(
            new StubNotificationLocalizer(),
            new FakePermissionService([])).GetSection();

        // Section vidée → omise par BuildNavTree (sections à 0 item) : lecture/operateur ne voient rien.
        section.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(new string[0], false)] // lecture : pas de settings
    [InlineData(new[] { "liakont.read" }, false)] // operateur (read+actions) : pas de settings
    [InlineData(new[] { "liakont.settings" }, true)] // parametrage / superviseur : settings
    public void GetSection_Visible_Exactly_When_Role_Grants_Settings(string[] permissions, bool expectVisible)
    {
        var section = new NotificationNavVisibilityFilter(
            new StubNotificationLocalizer(),
            new FakePermissionService(permissions)).GetSection();

        if (expectVisible)
        {
            section.Items.Select(i => i.Href).Should().Contain(TemplatesHref);
        }
        else
        {
            section.Items.Should().BeEmpty();
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly HashSet<string> _permissions;

        public FakePermissionService(string[] permissions) =>
            _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _permissions.Contains(permission);
    }

    private sealed class StubNotificationLocalizer : IStringLocalizer<NotificationResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
