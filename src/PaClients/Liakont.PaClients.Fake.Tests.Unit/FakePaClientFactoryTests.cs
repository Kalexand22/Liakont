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
    public async Task Published_Setting_Survives_A_Second_Resolution()
    {
        // Parcours produit FIX201 : l'onboarding (publication) et l'envoi sont DEUX résolutions distinctes
        // du registre (SendTenantJob résout à chaque exécution). Sans le cache par compte, la seconde
        // résolution rendrait une instance vierge → StartDate perdue. Ce test prouve la SURVIE du réglage
        // entre résolutions ; le gating d'envoi qui lit StartDate (SendTenantJob.IsTaxReportSettingActive)
        // est couvert par les tests pipeline (PipelineSendHarness), pas par le plug-in factice (qui émet
        // indépendamment du réglage).
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
        setting.TypeOperation.Should().Be("LBS");
    }

    [Fact]
    public async Task A_Fresh_Account_Has_No_Published_Setting()
    {
        // Sans onboarding (env vierge) : le réglage n'a pas de date de début. C'est cette valeur (nulle)
        // que lit le diagnostic pré-envoi (SendTenantJob.IsTaxReportSettingActive) pour refuser l'envoi —
        // le gating lui-même est couvert côté pipeline.
        var registry = new PaClientRegistry([new FakePaClientFactory()]);

        var client = registry.Resolve(new PaAccountDescriptor("Fake", "tenant-never-published"));

        var setting = await client.GetTaxReportSettingAsync();
        setting.StartDate.Should().BeNull();
    }
}
