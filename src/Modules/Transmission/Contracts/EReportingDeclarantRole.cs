namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Rôle du déclarant dans un e-reporting de transaction (donnée TT-15 « Code rôle »,
/// <c>/ReportDocument/Issuer/RoleCode</c>, règle DGFiP <b>G7.52</b> ; Annexe 6 — Format sémantique
/// e-reporting V1.10). Liste fermée : <see cref="Seller"/> (« SE », déclarant Vendeur) ou
/// <see cref="Buyer"/> (« BY », déclarant Acheteur). Pour l'e-reporting B2C des <b>ventes</b> (cas enchères /
/// régime de la marge, F03 §2.5), le déclarant est Vendeur → <see cref="Seller"/>.
/// </summary>
public enum EReportingDeclarantRole
{
    /// <summary>BY — le déclarant est l'Acheteur (G7.52).</summary>
    Buyer = 1,

    /// <summary>SE — le déclarant est le Vendeur (G7.52).</summary>
    Seller = 2,
}
