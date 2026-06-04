namespace Liakont.Modules.Archive.Domain;

using System;

/// <summary>
/// Résultat de la vérification d'une preuve d'ancrage (TRK06). <see cref="IsValid"/> est <c>true</c>
/// quand la signature du service d'horodatage est valide ET que l'empreinte scellée correspond à la tête
/// de chaîne attendue. <see cref="IsAuthorityAuthenticated"/> distingue, PARMI les preuves valides, celles
/// dont l'autorité d'horodatage a été AUTHENTIFIÉE (certificat épinglé) de celles signées par une TSA non
/// épinglée (signature cohérente mais identité non garantie — un jeton forgé auto-signé reste possible) :
/// le vérifieur n'allume le « coffre ancré » que sur une preuve authentifiée.
/// </summary>
/// <param name="IsValid">La preuve est valide (signature + empreinte) et atteste bien l'empreinte attendue.</param>
/// <param name="IsAuthorityAuthenticated">L'autorité d'horodatage est authentifiée (certificat épinglé vérifié).</param>
/// <param name="AnchoredUtc">Instant attesté par la preuve (UTC), ou <c>null</c>.</param>
/// <param name="Detail">Message français en cas d'anomalie, ou de confirmation.</param>
public sealed record TimestampVerification(
    bool IsValid,
    bool IsAuthorityAuthenticated,
    DateTimeOffset? AnchoredUtc,
    string Detail);
