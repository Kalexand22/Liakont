namespace Liakont.Modules.SupportTrace.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.SupportTrace.Contracts;
using Liakont.Modules.SupportTrace.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// <c>AddSupportTraceModule</c> câble le store FileSystem et le service de purge, et applique les replis de
/// paramétrage (racine hors arbre de build, rétention au défaut F16 §10 si non/ mal renseignée).
/// </summary>
public sealed class SupportTraceModuleRegistrationTests
{
    [Fact]
    public void Registers_The_Store_And_The_Purge_Service()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SupportTrace:RootPath"] = "/srv/support-trace",
            ["SupportTrace:RetentionDays"] = "30",
        });

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetService<ISupportTraceStore>().Should().BeOfType<FileSystemSupportTraceStore>();
        scope.ServiceProvider.GetService<ISupportTracePurgeService>().Should().BeOfType<SupportTracePurgeService>();

        var options = provider.GetRequiredService<IOptions<SupportTraceOptions>>().Value;
        options.RootPath.Should().Be("/srv/support-trace");
        options.RetentionDays.Should().Be(30);
    }

    [Fact]
    public void Falls_Back_To_A_Stable_Root_And_The_Default_Retention()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SupportTrace:RetentionDays"] = "0",
        });

        var options = provider.GetRequiredService<IOptions<SupportTraceOptions>>().Value;
        options.RootPath.Should().NotBeNullOrWhiteSpace("une racine de repli stable est posée hors paramétrage explicite");
        options.RetentionDays.Should().Be(SupportTraceOptions.DefaultRetentionDays, "une rétention non positive retombe au défaut F16 §10");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddSupportTraceModule(configuration);

        return services.BuildServiceProvider();
    }
}
