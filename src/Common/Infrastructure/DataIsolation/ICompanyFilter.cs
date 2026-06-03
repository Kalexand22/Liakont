namespace Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Explicit company filter for data isolation.
/// Repositories call this to obtain the current company ID for WHERE clauses.
/// Throws InvalidOperationException if CompanyId is null.
/// </summary>
public interface ICompanyFilter
{
    /// <summary>
    /// Returns the current company ID from IActorContext.
    /// Throws InvalidOperationException if CompanyId is null.
    /// </summary>
    Guid GetRequiredCompanyId();
}
