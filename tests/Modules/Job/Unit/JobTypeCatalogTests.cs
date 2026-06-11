namespace Stratum.Modules.Job.Tests.Unit;

using System.Linq;
using FluentAssertions;
using Stratum.Modules.Job.Contracts.Services;
using Stratum.Modules.Job.Infrastructure;
using Stratum.Modules.Job.Infrastructure.Services;
using Xunit;

// FIX211 : tests de la dérivation par réflexion du catalogue des types de jobs (liste fixe, libellés FR,
// paramètres typés). Types de payload synthétiques pour isoler la logique des payloads réels du produit.
public sealed class JobTypeCatalogTests
{
    private enum TestMode
    {
        Fast,
        Slow,
    }

    [Fact]
    public void Empty_Record_Has_No_Parameters()
    {
        var catalog = Catalog((typeof(EmptyTrigger), "Déclencheur vide"));

        var descriptor = catalog.Find(typeof(EmptyTrigger).FullName!);

        descriptor.Should().NotBeNull();
        descriptor!.DisplayName.Should().Be("Déclencheur vide");
        descriptor.Parameters.Should().BeEmpty("un record sans propriété n'a aucun paramètre → payload masqué");
    }

    [Fact]
    public void Boolean_Parameter_With_Default_Is_Optional_With_Default_Value()
    {
        var catalog = Catalog((typeof(WithDryRun), "Avec simulation"));

        var p = catalog.Find(typeof(WithDryRun).FullName!)!.Parameters.Single();

        p.Name.Should().Be("DryRun");
        p.Kind.Should().Be(JobParameterKind.Boolean);
        p.Required.Should().BeFalse("le paramètre du constructeur a une valeur par défaut");
        p.DefaultValue.Should().Be("false");
    }

    [Fact]
    public void Positional_Required_String_Is_Required_Text()
    {
        var catalog = Catalog((typeof(WithRequiredTenant), null));

        var parameters = catalog.Find(typeof(WithRequiredTenant).FullName!)!.Parameters;

        var tenant = parameters.Single(p => p.Name == "TenantId");
        tenant.Kind.Should().Be(JobParameterKind.Text);
        tenant.Required.Should().BeTrue("paramètre de constructeur sans valeur par défaut et non annulable");

        var dryRun = parameters.Single(p => p.Name == "DryRun");
        dryRun.Required.Should().BeFalse();
    }

    [Fact]
    public void Required_Init_Member_Is_Required_And_Nullable_Init_Is_Optional()
    {
        var catalog = Catalog((typeof(WithRequiredInit), null));

        var parameters = catalog.Find(typeof(WithRequiredInit).FullName!)!.Parameters;

        var email = parameters.Single(p => p.Name == "Email");
        email.Kind.Should().Be(JobParameterKind.Text);
        email.Required.Should().BeTrue("propriété marquée 'required'");

        var retries = parameters.Single(p => p.Name == "Retries");
        retries.Kind.Should().Be(JobParameterKind.Number);
        retries.Required.Should().BeFalse("int? annulable → optionnel");
    }

    [Fact]
    public void Enum_Parameter_Exposes_Its_Options()
    {
        var catalog = Catalog((typeof(WithNumberAndEnum), null));

        var parameters = catalog.Find(typeof(WithNumberAndEnum).FullName!)!.Parameters;

        parameters.Single(p => p.Name == "Count").Kind.Should().Be(JobParameterKind.Number);

        var mode = parameters.Single(p => p.Name == "Mode");
        mode.Kind.Should().Be(JobParameterKind.Enum);
        mode.EnumOptions.Should().BeEquivalentTo("Fast", "Slow");
    }

    [Fact]
    public void Missing_Display_Name_Falls_Back_To_Humanized_Short_Name_Never_FullName()
    {
        var catalog = Catalog((typeof(WithDryRun), null));

        var descriptor = catalog.Find(typeof(WithDryRun).FullName!)!;

        descriptor.DisplayName.Should().Be("With dry run");
        descriptor.DisplayName.Should().NotContain(".", "jamais le FullName .NET");
    }

    [Fact]
    public void GetAll_Is_Sorted_By_Display_Name_And_Dedups_By_Key()
    {
        var catalog = Catalog(
            (typeof(WithDryRun), "Bravo"),
            (typeof(EmptyTrigger), "Alpha"),
            (typeof(WithDryRun), "Bravo")); // doublon de clé : une seule entrée

        var all = catalog.GetAll();

        all.Select(d => d.DisplayName).Should().Equal("Alpha", "Bravo");
    }

    [Fact]
    public void Find_Returns_Null_For_Unregistered_Key()
    {
        var catalog = Catalog((typeof(EmptyTrigger), null));

        catalog.Find("Nope.Unknown.Type").Should().BeNull();
    }

    private static JobTypeCatalog Catalog(params (System.Type Type, string? Label)[] registrations) =>
        new(registrations.Select(r => new JobHandlerRegistration(r.Type, r.Label)));

    private sealed record EmptyTrigger;

    private sealed record WithDryRun(bool DryRun = false);

    private sealed record WithRequiredTenant(string TenantId, bool DryRun = false);

    private sealed record WithNumberAndEnum(int Count, TestMode Mode);

    private sealed record WithRequiredInit
    {
        public required string Email { get; init; }

        public int? Retries { get; init; }
    }
}
