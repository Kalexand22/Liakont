namespace Liakont.SignatureProviders.Yousign.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Gardes de frontière du plug-in Yousign (INV-YOUSIGN-2 ; CLAUDE.md n°6/14/16) : il ne référence QUE
/// <c>Signature.Contracts</c> (+ Common) ; jamais un autre plug-in, un module métier, <c>Notification.Domain</c>
/// (WebhookSignature) ou <c>Archive.Domain</c> ; aucun type « fil » Yousign exposé hors de l'assembly. Vérifié
/// au niveau IL (réflexion + NetArchTest) ET déclaratif (scan du .csproj).
/// </summary>
public sealed class YousignBoundaryTests
{
    private static readonly Assembly PluginAssembly = typeof(YousignSignatureProviderFactory).Assembly;

    [Fact]
    public void Plugin_does_not_reference_any_other_signature_provider_or_pa_plugin()
    {
        ReferencedAssemblyNames(PluginAssembly)
            .Should().NotContain(
                name => (name.StartsWith("Liakont.SignatureProviders", StringComparison.Ordinal)
                         && !name.StartsWith("Liakont.SignatureProviders.Yousign", StringComparison.Ordinal))
                        || name.StartsWith("Liakont.PaClients", StringComparison.Ordinal),
                "un plug-in ne référence JAMAIS un autre plug-in (module-rules §6, CLAUDE.md n°6/16)");
    }

    [Fact]
    public void Plugin_only_references_the_signature_contracts_module()
    {
        ReferencedAssemblyNames(PluginAssembly)
            .Should().NotContain(
                name => name.StartsWith("Liakont.Modules.", StringComparison.Ordinal)
                        && !string.Equals(name, "Liakont.Modules.Signature.Contracts", StringComparison.Ordinal),
                "un plug-in de signature ne référence que Signature.Contracts (module-rules §6)");
    }

    [Fact]
    public void Plugin_does_not_reach_into_notification_domain_or_archive_domain()
    {
        var result = Types.InAssembly(PluginAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Stratum.Modules.Notification.Domain",
                "Liakont.Modules.Archive.Domain",
                "Liakont.Modules.Signature.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "le plug-in calcule son HMAC en interne et n'atteint aucun Domain vendored — {0}",
            DescribeFailures(result));
    }

    [Fact]
    public void Plugin_csproj_only_declares_signature_contracts_as_project_reference()
    {
        var pluginRoot = FindPluginRoot();
        var csproj = Path.Combine(pluginRoot, "Liakont.SignatureProviders.Yousign.csproj");
        File.Exists(csproj).Should().BeTrue("le .csproj du plug-in doit être localisable depuis le répertoire de test");

        const string allowedSuffix =
            "/src/Modules/Signature/Contracts/Liakont.Modules.Signature.Contracts.csproj";

        var resolvedRefs = XDocument.Load(csproj).Descendants("ProjectReference")
            .Attributes("Include").Select(a => a.Value)
            .Select(include => Path.GetFullPath(Path.Combine(pluginRoot, include)).Replace('\\', '/'))
            .ToArray();

        resolvedRefs.Should().NotBeEmpty("le plug-in référence au moins Signature.Contracts");
        resolvedRefs.Should().OnlyContain(
            r => r.EndsWith(allowedSuffix, StringComparison.OrdinalIgnoreCase),
            "un plug-in de signature ne déclare en ProjectReference que Signature.Contracts (CLAUDE.md n°6/16)");
    }

    [Fact]
    public void No_proprietary_yousign_wire_type_is_exposed_outside_the_assembly()
    {
        var exported = PluginAssembly.GetExportedTypes().Select(t => t.FullName ?? string.Empty).ToArray();

        exported.Should().NotContain(
            name => name.StartsWith("Liakont.SignatureProviders.Yousign.Wire", StringComparison.Ordinal),
            "aucun DTO « fil » Yousign n'est exposé hors de l'assembly (INV-YOUSIGN-2)");

        exported.Should().NotContain(
            "Liakont.SignatureProviders.Yousign.YousignSignatureProvider",
            "l'implémentation d'ISignatureProvider reste interne, exposée seulement derrière l'abstraction");
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static string DescribeFailures(TestResult result) =>
        result.IsSuccessful ? string.Empty : string.Join(", ", result.FailingTypeNames ?? []);

    private static string FindPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "src", "Liakont.sln");
            if (File.Exists(sln))
            {
                return Path.Combine(dir.FullName, "src", "SignatureProviders", "Liakont.SignatureProviders.Yousign");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
