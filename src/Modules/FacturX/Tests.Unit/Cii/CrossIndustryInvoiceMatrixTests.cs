namespace Liakont.Modules.FacturX.Tests.Unit.Cii;

using System.IO;
using System.Text;
using FluentAssertions;
using Liakont.Modules.FacturX.Application.Cii;
using Xunit;

/// <summary>
/// Conformité de toute la matrice de sortie V1 (mono-taux, multi-taux, exonéré VATEX, autoliquidation,
/// criée mono-Seller) : chaque CII produit est (1) bien formé et structurellement complet vis-à-vis
/// d'EN 16931 (BT/BG obligatoires), (2) conforme aux identités arithmétiques BR-CO EN 16931, et (3)
/// stable vis-à-vis du golden file commité (garde de régression). Acceptance FX03 (F16 §8 / ADR-0023 §4,
/// tier rapide d'« assertions structurelles »).
/// <para>
/// La conformité XSD CII EN 16931 + Schematron CEN/TC 434 RÉELLE n'est PAS exercée ici : les XSD CII du
/// dépôt sont des profils DGFiP restreints (faux négatif garanti — voir <see cref="CiiStructuralValidator"/>),
/// le XSD EN 16931 complet et le Schematron CEN sont des artefacts externes non vendorés (F16 §10 A4,
/// NON TRANCHÉ) et le Schematron exige un processeur XSLT 2.0 (ADR). Elle est portée par
/// <b>veraPDF + Mustangproject</b> au tier intégration de FX04 + la recette GATE_FACTURX.
/// </para>
/// </summary>
public sealed class CrossIndustryInvoiceMatrixTests
{
    private static readonly CrossIndustryInvoiceSerializer Serializer = new();

    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in CiiTestPivots.Names)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatrixCase_HasCompleteEn16931Structure(string caseName)
    {
        var xml = Serializer.Serialize(CiiTestPivots.Get(caseName));

        var issues = CiiStructuralValidator.Check(xml);

        issues.Should().BeEmpty(
            "le CII du cas « {0} » doit être bien formé et porter tous les BT/BG obligatoires EN 16931 ; manques : {1}",
            caseName,
            string.Join(" | ", issues));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatrixCase_SatisfiesBusinessRules(string caseName)
    {
        var xml = Serializer.Serialize(CiiTestPivots.Get(caseName));

        var violations = CiiBusinessRuleChecker.Check(xml);

        violations.Should().BeEmpty(
            "le CII du cas « {0} » doit satisfaire les identités BR-CO ; violations : {1}",
            caseName,
            string.Join(" | ", violations));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatrixCase_MatchesGolden(string caseName)
    {
        var actual = Encoding.UTF8.GetString(Serializer.Serialize(CiiTestPivots.Get(caseName)));
        var goldenPath = Path.Combine(RepoPaths.GoldenDir(), caseName + ".cii.xml");

        File.Exists(goldenPath).Should().BeTrue(
            "le golden file {0} doit exister (régénérer avec le générateur si la sortie a légitimement changé)",
            goldenPath);

        var expected = File.ReadAllText(goldenPath);
        Normalize(actual).Should().Be(
            Normalize(expected), "le CII du cas « {0} » ne doit pas dériver du golden commité", caseName);
    }

    private static string Normalize(string xml) =>
        xml.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).TrimEnd();
}
