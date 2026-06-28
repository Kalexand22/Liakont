namespace Liakont.Modules.Pipeline.Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Rejeu À LA DEMANDE du contenu d'UN document pour l'affichage du détail ligne à ligne (BUG-5) : relit le pivot
/// SOURCE stagé (<c>IPayloadStagingStore</c>) et REJOUE le mapping TVA avec le MÊME moteur qu'au CHECK et qu'à
/// l'envoi (<c>CheckTvaMapping</c> + table validée du tenant) pour exposer, DÈS que le document est lu/contrôlé
/// (états Bloqué / Prêt-à-envoyer), les lignes lues + le résultat du mapping (catégorie/VATEX/taux) — pas
/// seulement après transmission. C'est la SEULE surface (Contracts) par laquelle la console obtient ce contenu
/// avant transmission ; l'implémentation réutilise la SOURCE UNIQUE de la classification fiscale (jamais une
/// seconde déduction divergente — CLAUDE.md n°2). Tenant-scopée (le tenant est résolu par la requête).
/// </summary>
public interface IDocumentContentReplayService
{
    /// <summary>
    /// Relit + rejoue le mapping du document <paramref name="documentId"/> du tenant courant et retourne le pivot
    /// à afficher (ENRICHI si le mapping passe, SOURCE — régime lu, catégorie/VATEX vides — s'il bloque ; c'est le
    /// diagnostic FACTUEL d'un blocage, jamais une valeur inventée). Retourne <see cref="DocumentContentReplay.Unavailable"/>
    /// quand le pivot source stagé n'est plus disponible (purgé après émission, absent, ou intégrité KO) : l'appelant
    /// retombe alors sur le snapshot transmis. AUCUNE transition d'état, AUCUNE écriture — lecture pure.
    /// </summary>
    Task<DocumentContentReplay> ReplayAsync(Guid documentId, CancellationToken cancellationToken = default);
}
