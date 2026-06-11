namespace Liakont.Modules.TvaMapping.Tests.Unit;

using System.Linq;
using FluentAssertions;
using Liakont.Modules.TvaMapping.Domain.Services;
using Xunit;

/// <summary>
/// Liste FERMÉE des codes VATEX proposés à l'édition console (item TVA05 / WEB07b). Les codes sont
/// TRANSCRITS du tableau « Codes VATEX clés » de F03 §2.2 — ce test verrouille la transcription : aucun
/// code n'est ajouté ni retiré sans une décision de spec (CLAUDE.md n°2, garde anti-règle-inventée).
/// </summary>
public sealed class VatexCatalogTests
{
    // Transcription EXACTE de F03-Mapping-TVA.md §2.2 (ordre du tableau). Toute divergence ici signale
    // soit une dérive du catalogue, soit une mise à jour de spec à acter explicitement.
    private static readonly string[] ExpectedCodesFromF03Section22 =
    {
        "VATEX-EU-F",
        "VATEX-EU-I",
        "VATEX-EU-J",
        "VATEX-EU-AE",
        "VATEX-EU-IC",
        "VATEX-EU-G",
        "VATEX-EU-O",
        "VATEX-FR-FRANCHISE",
        "VATEX-FR-AE",
        "VATEX-FR-CNWVAT",
        "VATEX-FR-298SEXDECIESA",
    };

    [Fact]
    public void AllowedCodes_match_F03_section_2_2_exactly()
    {
        VatexCatalog.AllowedCodes.Should().Equal(ExpectedCodesFromF03Section22);
    }

    [Fact]
    public void Every_entry_has_a_code_and_a_human_description()
    {
        VatexCatalog.All.Should().OnlyContain(entry =>
            !string.IsNullOrWhiteSpace(entry.Code) && !string.IsNullOrWhiteSpace(entry.Description));
    }

    [Fact]
    public void AllowedCodes_is_derived_from_All_single_source_of_truth()
    {
        VatexCatalog.AllowedCodes.Should().Equal(VatexCatalog.All.Select(entry => entry.Code));
    }

    [Fact]
    public void No_duplicate_codes()
    {
        VatexCatalog.AllowedCodes.Should().OnlyHaveUniqueItems();
    }
}
