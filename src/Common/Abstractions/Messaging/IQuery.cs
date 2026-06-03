namespace Stratum.Common.Abstractions.Messaging;

using MediatR;

public interface IQuery<out TResult> : IRequest<TResult>
{
}
