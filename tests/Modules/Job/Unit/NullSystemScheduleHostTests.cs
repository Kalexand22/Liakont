namespace Stratum.Modules.Job.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Job.Infrastructure.Services;
using Xunit;

// BUG-4b : le défaut socle-nu de ISystemScheduleHost ne traite AUCUN job comme « système » et n'expose
// aucune société porteuse cross-tenant → le formulaire et la liste gardent le comportement du socle nu
// (toute planification tenant-scopée ; un opérateur sans société est bloqué, liste vide). Garde explicite
// du contrat « socle nu inchangé » (le Host produit l'écrase par LiakontSystemScheduleHost).
public sealed class NullSystemScheduleHostTests
{
    private readonly NullSystemScheduleHost _host = new();

    [Fact]
    public void No_Job_Type_Is_Treated_As_System()
    {
        _host.ResolveHostCompanyId("Any.Job.Type").Should().BeNull();
        _host.ResolveHostCompanyId(string.Empty).Should().BeNull();
    }

    [Fact]
    public void No_Cross_Tenant_Host_Company_Is_Exposed()
    {
        _host.CrossTenantHostCompanyId.Should().BeNull();
    }
}
