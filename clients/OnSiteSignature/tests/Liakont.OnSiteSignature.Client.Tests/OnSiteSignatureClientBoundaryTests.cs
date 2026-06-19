namespace Liakont.OnSiteSignature.Client.Tests;

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

/// <summary>
/// Test de PURETÉ du client soft (ADR-0030 §2 ; INV-ONSITE-1) par liste BLANCHE (échec fermé) : le client
/// est un PUR CAPTEUR qui parle au proxy UNIQUEMENT en HTTP — il ne référence NI <c>Liakont.Agent.*</c> NI
/// un module plateforme (<c>Stratum.*</c> / <c>Liakont.Modules.*</c>). Chaque assembly référencée doit être
/// le cadre .NET Framework (identifié par son JETON DE CLÉ PUBLIQUE Microsoft — pas un simple préfixe
/// « System. »), l'assembly du client lui-même, ou une tierce EXPLICITEMENT déclarée (Newtonsoft.Json).
/// </summary>
public sealed class OnSiteSignatureClientBoundaryTests
{
    private static readonly string[] FrameworkPublicKeyTokens =
    {
        "b77a5c561934e089", // mscorlib, System, System.Core, System.Net.Http...
        "b03f5f7f11d50a3a", // System.Security, Microsoft.CSharp...
        "31bf3856ad364e35", // assemblies WPF/WCF du framework
        "cc7b13ffcd2ddd51", // netstandard (façade)
    };

    private static readonly string[] AllowedThirdPartyAssemblies =
    {
        "Newtonsoft.Json",
    };

    [Fact]
    public void Client_References_Only_BCL_Itself_And_Declared_ThirdParties()
    {
        Assembly client = typeof(OnSiteSignatureSession).Assembly;

        var leaks = client.GetReferencedAssemblies()
            .Where(r => !IsAllowed(r))
            .Select(r => r.Name)
            .ToArray();

        leaks.Should().BeEmpty(
            "le client soft de signature sur place ne référence que le cadre Microsoft, lui-même et "
            + "Newtonsoft.Json — JAMAIS Liakont.Agent.*, Stratum.* ni un module plateforme (ADR-0030 §2).");
    }

    [Fact]
    public void Client_DoesNotReference_AgentOrPlatformModules()
    {
        Assembly client = typeof(OnSiteSignatureSession).Assembly;

        client.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Should().NotContain(
                n => n.StartsWith("Liakont.Agent", StringComparison.Ordinal)
                    || n.StartsWith("Stratum.", StringComparison.Ordinal)
                    || n.StartsWith("Liakont.Modules.", StringComparison.Ordinal),
                "frontière physique : le capteur n'est pas l'agent et n'atteint aucun module (INV-ONSITE-1).");
    }

    private static bool IsAllowed(AssemblyName reference)
    {
        string? name = reference.Name;
        if (name is null)
        {
            return false;
        }

        if (name == "Liakont.OnSiteSignature.Client")
        {
            return true;
        }

        if (AllowedThirdPartyAssemblies.Contains(name))
        {
            return true;
        }

        string token = TokenToString(reference.GetPublicKeyToken());
        return FrameworkPublicKeyTokens.Contains(token, StringComparer.OrdinalIgnoreCase);
    }

    private static string TokenToString(byte[]? token)
    {
        if (token is null || token.Length == 0)
        {
            return string.Empty;
        }

        return string.Concat(token.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
