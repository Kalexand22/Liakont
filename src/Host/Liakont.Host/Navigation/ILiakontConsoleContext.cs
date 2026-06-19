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

    /// <summary>
    /// Nombre d'éléments de réconciliation EN ATTENTE d'une action opérateur (propositions à confirmer +
    /// PDF orphelins à rattacher), pour le compteur de la navigation (WEB08). Les documents émis sans PDF en
    /// sont exclus : ils n'appellent pas d'action immédiate (ils attendent un PDF de la source). <c>0</c> tant
    /// que le contexte n'est pas chargé. INSTANTANÉ pris à l'ouverture du circuit (la nav est construite une
    /// fois) ; il n'est pas réévalué après une action dans la page.
    /// </summary>
    int ReconciliationPendingCount { get; }

    /// <summary>
    /// Vrai si l'utilisateur courant est un super-admin (rôle <c>stratum-admin</c>) opérant en contexte
    /// CROSS-TENANT : il n'appartient à aucun tenant en particulier (RB1). La navigation masque alors les
    /// entrées TENANT-SCOPÉES (Documents, Encaissements, Traitements, Signatures, Réconciliation,
    /// Paramétrage) et ne laisse que les surfaces cross-tenant (Supervision, Clients, Flotte) ; le chrome
    /// n'affiche pas de « tenant courant ». Faux pour un utilisateur de tenant ordinaire (qui voit, lui,
    /// ses surfaces tenant selon ses permissions). Faux tant que le contexte n'est pas chargé.
    /// </summary>
    bool IsCrossTenantAdmin { get; }

    /// <summary>Charge (une seule fois) l'état de console pour le tenant courant. Idempotent.</summary>
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}
