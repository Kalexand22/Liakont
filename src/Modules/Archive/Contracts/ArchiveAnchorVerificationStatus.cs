namespace Liakont.Modules.Archive.Contracts;

/// <summary>
/// État de vérification d'une preuve d'ancrage temporel (TRK06). Distingue une preuve réellement
/// INVALIDE d'une preuve simplement NON VÉRIFIABLE par la configuration courante, et — parmi les preuves
/// valides — celles dont la TSA est AUTHENTIFIÉE (certificat épinglé) de celles seulement
/// cohérentes mais non authentifiées : seul un ancrage <see cref="Valid"/> allume le « coffre ancré ».
/// </summary>
public enum ArchiveAnchorVerificationStatus
{
    /// <summary>Preuve valide (signature + empreinte) ET TSA authentifiée (certificat épinglé vérifié).</summary>
    Valid,

    /// <summary>
    /// Preuve cohérente (signature + empreinte valides) mais TSA NON épinglée : l'identité de l'autorité
    /// n'est pas garantie (un jeton forgé auto-signé reste possible). Ne compte pas comme « coffre ancré ».
    /// </summary>
    ValidUnauthenticated,

    /// <summary>Preuve INVALIDE : altérée, manquante, orpheline, ou signataire non conforme à la TSA épinglée.</summary>
    Invalid,

    /// <summary>Preuve d'une méthode que l'instance courante ne sait pas vérifier : conservée, non comptée comme altération.</summary>
    NotVerifiable,
}
