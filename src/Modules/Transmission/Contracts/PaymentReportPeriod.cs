namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Période d'un e-reporting de paiement, avec son TYPE DE FLUX (F01-F02 §1). DTO neutre : il ne
/// porte qu'un intervalle de dates et un flux — aucune règle fiscale (cadence, régime, seuil) n'est
/// inventée ici (CLAUDE.md n°2) ; la cadence et le découpage relèvent du paramétrage du tenant et du
/// pipeline (PIP03).
/// </summary>
public sealed record PaymentReportPeriod
{
    /// <summary>Type de flux du reporting (domestique = 10.4 / international = 10.2).</summary>
    public required PaymentReportFlux Flux { get; init; }

    /// <summary>Premier jour de la période couverte (inclus).</summary>
    public required DateOnly PeriodStart { get; init; }

    /// <summary>Dernier jour de la période couverte (inclus).</summary>
    public required DateOnly PeriodEnd { get; init; }
}
