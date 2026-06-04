namespace Liakont.Modules.TvaMapping.Domain.Mapping;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Résultat du mapping TVA d'une part de ligne (item TVA02). Deux issues mutuellement exclusives :
/// <list type="bullet">
/// <item><b>Mappé</b> (<see cref="IsMapped"/> = <c>true</c>) : une règle de la table du tenant a été
/// appliquée → <see cref="Category"/>, <see cref="RateMode"/>/<see cref="Rate"/>, <see cref="Vatex"/>
/// et la <see cref="Trace"/> d'audit sont renseignés.</item>
/// <item><b>Bloqué</b> (<see cref="IsMapped"/> = <c>false</c>) : aucune règle applicable (régime absent
/// ou flags non satisfaits) → seul <see cref="BlockReason"/> est renseigné. Conformément à
/// <c>defaultBehavior=block</c> (F03 §4.1, INV-007), le moteur ne devine JAMAIS une catégorie : il
/// bloque, pour ne jamais transmettre un motif fiscal faux (CLAUDE.md n°2/3).</item>
/// </list>
/// </summary>
public sealed class MappingResult
{
    private MappingResult(
        bool isMapped,
        VatCategory? category,
        RateMode? rateMode,
        decimal? rate,
        string? vatex,
        MappingTrace? trace,
        string? blockReason)
    {
        IsMapped = isMapped;
        Category = category;
        RateMode = rateMode;
        Rate = rate;
        Vatex = vatex;
        Trace = trace;
        BlockReason = blockReason;
    }

    /// <summary><c>true</c> si une règle a été appliquée ; <c>false</c> si le document est bloqué.</summary>
    public bool IsMapped { get; }

    /// <summary>Catégorie de TVA produite (UNCL5305) ; <c>null</c> quand bloqué.</summary>
    public VatCategory? Category { get; }

    /// <summary>Mode de taux de la règle appliquée ; <c>null</c> quand bloqué.</summary>
    public RateMode? RateMode { get; }

    /// <summary>
    /// Taux résolu (pourcentage, <c>decimal</c> exact) quand <see cref="RateMode"/> vaut
    /// <see cref="Entities.RateMode.Fixed"/> ; <c>null</c> quand le taux est
    /// <see cref="Entities.RateMode.ComputedFromSource"/> (résolu en aval à partir des montants de la
    /// ligne, F03 §4.1) ou quand le document est bloqué.
    /// </summary>
    public decimal? Rate { get; }

    /// <summary>Code VATEX produit (motif d'exonération) ; <c>null</c> si non applicable ou bloqué.</summary>
    public string? Vatex { get; }

    /// <summary>Trace d'audit du mapping ; renseignée si et seulement si <see cref="IsMapped"/>.</summary>
    public MappingTrace? Trace { get; }

    /// <summary>
    /// Motif de blocage (message opérateur français, avec action corrective — CLAUDE.md n°12) ;
    /// renseigné si et seulement si <see cref="IsMapped"/> est <c>false</c>.
    /// </summary>
    public string? BlockReason { get; }

    /// <summary>Crée un résultat « mappé » à partir de la règle appliquée et de sa trace.</summary>
    public static MappingResult Mapped(
        VatCategory category,
        RateMode rateMode,
        decimal? rate,
        string? vatex,
        MappingTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        return new MappingResult(true, category, rateMode, rate, vatex, trace, blockReason: null);
    }

    /// <summary>Crée un résultat « bloqué » porteur du motif opérateur.</summary>
    public static MappingResult Blocked(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MappingResult(false, category: null, rateMode: null, rate: null, vatex: null, trace: null, reason);
    }
}
