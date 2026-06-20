namespace Liakont.Tests.E2E.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Liakont.Host.Security;
using Xunit;

/// <summary>
/// Garde CI de RDF10 : impose qu'<b>au moins un E2E par permission sensible</b>
/// (<see cref="SensitivePermissions"/>) soit exercé avec un rôle realm <b>NON super-admin</b>. C'est la
/// réparation du trou IDN01 (gating jamais réellement exercé car joué en super-admin) : chaque preuve
/// est un test E2E réel annoté <see cref="SensitivePermissionCoverageAttribute"/>, et cette garde échoue
/// si une permission sensible perd sa couverture ou si une couverture est déclarée sur autre chose qu'un
/// vrai E2E non super-admin.
/// <para>
/// Test UNITAIRE pur (réflexion sur l'assembly E2E, aucun Playwright, aucun conteneur) : volontairement
/// SANS <c>[Trait("Category","E2E")]</c> et SANS héritage de <c>KeycloakBaseE2ETest</c>, pour qu'il tourne
/// dans <c>run-tests</c> (filtre <c>Category!=E2E</c>) — comme <see cref="E2EUserOtpSecretsConsistencyTests"/>.
/// </para>
/// </summary>
public sealed class SensitivePermissionE2ECoverageTests
{
    [Fact]
    public void Every_Sensitive_Permission_Has_A_NonSuperAdmin_E2E()
    {
        var coverage = CollectCoverage();

        foreach (var permission in SensitivePermissions.All)
        {
            var missing =
                $"la permission sensible '{permission}' DOIT être exercée par ≥1 E2E avec un rôle non "
                + "super-admin (garde RDF10, trou IDN01) — annoter le test de gating avec "
                + "[SensitivePermissionCoverage(permission, role)]";

            coverage.Should().ContainKey(permission, missing);
            coverage[permission].Should().NotBeEmpty(
                $"la permission sensible '{permission}' doit avoir au moins un rôle couvrant non super-admin");
        }
    }

    [Fact]
    public void Coverage_Declarations_Reference_Real_NonSuperAdmin_E2E_Tests()
    {
        foreach (var (method, attribute) in AnnotatedMethods())
        {
            var type = method.DeclaringType!;
            var where = $"{type.Name}.{method.Name}";

            var notE2E = $"{where} déclare une couverture de permission sensible mais n'est pas un E2E (doit dériver de KeycloakBaseE2ETest)";
            typeof(Liakont.Tests.E2E.KeycloakBaseE2ETest).IsAssignableFrom(type).Should().BeTrue(notE2E);

            HasE2ECategoryTrait(type).Should().BeTrue(
                $"{type.Name} doit porter [Trait(\"Category\",\"E2E\")] (sinon le test tournerait hors conteneurs)");

            method.GetCustomAttributes<FactAttribute>(inherit: true).Any().Should().BeTrue(
                $"{where} doit être un test xUnit ([Fact]/[Theory])");

            // Rôle non super-admin : le realm E2E ne contient AUCUN super-admin ; tout utilisateur seedé
            // (donc présent dans E2EUserOtpSecrets) est par construction un rôle realm non super-admin.
            var act = () => E2EUserOtpSecrets.ForUser(attribute.Role);
            act.Should().NotThrow(
                $"le rôle '{attribute.Role}' déclaré par {where} doit être un utilisateur E2E seedé (non super-admin)");
        }
    }

    private static Dictionary<string, HashSet<string>> CollectCoverage()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, attribute) in AnnotatedMethods())
        {
            if (!map.TryGetValue(attribute.Permission, out var roles))
            {
                roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[attribute.Permission] = roles;
            }

            roles.Add(attribute.Role);
        }

        return map;
    }

    private static IEnumerable<(MethodInfo Method, SensitivePermissionCoverageAttribute Attribute)> AnnotatedMethods()
    {
        var assembly = typeof(SensitivePermissionCoverageAttribute).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in method.GetCustomAttributes<SensitivePermissionCoverageAttribute>(inherit: false))
                {
                    yield return (method, attribute);
                }
            }
        }
    }

    private static bool HasE2ECategoryTrait(Type type) =>
        type.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(TraitAttribute) && a.ConstructorArguments.Count == 2)
            .Any(a => (a.ConstructorArguments[0].Value as string) == "Category"
                   && (a.ConstructorArguments[1].Value as string) == "E2E");
}
