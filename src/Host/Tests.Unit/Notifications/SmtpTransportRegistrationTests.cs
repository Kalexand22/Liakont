namespace Liakont.Host.Tests.Unit.Notifications;

using FluentAssertions;
using Liakont.Host.InstanceEmail;
using Liakont.Host.Notifications;
using Liakont.Host.Tests.Unit.InstanceEmail;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.TenantSettings.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Infrastructure;
using Xunit;

/// <summary>
/// Garde anti-régression du cœur du livrable SUP03 : le transport SMTP réel REMPLACE bien le
/// <c>StubEmailTransport</c> enregistré par <c>AddNotificationModule</c>. Si une évolution future du module
/// changeait l'enregistrement de <see cref="IEmailTransport"/> (TryAdd, double enregistrement, ordre), ce test
/// échouerait au lieu de retomber silencieusement sur le stub (emails qui ne partent plus, sans rien casser).
/// </summary>
public sealed class SmtpTransportRegistrationTests
{
    [Fact]
    public void Smtp_Transport_Replaces_Stub_From_Notification_Module()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationModule();
        services.Configure<SmtpOptions>(_ => { });

        // Dépendances du transport provider-aware (ADR-0039) : store de config d'instance + déchiffrement +
        // fournisseur de jeton OAuth. Enregistrées ici (comme AddFleetSupervisionModule / TenantSettings / Host).
        services.AddSingleton<IInstanceEmailConfigStore>(new FakeInstanceEmailConfigStore());
        services.AddSingleton<ISecretProtector>(new FakeSecretProtector());
        services.AddSingleton<IEmailOAuthTokenProvider>(new FakeEmailOAuthTokenProvider());

        // Reproduit l'enregistrement du composition root (AppBootstrap) : Replace du stub par le vrai
        // transport, et disponibilité d'envoi (BUG-31) servie par la MÊME instance scoped.
        services.AddScoped<SmtpEmailTransport>();
        services.Replace(ServiceDescriptor.Scoped<IEmailTransport>(sp => sp.GetRequiredService<SmtpEmailTransport>()));
        services.AddScoped<IEmailSendAvailability>(sp => sp.GetRequiredService<SmtpEmailTransport>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        var availability = scope.ServiceProvider.GetRequiredService<IEmailSendAvailability>();

        transport.Should().BeOfType<SmtpEmailTransport>();
        availability.Should().BeSameAs(transport, "la garde du provisioning interroge le transport RÉEL (config en base OU appsettings — BUG-31)");
    }
}
