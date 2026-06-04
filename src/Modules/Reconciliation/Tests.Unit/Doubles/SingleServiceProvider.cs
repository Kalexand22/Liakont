namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System;

/// <summary>Fournisseur de services minimal : résout un unique service par son type (pour le job tenant).</summary>
internal sealed class SingleServiceProvider : IServiceProvider
{
    private readonly Type _serviceType;
    private readonly object _instance;

    public SingleServiceProvider(Type serviceType, object instance)
    {
        _serviceType = serviceType;
        _instance = instance;
    }

    public object? GetService(Type serviceType) =>
        serviceType == _serviceType ? _instance : null;
}
