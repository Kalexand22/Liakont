namespace Liakont.Host.Tests.Unit.Documents;

using System.Linq;
using FluentAssertions;
using Liakont.Host.Documents;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class DocumentColumnRegistryTests
{
    [Fact]
    public void Should_Declare_The_F10_Columns_With_Their_Keys()
    {
        var registry = new DocumentColumnRegistry();

        var keys = registry.GetAvailableColumns().Select(c => c.Key).ToList();

        // Les clés correspondent aux propriétés de DocumentSummaryDto (lecture par la grille).
        keys.Should().Contain(["DocumentNumber", "IssueDate", "CustomerName", "TotalGross", "DocumentType", "State", "LastUpdateUtc"]);
    }

    [Fact]
    public void Should_Show_The_Operator_Columns_By_Default_And_Hide_LastUpdate()
    {
        var registry = new DocumentColumnRegistry();

        var visibleKeys = registry.GetDefaultVisibleColumns().Select(c => c.Key).ToList();

        visibleKeys.Should().Contain(["DocumentNumber", "IssueDate", "CustomerName", "TotalGross", "DocumentType", "State"]);
        visibleKeys.Should().NotContain("LastUpdateUtc", "la date de mise à jour est un axe disponible mais masqué par défaut");
    }

    [Fact]
    public void Should_Type_The_Amount_As_Money_And_The_Date_As_Date()
    {
        var registry = new DocumentColumnRegistry();

        registry.GetColumn("TotalGross")!.DataType.Should().Be(ColumnDataType.Money);
        registry.GetColumn("IssueDate")!.DataType.Should().Be(ColumnDataType.Date);
    }

    [Fact]
    public void Should_Expose_State_As_A_Text_Column_Without_Raw_English_Enum_Values()
    {
        var registry = new DocumentColumnRegistry();

        var state = registry.GetColumn("State")!;

        // Texte (pas Enum) : le filtre avancé de la grille n'exposerait sinon les clés brutes anglaises.
        // Le filtrage d'état français passe par le sélecteur de la page et les pastilles de synthèse.
        state.DataType.Should().Be(ColumnDataType.Text);
        state.AllowedValues.Should().BeNull();
    }

    [Fact]
    public void Should_Use_French_Titles()
    {
        var registry = new DocumentColumnRegistry();

        registry.GetColumn("DocumentNumber")!.Title.Should().Be("N°");
        registry.GetColumn("CustomerName")!.Title.Should().Be("Acheteur");
        registry.GetColumn("TotalGross")!.Title.Should().Be("Montant");
        registry.GetColumn("State")!.Title.Should().Be("État");
    }

    [Fact]
    public void Should_Declare_The_Document_Family_Column_Visible_By_Default()
    {
        // BUG-20 : la famille de pièce (BA/BV/FC/NH) est une colonne Texte visible par défaut, dérivée par le
        // ColumnTemplate de la page depuis la référence source (clé « SourceReference »).
        var registry = new DocumentColumnRegistry();

        var family = registry.GetColumn("SourceReference")!;
        family.Title.Should().Be("Famille de pièce");
        family.DataType.Should().Be(ColumnDataType.Text);
        registry.GetDefaultVisibleColumns().Select(c => c.Key).Should().Contain("SourceReference");
    }
}
