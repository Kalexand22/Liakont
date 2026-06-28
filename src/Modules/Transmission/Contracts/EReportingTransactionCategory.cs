namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Catégorie de transaction e-reporting B2C (donnée TT-81 « Catégorie des transactions »,
/// <c>/TransactionsReport/Transactions/CategoryCode</c>, flux 10.3). Liste <b>FERMÉE</b> des 4 valeurs de
/// la règle DGFiP <b>G1.68</b> (Annexe 7 — Règles de gestion V1.9 ; figée F03 §2.6) — toute autre valeur
/// est invalide (fail-closed, CLAUDE.md n°2/3). Le code de fil canonique est porté par
/// <see cref="EReportingCodes"/> ; la projection vers une PA concrète vit dans son plug-in (frontière n°6).
/// </summary>
public enum EReportingTransactionCategory
{
    /// <summary>TLB1 — Livraisons de biens soumises à la TVA (G1.68).</summary>
    Tlb1 = 1,

    /// <summary>TPS1 — Prestations de services soumises à la TVA (G1.68).</summary>
    Tps1 = 2,

    /// <summary>TNT1 — Biens et services non soumis à la TVA en France (ventes à distance intracom, art. 258 A I-1° / 259 B CGI ; G1.68).</summary>
    Tnt1 = 3,

    /// <summary>TMA1 — Opérations sous régime de TVA sur la marge (art. 266-1-e, 268, 297 A CGI ; G1.68). Cas enchères, F03 §2.4/§2.5.</summary>
    Tma1 = 4,
}
