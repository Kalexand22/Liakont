namespace Liakont.Host.Navigation;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Server.Circuits;

/// <summary>
/// Pré-charge l'état de console (<see cref="ILiakontConsoleContext"/>) à l'ouverture du circuit Blazor,
/// AVANT que la navigation ne soit rendue, pour que la nav conditionnelle (Réconciliation) soit correcte
/// dès le premier rendu interactif. S'exécute APRÈS <c>TenantCircuitHandler</c> (qui n'override pas
/// <see cref="CircuitHandler.Order"/> et vaut donc 0) : le tenant doit être propagé au circuit d'abord.
/// </summary>
internal sealed class LiakontConsoleCircuitHandler : CircuitHandler
{
    private readonly ILiakontConsoleContext _consoleContext;

    public LiakontConsoleCircuitHandler(ILiakontConsoleContext consoleContext)
    {
        _consoleContext = consoleContext;
    }

    /// <summary>Après le handler de tenant (Order 0), afin de lire le tenant déjà propagé.</summary>
    public override int Order => 100;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        => _consoleContext.EnsureInitializedAsync(cancellationToken);
}
