# Jobs multi-tenant — patron `ITenantJob` / `TenantJobRunner`

> Mécanique **unique** de balayage des tenants pour les travaux de fond (SOL06). Aucun module ne
> réinvente sa propre boucle « pour chaque tenant ». **Sources** : `orchestration/items/SOL.yaml`
> (SOL06), `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md`, `blueprint.md` §7,
> `docs/architecture/module-rules.md` §6, `CLAUDE.md` n°9.
>
> Documents frères : [`module-rules.md`](module-rules.md) §6 (multi-tenancy),
> [`testing-strategy.md`](testing-strategy.md).

---

## 1. Le problème

La plateforme est en **database-per-tenant** (ADR-0011). Le module `Job` vendored exécute ses jobs
dans la base du tenant **courant** (connexion routée par `IConnectionFactory` → `ITenantContext`),
mais il n'a **aucune** notion de « tous les tenants » : il ne sait pas itérer sur le catalogue
d'instance ni basculer la connexion d'un tenant à l'autre.

Or plusieurs travaux de fond sont **par tenant, pour tous les tenants** : ancrage quotidien
(TRK06), jobs planifiés (PIP01/PIP03), évaluation de supervision (SUP01). La mécanique commune
`TenantJobRunner` couvre ce besoin une seule fois.

## 2. Les pièces (toutes dans `Stratum.Common.*`)

| Type | Assembly | Rôle |
|---|---|---|
| `ITenantJob` | `Common.Abstractions` (`Jobs/`) | Le travail à faire **pour un seul tenant**. `Name` (diagnostic) + `ExecuteAsync(TenantJobContext, ct)`. |
| `TenantJobContext` | `Common.Abstractions` (`Jobs/`) | Contexte d'une invocation : `TenantId` + `Services` (le `IServiceProvider` du scope, déjà routé vers la base du tenant). |
| `ITenantJobRunner` | `Common.Abstractions` (`Jobs/`) | `RunForAllTenantsAsync(job, ct)` : exécute le job pour **chaque tenant actif**. |
| `TenantJobRunSummary` / `TenantJobFailure` | `Common.Abstractions` (`Jobs/`) | Bilan : total, succès, échecs par tenant. |
| `TenantJobRunner` | `Common.Infrastructure` (`Jobs/`) | Implémentation. Enregistrée par `AddTenantJobs()`. |
| `ITenantScopeFactory` / `ITenantScope` | `Common.Abstractions` (`MultiTenancy/`) | Seam de basculement de tenant ; **implémenté dans le Host** (composition root). |

## 3. Garanties du runner

- **Tenants actifs uniquement** : `ITenantQueries.ListAsync()` filtré sur `IsActive` (catalogue
  `outbox.tenants`, base système).
- **Connexion basculée par tenant** : chaque tenant s'exécute dans un scope dédié dont
  `IConnectionFactory` pointe vers SA base. Les repositories scoped d'un module fonctionnent donc
  sans modification.
- **Isolation des échecs** : l'échec d'un tenant est journalisé avec son `TenantId` et **n'arrête
  pas** les autres ; il figure dans `TenantJobRunSummary.Failures`.
- **Pas de fuite de connexion** : le scope (et donc la connexion) est libéré après chaque tenant.
- **Annulation** : le `CancellationToken` de l'appelant interrompt tout le run (jamais avalé comme un
  échec de tenant).
- **Signal d'un run PARTIEL** (RDL07, ADR-0006 §4) : un run comportant au moins un échec se **termine**
  (pas de retry/dead-letter — l'escalade des cas à enjeu passe par les états document `Blocked`/
  `RejectedByPa` et le dead-man's-switch, règles sourcées F12 §5.2), mais le runner émet un `Warning`
  structuré portant le job et les tenants en échec. `TenantJobRunSummary` expose `HasFailures` pour le
  handler appelant. Aucune règle d'alerte n'est inventée (le catalogue F12 §5.2 est fermé).
- **0 tenant actif = anomalie potentielle** (RDL07) : un catalogue d'instance vide (bug de
  provisioning, mauvaise base) est loggué en `Warning` distinct (`HadNoActiveTenants`), pas en
  `Information` indistinct d'un run normal.

> Le runner **n'établit jamais le tenant lui-même** dans une couche métier : il passe par
> `ITenantScopeFactory`, dont l'implémentation vit dans le Host (seul autorisé à muter le contexte
> de tenant — `module-rules.md` §6, ADR-0006).

## 4. Patron d'utilisation (consommateurs : TRK06, PIP01, PIP03, SUP01)

Un module ne planifie **pas** un job par tenant. Il planifie **un** job **système** (via le module
`Job` sur la base système) dont le handler appelle le runner :

```csharp
// 1) Le travail pour UN tenant (Application/Infrastructure du module consommateur).
public sealed class DailyAnchoringTenantJob : ITenantJob
{
    public string Name => "trk.daily-anchoring";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken ct)
    {
        // context.Services est routé vers la base du tenant : on résout les services scoped normaux.
        var anchoring = context.Services.GetRequiredService<IAnchoringService>();
        await anchoring.AnchorPendingAsync(ct); // requêtes tenant-scopées habituelles
    }
}

// 2) Le déclencheur : un job SYSTÈME planifié (JobScheduler du module Job), dont le handler
//    fait le fan-out sur tous les tenants via le runner.
public sealed class DailyAnchoringFanOutHandler : IJobHandler<DailyAnchoringTrigger>
{
    private readonly ITenantJobRunner _runner;

    public DailyAnchoringFanOutHandler(ITenantJobRunner runner) => _runner = runner;

    public async Task HandleAsync(DailyAnchoringTrigger payload, CancellationToken ct)
    {
        var summary = await _runner.RunForAllTenantsAsync(new DailyAnchoringTenantJob(), ct);
        // summary.Failures : à journaliser / alerter (Supervision) si non vide.
    }
}
```

**À ne jamais faire** : itérer soi-même sur les tenants, ou ouvrir une connexion par tenant à la
main dans un handler métier. C'est une violation de frontière (P1, `module-rules.md` §6).

## 5. Enregistrement

- `AddTenantJobs()` (extension `Common.Infrastructure.Jobs`) enregistre `ITenantJobRunner`. Appelée
  par le Host (`AppBootstrap`).
- `ITenantScopeFactory` est enregistré par `AddStratumMultiTenancy` (Host) — son implémentation
  `TenantScopeFactory` positionne le `MutableTenantContext` du scope, comme le middleware.
- En test, on fournit un `ITenantScopeFactory` de test qui construit un scope routé via le vrai
  `TenantScopedConnectionFactory` (voir `TenantJobRunnerIntegrationTests`, 2 bases tenant réelles).

## 6. Tests (SOL06)

- **Unit** (`Common.Infrastructure.Tests.Unit/Jobs`) : filtre des tenants actifs, isolation des
  échecs, bilan, disposal du scope par tenant, propagation de l'annulation.
- **Intégration** (`Common.Infrastructure.Tests.Integration/Jobs`, Testcontainers, 2 bases tenant) :
  basculement de connexion par tenant (`current_database()` distinct), isolation des données (aucune
  fuite cross-tenant), isolation des échecs au travers de deux bases réelles.
