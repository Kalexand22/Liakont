namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System.Globalization;

/// <summary>Compteurs d'une exécution SEND, consignés dans <c>pipeline.run_logs</c>.</summary>
internal sealed class SendTally
{
    /// <summary>Documents émis avec succès.</summary>
    public int Succeeded { get; private set; }

    /// <summary>Documents en échec (rejet PA, erreur technique, intégrité).</summary>
    public int Failed { get; private set; }

    /// <summary>Documents différés (contenu pas encore stagé, ou dépôt asynchrone en cours — transitoire).</summary>
    public int Deferred { get; private set; }

    /// <summary>Documents en attente d'une ACTION OPÉRATEUR (émetteur non résolu / catégorie TVA non reposée) —
    /// HOLD distinct du différé transitoire : jamais présenté comme « en cours d'émission » (CLAUDE.md n°3).</summary>
    public int Held { get; private set; }

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
            case SendOutcome.Held:
                Held++;
                break;
            default:
                Skipped++;
                break;
        }

        Processed = Succeeded + Failed + Deferred + Held + Skipped;
    }

    /// <summary>Détail lisible de l'exécution pour le journal d'exécutions. « différés » = documents
    /// téléversés dont le contenu n'est pas encore stagé (ou dépôt asynchrone accepté) : ils sont EN COURS
    /// D'ÉMISSION (transitoire, émis au prochain cycle), pas en anomalie — le wording le dit pour ne pas
    /// alarmer l'opérateur (RBF07). « en attente de paramétrage » = HOLD exigeant une action opérateur
    /// (SIREN publié / table TVA validée) : signalé avec son action corrective, jamais noyé dans les différés.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"SEND : {Succeeded} émis, {Failed} en échec, {Deferred} différés (en cours d'émission), {Held} en attente de paramétrage (SIREN publié / table TVA validée), {Skipped} ignorés.");
}
