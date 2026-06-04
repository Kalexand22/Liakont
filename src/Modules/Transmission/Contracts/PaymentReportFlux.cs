namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Type de flux d'e-reporting de paiement (F01-F02 §1). Les deux flux ont des routages et des
/// schémas DGFiP DIFFÉRENTS : ils sont distingués explicitement (acceptance PAA01).
/// NOTE décision D2 (2026-06-03) : en V1, le pipeline (PIP03) n'alimente QUE le flux Domestic (10.4) ;
/// International reste une capacité déclarable, prête pour la phase 2.
/// </summary>
public enum PaymentReportFlux
{
    /// <summary>Paiements domestiques (B2C) — flux 10.4.</summary>
    Domestic = 1,

    /// <summary>Paiements internationaux — flux 10.2.</summary>
    International = 2,
}
