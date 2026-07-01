namespace Liakont.Host.Backfill;

/// <summary>
/// Déclencheur SYSTÈME (charge utile vide, base système) du backfill rétroactif GED du corpus fiscal déjà scellé
/// (GED10, F19 §11 D12) ; son handler <see cref="GedCorpusBackfillFanOutHandler"/> fait le fan-out sur tous les
/// tenants actifs (SOL06). GESTE OPÉRÉ, non planifié par défaut (cadence de déploiement — SystemJobDefinitions).
/// </summary>
public sealed record GedCorpusBackfillTrigger;
