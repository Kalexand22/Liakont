namespace Liakont.Host.Tests.Unit.Security;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

/// <summary>
/// Garde de page (RLF03 / finding F5a). Chaque page opérateur Liakont doit porter
/// <c>[Authorize(Policy = ...)]</c> mappée sur la permission §3 correcte
/// (identity-permissions-liakont.md), et NON un simple <c>[Authorize]</c> (ouvrable par tout
/// authentifié). Anti-régression : une page paramétrage accessible en lecture directe par URL
/// (ex. /parametrage/table-tva = table TVA fiscale) est le défaut exact que ce lot corrige.
/// Le HUB /parametrage est gardé par <c>liakont.read</c> (et non settings) : FIX208 y offre l'export d'audit
/// par période aux lecteurs — le gater par settings régresserait cette capacité ; ses sous-pages settings
/// (table TVA, comptes PA, fiscal, alertes, agents) restent en <c>liakont.settings</c>.
/// Test par réflexion sur l'attribut du composant généré — déterministe, sans navigateur ni credentials.
/// </summary>
public sealed class PageAuthorizationPolicyTests
{
    [Theory]
    [InlineData(typeof(Documents), LiakontPermissions.Read)]
    [InlineData(typeof(DocumentDetail), LiakontPermissions.Read)]
    [InlineData(typeof(Encaissements), LiakontPermissions.Read)]
    [InlineData(typeof(Treatments), LiakontPermissions.Actions)]
    [InlineData(typeof(Parametrage), LiakontPermissions.Read)]
    [InlineData(typeof(Fiscal), LiakontPermissions.Settings)]
    [InlineData(typeof(TableTva), LiakontPermissions.Settings)]
    [InlineData(typeof(ComptesPa), LiakontPermissions.Settings)]
    [InlineData(typeof(Alertes), LiakontPermissions.Settings)]
    [InlineData(typeof(Agents), LiakontPermissions.Settings)]
    public void Operator_Page_Should_Be_Guarded_By_Its_Section3_Permission_Policy(Type pageType, string expectedPolicy)
    {
        var authorize = pageType
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .ToList();

        authorize.Should().ContainSingle("la page {0} doit porter exactement une garde [Authorize]", pageType.Name);
        authorize[0].Policy.Should().Be(
            expectedPolicy,
            "la page {0} doit être gardée par la policy {1} (matrice §3)",
            pageType.Name,
            expectedPolicy);
    }
}
