namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;

/// <summary>
/// Fournisseur de services minimal résolvant PLUSIEURS services par type (scope tenant fictif du dashboard
/// SUP02, où l'agrégateur résout IAlertQueries / IDocumentQueries / IAgentQueries / IAlertAcknowledgementService).
/// Variante multi-service de <see cref="SingleServiceProvider"/>.
/// </summary>
internal sealed class MultiServiceProvider : IServiceProvider
{
    private readonly IReadOnlyDictionary<Type, object> _services;

    public MultiServiceProvider(IReadOnlyDictionary<Type, object> services) => _services = services;

    public object? GetService(Type serviceType) =>
        _services.TryGetValue(serviceType, out var instance) ? instance : null;
}
