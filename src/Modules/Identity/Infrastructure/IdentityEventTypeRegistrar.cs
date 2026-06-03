namespace Stratum.Modules.Identity.Infrastructure;

using Microsoft.Extensions.Hosting;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Identity.Contracts.Events;

internal sealed class IdentityEventTypeRegistrar : IHostedService
{
    private readonly IEventTypeRegistry _registry;

    public IdentityEventTypeRegistrar(IEventTypeRegistry registry)
    {
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry
            .Register<UserCreatedV1>("identity.user.created")
            .Register<UserDeactivatedV1>("identity.user.deactivated")
            .Register<UserRoleAssignedV1>("identity.user_role.assigned");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
