namespace Stratum.Common.UI.Services;

using System.Diagnostics.CodeAnalysis;

public interface IFormRegistry
{
    Type Resolve<TEntity>(string? contextKey = null);

    bool TryResolve<TEntity>(string? contextKey, [MaybeNullWhen(false)] out Type formType);
}
