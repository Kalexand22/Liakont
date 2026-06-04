namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests d'enregistrement de l'ancrage (TRK06) : la méthode configurée sélectionne le bon ancrage, et une
/// valeur inconnue fait ÉCHOUER le démarrage (jamais un repli silencieux sur NoAnchor).
/// </summary>
public sealed class ArchiveModuleRegistrationTests
{
    [Theory]
    [InlineData(null, typeof(NoAnchorTimestampAnchor))]
    [InlineData("None", typeof(NoAnchorTimestampAnchor))]
    [InlineData("rfc3161", typeof(Rfc3161TimestampAnchor))]
    [InlineData("OpenTimestamps", typeof(OpenTimestampsTimestampAnchor))]
    public void AddArchiveModule_SelectsAnchorByMethod(string? method, Type expected)
    {
        ServiceDescriptor descriptor = AnchorDescriptor(method);

        descriptor.ImplementationType.Should().Be(expected);
    }

    [Fact]
    public void AddArchiveModule_UnknownMethod_ThrowsAtStartup()
    {
        IConfiguration configuration = BuildConfiguration("Bogus");
        var services = new ServiceCollection();

        Action act = () => services.AddArchiveModule(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Bogus*");
    }

    private static ServiceDescriptor AnchorDescriptor(string? method)
    {
        var services = new ServiceCollection();
        services.AddArchiveModule(BuildConfiguration(method));
        return services.Single(d => d.ServiceType == typeof(ITimestampAnchor));
    }

    private static IConfiguration BuildConfiguration(string? method)
    {
        var settings = new Dictionary<string, string?>();
        if (method is not null)
        {
            settings["Archive:Anchor:Method"] = method;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
