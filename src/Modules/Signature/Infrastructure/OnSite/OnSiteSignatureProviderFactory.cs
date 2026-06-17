namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Fabrique du fournisseur de signature SUR PLACE (Wacom — ADR-0030). Enregistrée en singleton dans le
/// conteneur ; l'<see cref="ISignatureProviderRegistry"/> l'indexe par <see cref="ProviderType"/> et résout
/// le fournisseur sans aucun <c>if (type == "Wacom")</c> (CLAUDE.md n°6/16). Les capacités du sur place sont
/// FIXES (pilotées par ADR-0030, pas par le compte du tenant) : la fabrique ne lit aucun secret du compte.
/// </summary>
public sealed class OnSiteSignatureProviderFactory : ISignatureProviderFactory
{
    /// <inheritdoc />
    public string ProviderType => OnSiteSignatureProvider.ProviderTypeKey;

    /// <inheritdoc />
    public ISignatureProvider Create(SignatureProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new OnSiteSignatureProvider();
    }
}
