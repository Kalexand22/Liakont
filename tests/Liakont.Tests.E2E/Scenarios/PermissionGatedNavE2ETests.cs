namespace Liakont.Tests.E2E.Scenarios;

using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Tests.E2E;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// E2E (IDN01, ADR-0017) du pont rôle→permission sous OIDC sur une surface permission-gated DÉJÀ
/// existante : la nav Supervision de WEB01. Prouve qu'un rôle élevé NON super-admin voit EXACTEMENT
/// les éléments que ses rôles realm accordent — le <c>superviseur</c> (porteur de
/// <c>liakont.supervision</c>) voit l'entrée Supervision, l'<c>operateur</c> ne la voit pas (mais voit
/// bien les entrées non gardées). Anti-faux-vert : aucun E2E n'est affaibli, l'élément est réellement
/// rendu pour le rôle qui le porte. La re-validation de l'E2E opérateur 7/7 de WEB05 lui incombe.
/// </summary>
/// <remarks>
/// Catégorie E2E : exécuté par la suite Playwright (<c>tools/run-e2e.ps1</c>, conteneurs Keycloak +
/// PostgreSQL), pas par <c>run-tests</c>. La preuve EXÉCUTÉE dans le pipeline d'IDN01 (verify-fast +
/// run-tests) est portée par les tests unitaires/bUnit/intégration de la couche d'auth.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class PermissionGatedNavE2ETests : KeycloakBaseE2ETest
{
    private const string SupervisionNavTestId = "nav-link-supervision";
    private const string DocumentsNavTestId = "nav-link-documents";

    public PermissionGatedNavE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Superviseur_Sees_Supervision_Nav()
    {
        await LoginViaKeycloakAsync("superviseur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        (await Page.GetByTestId(SupervisionNavTestId).IsVisibleAsync())
            .Should().BeTrue("le superviseur porte liakont.supervision (matrice §3) → l'entrée Supervision est visible");
    }

    [Fact]
    public async Task Operateur_Does_Not_See_Supervision_Nav_But_Sees_Documents()
    {
        await LoginViaKeycloakAsync("operateur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Les entrées non gardées restent visibles pour un rôle élevé non super-admin.
        (await Page.GetByTestId(DocumentsNavTestId).IsVisibleAsync())
            .Should().BeTrue("l'operateur voit les entrées de navigation non gardées (Documents)");

        // Supervision n'est PAS rendue : l'operateur ne porte pas liakont.supervision.
        (await Page.GetByTestId(SupervisionNavTestId).CountAsync())
            .Should().Be(0, "l'operateur ne porte pas liakont.supervision → l'entrée Supervision est masquée");
    }
}
