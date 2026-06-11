namespace Liakont.Host.Tests.Unit.Startup;

using FluentAssertions;
using Liakont.Host.Startup;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// Couvre la construction des options ForwardedHeaders de l'appliance (reverse proxy, F12 §6.2/6.6).
/// Point sensible : la confiance par défaut (loopback) DOIT être vidée et seuls les réseaux/proxys
/// déclarés sont de confiance (sinon X-Forwarded-For est usurpable, IP de rate-limit/redirect_uri
/// faussés). Une valeur invalide doit lever AU DÉMARRAGE (échec visible), pas silencieusement.
/// </summary>
public sealed class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void Disabled_When_Section_Empty_Returns_Null()
    {
        ForwardedHeadersConfiguration.Build(Section()).Should().BeNull();
    }

    [Fact]
    public void Enabled_False_Returns_Null()
    {
        ForwardedHeadersConfiguration.Build(Section(("Enabled", "false"))).Should().BeNull();
    }

    [Fact]
    public void Enabled_Sets_Proto_Host_For_And_Clears_Default_Loopback_Trust()
    {
        var options = ForwardedHeadersConfiguration.Build(Section(
            ("Enabled", "true"),
            ("KnownNetworks:0", "172.28.0.0/16")));

        options.Should().NotBeNull();
        options!.ForwardedHeaders.Should().Be(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);

        // SEUL le réseau déclaré est de confiance — la confiance loopback par défaut est vidée.
        options.KnownIPNetworks.Should().ContainSingle()
            .Which.Should().Be(System.Net.IPNetwork.Parse("172.28.0.0/16"));
        options.KnownProxies.Should().BeEmpty();
    }

    [Fact]
    public void Enabled_Adds_Declared_Proxy_Only()
    {
        var options = ForwardedHeadersConfiguration.Build(Section(
            ("Enabled", "true"),
            ("KnownProxies:0", "10.0.0.5")));

        options.Should().NotBeNull();
        options!.KnownProxies.Should().ContainSingle()
            .Which.Should().Be(System.Net.IPAddress.Parse("10.0.0.5"));
        options.KnownIPNetworks.Should().BeEmpty();
    }

    [Fact]
    public void Malformed_Cidr_Throws_At_Build_Time()
    {
        var act = () => ForwardedHeadersConfiguration.Build(Section(
            ("Enabled", "true"),
            ("KnownNetworks:0", "not-a-cidr")));

        act.Should().Throw<System.FormatException>();
    }

    private static IConfigurationSection Section(params (string Key, string Value)[] values)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            dict[$"ForwardedHeaders:{key}"] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build()
            .GetSection("ForwardedHeaders");
    }
}
