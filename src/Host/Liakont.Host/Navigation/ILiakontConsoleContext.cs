namespace Liakont.Host.Navigation;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// État de console pré-chargé PAR CIRCUIT pour piloter la navigation conditionnelle (WEB01). Lu de façon
/// SYNCHRONE par les fournisseurs de navigation (<c>INavSectionProvider.GetSection</c> est synchrone) ;
/// chargé une seule fois à l'ouverture du circuit par <see cref="LiakontConsoleCircuitHandler"/>, ce qui
/// garantit que la nav conditionnelle est correcte dès le premier rendu.
/// </summary>
internal interface ILiakontConsoleContext
{
    /// <summary>
    /// Vrai si la section Réconciliation doit être visible : l'agent du tenant alimente un pool de PDF
    /// non rattachés (<c>IIngestedPdfStore</c>). Faux tant que le contexte n'est pas chargé ou si le pool
    /// est vide.
    /// </summary>
    /// <remarks>
    /// La spec WEB01 cite « capacité pool PDF déclarée via GET /settings » ; or ce read-model de capacités
    /// agent relève d'API01d (GELÉ) et n'est PAS exposé. Signal disponible et fidèle retenu : la présence
    /// effective du pool — un agent qui « fournit le pool » est exactement celui qui y dépose des PDF.
    /// Heuristique d'AFFICHAGE uniquement, aucune règle fiscale.
    /// </remarks>
    bool ReconciliationAvailable { get; }

    /// <summary>Charge (une seule fois) l'état de console pour le tenant courant. Idempotent.</summary>
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}
