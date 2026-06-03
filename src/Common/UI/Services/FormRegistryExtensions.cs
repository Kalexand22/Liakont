namespace Stratum.Common.UI.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

public static class FormRegistryExtensions
{
    public static IServiceCollection RegisterForm<TEntity, TForm>(
        this IServiceCollection services, string? contextKey = null)
        where TForm : ComponentBase
    {
        services.AddSingleton(new FormRegistration(typeof(TEntity), typeof(TForm), contextKey));
        return services;
    }
}
