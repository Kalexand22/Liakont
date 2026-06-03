namespace Liakont.Agent.Core.Tests;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Xunit;

/// <summary>
/// Vérifie la pureté du contrat partagé agent vers plateforme : Liakont.Agent.Contracts ne
/// dépend que du BCL (acceptance SOL02 « zéro dépendance hors BCL », blueprint.md §3.2).
/// Vérification au niveau IL (assembly réelle) plutôt que sur le .csproj : un analyseur
/// (PrivateAssets=all) n'introduit aucune dépendance d'assembly, donc cette règle reste
/// vraie même si le Directory.Build.props racine ajoute StyleCop.
/// </summary>
public class ContractsPurityTests
{
    [Fact]
    public void Contracts_assembly_references_only_the_BCL()
    {
        var bclPrefixes = new[] { "mscorlib", "netstandard", "System", "Microsoft.CSharp", "WindowsBase" };

        bool IsBcl(string? name) =>
            name != null && bclPrefixes.Any(p => name == p || name.StartsWith(p + ".", StringComparison.Ordinal));

        var nonBcl = typeof(AgentContractVersion).Assembly
            .GetReferencedAssemblies()
            .Where(a => !IsBcl(a.Name))
            .Select(a => a.Name)
            .ToArray();

        nonBcl.Should().BeEmpty("le contrat agent vers plateforme ne doit dépendre que du BCL (blueprint.md §3.2)");
    }
}
