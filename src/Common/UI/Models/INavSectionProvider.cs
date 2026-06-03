namespace Stratum.Common.UI.Models;

/// <summary>
/// Implemented by each module to contribute its sidebar navigation section.
/// Registered in DI; the ERP shell collects all providers to build the sidebar.
/// </summary>
public interface INavSectionProvider
{
    NavSection GetSection();
}
