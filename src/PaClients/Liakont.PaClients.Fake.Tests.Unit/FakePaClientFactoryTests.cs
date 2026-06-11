namespace Liakont.PaClients.Fake.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Xunit;

/// <summary>
/// Couvre la fabrique du plug-in factice (acceptance PAA02 : « se référence, se configure et
/// s'enregistre exactement comme B2Brouter / Super PDP ») : clé de type stable, propagation de la
/// configuration aux clients créés, et résolution par le registre de TYPES du module (par clé, jamais
/// un <c>if (type == …)</c> — CLAUDE.md n°6/16).
/// </summary>
public sealed class FakePaClientFactoryTests
{
    [Fact]
    public void PaType_Is_The_Fake_Key()
    {
        new FakePaClientFactory().PaType.Should().Be("Fake");
        FakePaClientFactory.PaTypeKey.Should().Be("Fake");
    }

    [Fact]
    public async Task Create_Produces_A_Client_With_The_Configured_Capabilities()
    {
        var caps = new PaCapabilities { PaName = "FakeConfig", SupportsCreditNotes = false };
        var factory = new FakePaClientFactory(new FakePaClientOptions { Capabilities = caps });

        var client = factory.Create(new PaAccountDescriptor("Fake", "tenant-a"));

        client.Capabilities.PaName.Should().Be("FakeConfig");

        // La capacité restreinte se reflète dans le comportement (avoir → résultat typé, jamais d'exception).
        var result = await client.SendDocumentAsync(TestDocuments.CreditNote("A-X"));
        result.State.Should().Be(PaSendState.CapabilityNotSupported);
    }

    [Fact]
    public void Create_With_A_Null_Account_Throws()
    {
        var factory = new FakePaClientFactory();

        var act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Registry_Resolves_The_Fake_Plugin_By_Key_Case_Insensitively()
    {
        var registry = new PaClientRegistry([new FakePaClientFactory()]);

        var client = registry.Resolve(new PaAccountDescriptor("fake", "tenant-a"));

        client.Should().BeOfType<FakePaClient>();
    }

    [Fact]
    public void Create_Returns_The_Same_Instance_For_The_Same_Account()
    {
        // FIX201 lifetime : deux résolutions du même compte (onboarding puis envoi) doivent partager
        // l'instance, sinon le réglage publié serait perdu (cf. remarque de classe).
        var factory = new FakePaClientFactory();

        var first = factory.Create(new PaAccountDescriptor("Fake", "tenant-a"));
        var second = factory.Create(new PaAccountDescriptor("fake", "tenant-a"));

        // Même compte (type insensible à la casse) → MÊME instance.
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Create_Returns_Distinct_Instances_Per_Account()
    {
        var factory = new FakePaClientFactory();

        var tenantA = factory.Create(new PaAccountDescriptor("Fake", "tenant-a"));
        var tenantB = factory.Create(new PaAccountDescriptor("Fake", "tenant-b"));

        // Comptes distincts → instances isolées (l'état d'un tenant ne fuit pas vers un autre).
        tenantB.Should().NotBeSameAs(tenantA);
    }

    [Fact]
    public async Task Published_Setting_Survives_A_Second_Resolution_And_The_Send_Issues()
    {
        // Parcours produit FIX201 : onboarding sur une résolution, envoi sur une AUTRE résolution.
        // Sans le cache par compte, la seconde résolution rendrait une instance vierge → réglage inactif
        // → « Transport not available » → aucun envoi pour toujours.
        var registry = new PaClientRegistry([new FakePaClientFactory()]);
        var account = new PaAccountDescriptor("Fake", "tenant-a");

        // 1) Onboarding : publication du SIREN / activation de la transmission.
        var onboarding = registry.Resolve(account);
        await onboarding.EnsureTaxReportSettingAsync(new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "LBS",
            EnterpriseSize = "PME",
            CinScheme = "0002",
        });

        // 2) Envoi : nouvelle résolution (comme SendTenantJob, qui résout à chaque exécution).
        var sending = registry.Resolve(account);

        var setting = await sending.GetTaxReportSettingAsync();
        setting.StartDate.Should().Be(new DateOnly(2026, 1, 1));

        var result = await sending.SendDocumentAsync(TestDocuments.Invoice("F-1"));
        result.State.Should().Be(PaSendState.Issued);
    }

    [Fact]
    public async Task A_Fresh_Account_Is_Not_Published_So_The_Send_Diagnostic_Blocks()
    {
        // Sans onboarding (env vierge) : le réglage n'a pas de date de début → inactif → le diagnostic
        // pré-envoi (SendTenantJob) refuse l'envoi. C'est exactement le blocage que FIX201 lève.
        var registry = new PaClientRegistry([new FakePaClientFactory()]);

        var client = registry.Resolve(new PaAccountDescriptor("Fake", "tenant-never-published"));

        var setting = await client.GetTaxReportSettingAsync();
        setting.StartDate.Should().BeNull();
    }
}
