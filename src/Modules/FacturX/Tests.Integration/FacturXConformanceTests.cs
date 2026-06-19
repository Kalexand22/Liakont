namespace Liakont.Modules.FacturX.Tests.Integration;

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.FacturX.Application.Cii;
using Liakont.Modules.FacturX.Contracts;
using Liakont.Modules.FacturX.Infrastructure;
using Liakont.Modules.FacturX.Tests.Unit.Cii;
using QuestPDF.Infrastructure;
using Xunit;

/// <summary>
/// Tier d'intégration FX04 (ADR-0023 §4, INV-FX-5 ; F16 §8) : le Factur-X RÉELLEMENT généré par
/// <see cref="FacturXBuilder"/> est validé en conteneur Docker par <b>veraPDF</b> (conformité PDF/A-3b)
/// ET <b>Mustangproject</b> (conformité Factur-X / Schematron EN 16931 — XML CII embarqué + conteneur).
/// La validation est BLOQUANTE : un artefact non conforme fait échouer le test (pas de faux-vert). On
/// croise l'exit code ET le verdict XML pour ne jamais conclure « conforme » sur une sortie d'erreur
/// (ex. OOM, parsing). Couvre toute la matrice de sortie V1 (mono/multi-taux, exonéré VATEX,
/// autoliquidation, criée). Exige un démon Docker (comme toute suite Testcontainers du dépôt).
/// </summary>
[Collection("FacturXValidation")]
public sealed class FacturXConformanceTests
{
    private readonly FacturXValidationContainers _containers;

    public FacturXConformanceTests(FacturXValidationContainers containers)
    {
        _containers = containers;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static IEnumerable<object[]> MatrixCases =>
        CiiTestPivots.Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task GeneratedFacturX_PassesVeraPdfAndMustang(string caseName)
    {
        PivotDocumentDto pivot = CiiTestPivots.Get(caseName);
        FacturXDocument document = await new FacturXBuilder(new CrossIndustryInvoiceSerializer()).BuildAsync(pivot);

        // (1) veraPDF — conformité PDF/A-3b. 0 = conforme ; 1 = non conforme ; ≥ 2 = erreur d'exécution.
        ExecResult vera = await _containers.ValidatePdfA3bAsync(document.PdfBytes, caseName);
        vera.ExitCode.Should().Be(
            0L,
            "le Factur-X « {0} » doit être un PDF/A-3b conforme (veraPDF). Sortie :\n{1}\n{2}",
            caseName,
            vera.Stdout,
            vera.Stderr);

        // (2) Mustangproject — conformité Factur-X / EN 16931. Exit 0 ET <summary status="valid">.
        ExecResult mustang = await _containers.ValidateFacturXAsync(document.PdfBytes, caseName);
        mustang.ExitCode.Should().Be(
            0L,
            "le Factur-X « {0} » doit être valide pour Mustangproject. Sortie :\n{1}\n{2}",
            caseName,
            mustang.Stdout,
            mustang.Stderr);
        Regex.IsMatch(mustang.Stdout, "<summary\\s+status=\"valid\"")
            .Should().BeTrue(
                "Mustang doit produire un verdict <summary status=\"valid\"> pour « {0} » (anti faux-vert). Sortie :\n{1}",
                caseName,
                mustang.Stdout);
    }
}
