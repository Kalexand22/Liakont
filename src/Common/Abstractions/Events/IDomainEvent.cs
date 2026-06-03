namespace Stratum.Common.Abstractions.Events;

using MediatR;

/// <summary>
/// Marker interface for domain events raised within a module.
/// </summary>
public interface IDomainEvent : INotification
{
}
