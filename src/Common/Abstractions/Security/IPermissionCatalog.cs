namespace Stratum.Common.Abstractions.Security;

public interface IPermissionCatalog
{
    IReadOnlyList<PermissionCatalogEntry> GetAll();
}
