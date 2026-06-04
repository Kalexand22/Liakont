namespace Liakont.Modules.TenantSettings.Tests.Integration.Doubles;

using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>Filtre de société de test : renvoie un company_id fixe (isolation par test).</summary>
internal sealed class TestCompanyFilter : ICompanyFilter
{
    private readonly Guid _companyId;

    public TestCompanyFilter(Guid companyId)
    {
        _companyId = companyId;
    }

    public Guid GetRequiredCompanyId() => _companyId;
}
