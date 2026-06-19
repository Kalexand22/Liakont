namespace Liakont.Agent.Contracts.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Xunit;

/// <summary>
/// Garde d'architecture du contrat (acceptance PIV01) : les DTOs sont purs (aucune logique),
/// immuables, n'utilisent que <see cref="decimal"/> pour les montants, et l'assembly ne dépend que
/// du BCL (zéro PackageReference). La pureté côté agent est déjà vérifiée au niveau IL par
/// <c>ContractsPurityTests</c> (agent/tests) ; ce miroir côté plateforme garantit la même règle
/// dans la suite exécutée par verify-fast sur <c>src/Liakont.sln</c>.
/// </summary>
public sealed class ContractArchitectureTests
{
    private static readonly Assembly ContractsAssembly = typeof(PivotDocumentDto).Assembly;

    public static IEnumerable<object[]> DtoTypes =>
        ContractsAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && t.Name.EndsWith("Dto", StringComparison.Ordinal))
            .Select(t => new object[] { t });

    [Fact]
    public void DtoTypes_Should_Be_Discovered()
    {
        // Garde anti-faux-vert : si la découverte ne ramène aucun DTO, les tests paramétrés
        // ci-dessous passeraient à vide (faux vert). On épingle un plancher.
        DtoTypes.Count().Should().BeGreaterThanOrEqualTo(15, "tous les DTOs du contrat doivent être couverts");
    }

    [Theory]
    [MemberData(nameof(DtoTypes))]
    public void Dto_Should_Be_Immutable(Type dtoType)
    {
        var writableProperties = dtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        writableProperties.Should().BeEmpty(
            $"les DTOs du contrat sont immuables (get-only) — {dtoType.Name} expose un setter public");
    }

    [Theory]
    [MemberData(nameof(DtoTypes))]
    public void Dto_Should_Carry_No_Logic(Type dtoType)
    {
        // Aucune méthode hors accesseurs de propriété (get_/set_). Les constructeurs ne sont pas
        // renvoyés par GetMethods ; les classes (non records) n'ajoutent aucun Equals/ToString.
        var nonAccessorMethods = dtoType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();

        nonAccessorMethods.Should().BeEmpty(
            $"un DTO du contrat ne porte aucune logique — {dtoType.Name} déclare une méthode");
    }

    [Theory]
    [MemberData(nameof(DtoTypes))]
    public void Dto_Should_Not_Use_Float_Or_Double(Type dtoType)
    {
        var floatingProperties = dtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
            {
                var underlying = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                return underlying == typeof(float) || underlying == typeof(double);
            })
            .Select(p => p.Name)
            .ToArray();

        floatingProperties.Should().BeEmpty(
            $"les montants sont en decimal, jamais float/double (CLAUDE.md n°1) — {dtoType.Name}");
    }

    [Fact]
    public void Contracts_Assembly_Carries_No_Fiscal_Reconciliation_Logic()
    {
        // RDL12 (redline ADR-0005) : la formule de réconciliation BR-CO-13 (PivotReconciliation) est
        // une logique de validation fiscale, source UNIQUE de LineTotalsRule (Blocking) — elle ne doit
        // JAMAIS revenir dans le paquet publiable consommé par l'agent net48 (« l'agent n'a AUCUNE
        // logique métier », ADR-0005 décision 3, CLAUDE.md n°6). Elle vit désormais dans l'assembly
        // plateforme-seul Liakont.Platform.Pivot. Garde anti-régression : aucun type « *Reconciliation »
        // dans le contrat agent.
        var reconciliationTypes = ContractsAssembly
            .GetTypes()
            .Where(t => t.IsPublic && t.Name.Contains("Reconciliation", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToArray();

        reconciliationTypes.Should().BeEmpty(
            "la formule de réconciliation BR-CO-13 reste hors du contrat agent publiable, dans un assembly "
            + "plateforme-seul (RDL12, ADR-0005) — l'agent net48 n'embarque aucune logique métier");
    }

    [Fact]
    public void Contracts_Assembly_References_Only_The_Bcl()
    {
        var bclPrefixes = new[] { "mscorlib", "netstandard", "System", "Microsoft.CSharp", "WindowsBase" };

        bool IsBcl(string? name) =>
            name != null && bclPrefixes.Any(p => name == p || name.StartsWith(p + ".", StringComparison.Ordinal));

        var nonBcl = ContractsAssembly
            .GetReferencedAssemblies()
            .Where(a => !IsBcl(a.Name))
            .Select(a => a.Name)
            .ToArray();

        nonBcl.Should().BeEmpty("le contrat agent vers plateforme ne dépend que du BCL (blueprint.md §3.2)");
    }
}
