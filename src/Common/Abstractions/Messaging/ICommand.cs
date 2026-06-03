namespace Stratum.Common.Abstractions.Messaging;

using MediatR;

public interface ICommand : IRequest
{
}

public interface ICommand<out TResult> : IRequest<TResult>
{
}
