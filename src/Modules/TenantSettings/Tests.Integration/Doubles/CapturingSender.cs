namespace Liakont.Modules.TenantSettings.Tests.Integration.Doubles;

using Liakont.Modules.TvaMapping.Contracts.Commands;
using MediatR;

/// <summary>
/// Double d'<see cref="ISender"/> capturant les commandes dispatchées hors du module (item FIX01b :
/// import de mapping TVA déclenché par l'import de seed tenant). Ne traverse PAS la frontière de module
/// pour de vrai (pas de handler TvaMapping ici) : il enregistre la commande et renvoie une valeur
/// configurable, ce qui permet de vérifier le CÂBLAGE (bon chemin de fichier, bon drapeau de résultat)
/// sans exiger la base TvaMapping dans le projet de test TenantSettings.
/// </summary>
internal sealed class CapturingSender : ISender
{
    /// <summary>Dernière commande dispatchée, ou <c>null</c> si aucune.</summary>
    public object? LastRequest { get; private set; }

    /// <summary>Valeur renvoyée pour une <see cref="ImportMappingTableSeedCommand"/> (import effectif par défaut).</summary>
    public bool MappingImportResult { get; set; } = true;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;

        if (request is ImportMappingTableSeedCommand && MappingImportResult is TResponse mapped)
        {
            return Task.FromResult(mapped);
        }

        throw new NotSupportedException(
            $"CapturingSender ne sait pas répondre à {request?.GetType().Name ?? "null"}.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        LastRequest = request;
        return Task.CompletedTask;
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult<object?>(null);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CapturingSender ne supporte pas les flux.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CapturingSender ne supporte pas les flux.");
}
