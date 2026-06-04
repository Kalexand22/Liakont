namespace Liakont.Modules.TvaMapping.Tests.Integration.Doubles;

using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Filtre tenant de test (l'implémentation réelle <c>CompanyFilter</c> est <c>internal</c> au Common) :
/// résout le <c>company_id</c> courant depuis le contexte acteur, exactement comme en production.
/// </summary>
internal sealed class TestCompanyFilter : ICompanyFilter
{
    private readonly IActorContextAccessor _accessor;

    public TestCompanyFilter(IActorContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid GetRequiredCompanyId()
        => _accessor.Current.CompanyId
           ?? throw new InvalidOperationException("Aucun tenant courant dans le contexte de test.");
}
