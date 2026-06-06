namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System.Globalization;

/// <summary>Compteurs d'une exécution SEND, consignés dans <c>pipeline.run_logs</c>.</summary>
internal sealed class SendTally
{
    /// <summary>Documents émis avec succès.</summary>
    public int Succeeded { get; private set; }

    /// <summary>Documents en échec (rejet PA, erreur technique, intégrité).</summary>
    public int Failed { get; private set; }

    /// <summary>Documents différés (contenu pas encore stagé — transitoire).</summary>
    public int Deferred { get; private set; }

    /// <summary>Documents ignorés (état changé, avoir sans capacité PA…).</summary>
    public int Skipped { get; private set; }

    /// <summary>Documents pris en compte (dénombrement direct en dry-run, sinon dérivé des issues).</summary>
    public int Processed { get; set; }

    /// <summary>Comptabilise une issue de document et met à jour le total traité.</summary>
    public void Add(SendOutcome outcome)
    {
        switch (outcome)
        {
            case SendOutcome.Succeeded:
                Succeeded++;
                break;
            case SendOutcome.Failed:
                Failed++;
                break;
            case SendOutcome.Deferred:
                Deferred++;
                break;
            default:
                Skipped++;
                break;
        }

        Processed = Succeeded + Failed + Deferred + Skipped;
    }

    /// <summary>Détail lisible de l'exécution pour le journal d'exécutions.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"SEND : {Succeeded} émis, {Failed} en échec, {Deferred} différés (staging absent), {Skipped} ignorés.");
}
