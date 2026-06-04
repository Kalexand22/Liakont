namespace Liakont.Modules.Archive.Contracts;

/// <summary>
/// État de vérification d'une preuve d'ancrage temporel (TRK06). Distingue une preuve réellement
/// INVALIDE (altérée, manquante, orpheline, ou forgée) d'une preuve simplement NON VÉRIFIABLE par la
/// configuration courante (méthode différente de l'ancrage configuré) : confondre les deux ferait
/// conclure à tort à une falsification du coffre (faux négatif alarmant) et casserait le « ceinture-bretelles »
/// (ancrages simultanés) prévu par la spec.
/// </summary>
public enum ArchiveAnchorVerificationStatus
{
    /// <summary>Preuve valide (signature + empreinte) et authentifiée selon la configuration d'instance.</summary>
    Valid,

    /// <summary>Preuve INVALIDE : altérée, manquante, orpheline, ou signataire non conforme à la TSA épinglée.</summary>
    Invalid,

    /// <summary>Preuve d'une méthode que l'instance courante ne sait pas vérifier : conservée, non comptée comme altération.</summary>
    NotVerifiable,
}
