namespace Liakont.Modules.TenantSettings.Infrastructure;

using System.Collections.Concurrent;
using Liakont.Modules.TenantSettings.Application;
using Microsoft.AspNetCore.DataProtection;

/// <summary>
/// Implémentation de <see cref="ISecretProtector"/> via ASP.NET Core Data Protection. Les clés de
/// protection sont gérées par l'infrastructure DP de l'instance (persistance par appliance — OPS01) ;
/// ce composant protège/déprotège sous un « purpose » dédié, un par secret (isolation cryptographique :
/// voir <see cref="PaAccountSecretPurposes"/>). Les protecteurs sont mis en cache par purpose.
/// </summary>
internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtectionProvider _provider;
    private readonly ConcurrentDictionary<string, IDataProtector> _protectors = new(StringComparer.Ordinal);

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public string Protect(string plaintext) => Protect(plaintext, PaAccountSecretPurposes.ApiKey);

    public string Unprotect(string protectedValue) => Unprotect(protectedValue, PaAccountSecretPurposes.ApiKey);

    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return GetProtector(purpose).Protect(plaintext);
    }

    public string Unprotect(string protectedValue, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return GetProtector(purpose).Unprotect(protectedValue);
    }

    private IDataProtector GetProtector(string purpose) =>
        _protectors.GetOrAdd(purpose, p => _provider.CreateProtector(p));
}
