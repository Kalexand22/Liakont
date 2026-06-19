# ADR-0006 — Mécanique de jobs multi-tenant dans `src/Common`, basculement de tenant derrière une abstraction

- **Statut** : Accepté (2026-06-04). Mise en œuvre : item SOL06.
  Amendé (2026-06-19, item RDL07) : §4 ajouté (sémantique d'un run de fan-out partiel + observabilité).
  Amendé (2026-06-19, item RDL08) : §5 ajouté (robustesse à l'échelle — budget par tenant, dé-duplication à
  l'enqueue, contrat de reprise après crash/annulation).
- **Date** : 2026-06-04
- **Contexte décisionnel** : `orchestration/items/SOL.yaml` (SOL06), `blueprint.md` §7 (multi-tenancy),
  `docs/architecture/module-rules.md` §6, `docs/conception/F12`, `CLAUDE.md` n°9 (requêtes
  tenant-scopées), `tasks/analyse-impact-pivot-plateforme.md` (constat : le module Job vendored n'a
  pas de résolution de tenant), `docs/architecture/provenance-socle-stratum.md` §4.4 et §4.12.
  Amendement §4 : `orchestration/items/RDL.yaml` (RDL07), `tasks/redline-adr-005-006-007.md`
  (A6-runtime-1, A6-cons-2, A6-runtime-3).

## Contexte

Le module `Job` du socle Stratum vendored crée un scope **sans** résolution de tenant (vérifié sur
pièce : aucune référence à `ITenantContext`, ses tables `job.*` vivent dans la base de chaque tenant,
le `JobWorker` consomme un `IConnectionFactory` scoped déjà routé). Avec **database-per-tenant**
(ADR-0011 Stratum), un travail de fond ne peut ni s'exécuter dans la base d'un tenant donné ni
**itérer sur tous les tenants** sans mécanique supplémentaire.

Quatre items en dépendent : **TRK06** (ancrage quotidien par tenant), **PIP01/PIP03** (jobs planifiés
par tenant), **SUP01** (évaluation de tous les tenants). Sans mécanique commune, chacun
réinventerait sa propre boucle de balayage des tenants — source de fuite cross-tenant (un `if`
oublié = données d'un client visibles par un autre). SOL06 spécifie et implémente cette mécanique
**une seule fois**.

Deux décisions sont à acter (le périmètre SOL06 demande explicitement le choix de placement par ADR) :

1. **Où vit la mécanique** : `src/Common` ou un nouveau module sous `src/Modules/` ?
2. **Comment basculer le tenant** d'un scope de fond sans violer l'invariant existant du socle :
   le contexte de tenant **mutable** (`MutableTenantContext`) est délibérément `internal` au Host
   « pour empêcher les couches domaine/application de muter le contexte de tenant ».

## Décision

### 1. La mécanique vit dans `src/Common` (pas un nouveau module)

`TenantJobRunner` et son contrat (`ITenantJob`, `TenantJobContext`, `ITenantJobRunner`,
`TenantJobRunSummary`, `TenantJobFailure`) sont placés dans les projets **vendored** `Common` :

- **Contrats** dans `Stratum.Common.Abstractions` (dossier `Jobs/`) — référencé par TOUS les modules.
- **Implémentation** dans `Stratum.Common.Infrastructure` (dossier `Jobs/`) + extension
  `AddTenantJobs()`.

Critère du périmètre SOL06 : « réutilisable par tous les modules sans dépendance circulaire ».
`Common` est l'assembly de base que tout module référence déjà ; un consommateur (TRK/PIP/SUP)
implémente un `ITenantJob` (réfère `Common.Abstractions`) et appelle `ITenantJobRunner` — aucune
référence inter-modules, aucun cycle. La mécanique ne dépend **pas** du module `Job` (elle ne fait
que balayer les tenants ; le déclenchement « côté base système » reste à la charge du consommateur,
via le `JobScheduler`/`IJobQueue` du module Job sur la base système).

Conséquence : **pas** de `MODULE.md`/`INVARIANTS.md`/`SCENARIOS.md` — cette obligation
(`module-rules.md` §11) ne vise que les modules sous `src/Modules/`.

Les fichiers ajoutés sous `src/Common` sont des **ajouts** (non des modifications de fichiers
`Stratum.*` existants) : le baseline de provenance (`tools/socle-provenance-check.ps1`,
voir provenance §4.12) ne les épingle pas, donc aucune fausse dérive ; ils sont consignés en
provenance §4.14 et marqués `// Liakont addition (SOL06)` pour garder la re-convergence NuGet claire.

### 2. Le basculement de tenant passe par une abstraction `ITenantScopeFactory` implémentée dans le Host

`TenantScopedConnectionFactory` (Common) lit `ITenantContext` (lecture seule) pour router la
connexion. Pour qu'un scope de fond pointe vers la base d'un tenant, il faut **établir** le tenant
sur ce scope — exactement ce que font le `TenantMiddleware` et le `TenantCircuitHandler` (qui
résolvent le `MutableTenantContext` concret, `internal` au Host, et le positionnent).

On introduit donc un **seam** :

- `ITenantScopeFactory.Create(tenantId) : ITenantScope` (abstraction, `Common.Abstractions`).
- L'**implémentation** `TenantScopeFactory` vit dans le **Host** (composition root) : c'est le seul
  endroit qui peut muter le contexte de tenant. Elle crée un scope DI et positionne le
  `MutableTenantContext` — même mécanique que le middleware.
- `TenantJobRunner` (Common) ne dépend que de `ITenantScopeFactory` + `ITenantQueries`. Les couches
  domaine/application ne peuvent toujours pas muter le tenant ; elles ne peuvent qu'**appeler** le
  runner, qui établit le tenant via le seam fourni par le composition root.

Ainsi l'invariant « seul le composition root établit le tenant » est **préservé**, tout en ajoutant
le runner comme **deuxième établisseur sanctionné** (l'analogue, côté jobs de fond, du middleware).

### 3. Comportement du runner

- Liste les tenants **actifs** (`ITenantQueries.ListAsync()` filtré sur `IsActive`) depuis le
  catalogue d'instance (`outbox.tenants`, base système).
- Pour chaque tenant : un scope dédié (connexion basculée), exécution de l'`ITenantJob`, puis
  disposal du scope (pas de fuite de connexion).
- **Isolation des échecs** : l'échec du tenant N est journalisé avec son `TenantId` et **n'interrompt
  pas** les suivants ; il est reporté dans `TenantJobRunSummary.Failures`.
- L'annulation (`CancellationToken` de l'appelant) **interrompt tout le run** (elle n'est pas avalée
  comme un échec de tenant).

### 4. Sémantique d'un run de fan-out PARTIEL + observabilité (amendement RDL07)

La revue critique (A6-runtime-1) a posé la question : quand un fan-out échoue pour une PARTIE des
tenants, les handlers loguent puis retournent sans `throw` → le `JobWorker` marque le job
`completed`, **sans retry ni dead-letter**. Faut-il escalader (retry/dead-letter) ? Et faut-il une
**règle d'alerte** dédiée pour l'ancrage/purge échoués ?

**Décision (tranchée ici, sévérité corrigée P1→P3) : un run partiel se TERMINE — pas de retry ni de
dead-letter automatique — et l'échec partiel est porté par un SIGNAL STRUCTURÉ, jamais un faux-vert
silencieux. On N'AJOUTE PAS de règle d'alerte de supervision dédiée.**

Justification :

1. **L'escalade des cas à enjeu existe DÉJÀ, par conception, et reste SOURCÉE.** Un envoi (SEND) à
   moitié échoué n'est pas invisible : les documents concernés restent en état `Blocked` /
   `RejectedByPa`, couverts par des règles d'alerte **sourcées F12 §5.2** (`documents.blocked`,
   `documents.pa_rejected`) ; l'absence prolongée d'agent ou de run d'extraction est couverte par le
   dead-man's-switch (`agent.mute`, `agent.missed_run`). Un retry/dead-letter générique au niveau du
   runner doublerait cette escalade sans la rendre plus fiable (et masquerait l'erreur de fond).
2. **Pas de nouvelle règle d'alerte tenant** : le catalogue des règles de supervision est
   l'**ensemble fermé de F12 §5.2** (`AlertRuleCatalog` — « aucune règle inventée », CLAUDE.md n°2).
   Ajouter « ancrage quotidien échoué pour tenant X » ou « purge support échouée » comme règle
   d'alerte tenant serait une règle INVENTÉE hors spec — interdit. Le résidu (ancrage TRK06, purge
   FX06) est donc couvert par le signal structuré ci-dessous, pas par une 9ᵉ règle.
3. **Le signal structuré, uniforme pour TOUS les fan-out** : `TenantJobRunner` émet, à la fin d'un
   run comportant au moins un échec, un `Warning` unique (`TenantJobRunSummary.HasFailures`) portant
   le nom du job, les compteurs et la liste des tenants en échec. C'est observable par la supervision
   d'exploitation (logs structurés + télémétrie d'instance / méta-supervision OPS04) sans inventer de
   règle métier. `TenantJobRunSummary` expose `HasFailures` et `HadNoActiveTenants` pour que le
   handler appelant décide en connaissance de cause.
4. **0 tenant actif = anomalie potentielle** (A6-runtime-3) : un catalogue d'instance vide (bug de
   provisioning, mauvaise base) est désormais loggué en `Warning` distinct (et non plus en
   `Information` indistinct d'un run normal), via `HadNoActiveTenants`.

**Filet de démarrage complémentaire (A6-cons-2)** : `SystemJobScheduleHealthCheck` couvre désormais
**tous** les jobs de fan-out récurrents (`SystemJobDefinitions`), répartis en deux classes :

- `RequiredSeeded` : cadence **sourcée** (supervision 15 min F12 §5.1 ; ancrage quotidien TRK06,
  ADR-0011), amorcée en dev. Absente au démarrage → `Warning` « doit être planifié ».
- `DeploymentCadence` : récurrent mais **cadence = geste de déploiement** (aucune n'est sourcée → pas
  de cron inventé, non amorcée) — envoi/sync/agrégation/rectificatifs (PIP), réconciliation (TRK07),
  purge de trace (FX06), drain de signature (SIG09), bascule tacite 389 (MND04). Absente au démarrage
  → `Warning` conditionnel « à planifier si vous utilisez cette fonctionnalité » : un job de fan-out
  jamais planifié (« job mort en production ») ne reste plus un faux-vert.

Le récapitulatif de supervision (SUP03, opt-in désactivé par défaut) et la méta-supervision de flotte
(OPS04, opt-in, non des fan-out par tenant) restent **hors** de ce filet (un avertissement serait du
bruit).

### 5. Robustesse à l'échelle : budget par tenant, dé-duplication à l'enqueue, contrat de reprise (amendement RDL08)

La revue critique (A6-scale-1/2/3/5, A6-runtime-2/4, A6-di-2) a relevé que le runner, robuste sur deux
tenants, présente des angles morts à l'échelle. Décisions tranchées ici :

#### 5.1 Contrat de reprise après crash/annulation (A6-scale-1, A6-runtime-2)

Le module `Job` vendored n'a **pas de reaper** : si le processus meurt pendant un fan-out (ou est annulé au
shutdown), le `JobEntry` reste `Running` (le worker ne reprend que les `Pending`), et le `TenantJobRunSummary`
partiel est perdu (l'`OperationCanceledException` de l'appelant interrompt tout le run, §3).

**Décision : PAS de reaper ajouté au socle. La reprise se fait par RE-EXÉCUTION au tick suivant, garantie sûre
par l'IDEMPOTENCE des `ITenantJob`.** C'est un **contrat** imposé à tout `ITenantJob` : ré-exécuter le job
pour un tenant déjà (partiellement) traité doit converger sans effet de bord nuisible (l'ancrage TRK06
re-scelle ce qui ne l'est pas et ignore ce qui l'est ; la purge FX06 est idempotente ; etc.). Un run crashé
n'est donc pas « perdu » : le `JobEntry` `Running` orphelin est *superseded* par le prochain déclenchement
cron, qui ré-enqueue une entrée fraîche (cf. §5.2, dé-dup Pending-only) que le worker exécute. Le travail déjà
committé dans chaque base tenant est durable ; le summary partiel est un diagnostic, pas un état à persister.

Justification du non-reaper : un reaper générique (`Running` expiré → `Pending`/`Dead`) dans le `JobWorker`
vendored serait une dérive de socle significative pour un gain nul ici — la re-exécution idempotente au tick
suivant couvre déjà la reprise, et l'escalade des cas à enjeu reste portée par les états document + le
dead-man's-switch (§4).

#### 5.2 Dé-duplication à l'enqueue, **Pending-only** (A6-scale-2)

Un fan-out plus long que sa cadence cron (supervision `*/15`) empilerait, à chaque tick, un déclencheur
identique → la file du worker mono-job (`BatchSize=1`) se remplit de doublons. **Décision : le scheduler
récurrent consulte une garde (`IRecurringJobEnqueueGuard`, impl. Host) avant d'enqueuer ; il SAUTE l'enqueue
s'il existe déjà un job du même type ET de la même portée tenant (`company_id`) en statut `Pending`** — puis
avance `next_run_at` à la prochaine échéance cron (respect de la cadence, pas de ré-essai en boucle). La
détection est `IJobQueries.HasPendingJobOfTypeAsync` (lecture seule, base système).

**Volontairement `Pending` SEULEMENT, jamais `Running`.** Dé-duper contre `Running` introduirait un
**deadlock** : un `Running` orphelin (crash, §5.1, aucun reaper) bloquerait à jamais le ré-enqueue → l'ancrage
WORM quotidien s'arrêterait silencieusement (grave pour un produit fiscal). Pending-only **borne** la file à
*au plus un Running + un Pending* par type (élimine l'empilement non borné, A6-scale-2) tout en laissant un run
crashé reprendre au tick suivant (§5.1). Le seul écart au littéral « ni Pending ni Running » est qu'un unique
Pending peut coexister avec un Running en cours — bénin et idempotent.

**Granularité de la clé : `(type de job, company_id)`.** C'est suffisant ET correct pour les fan-out SYSTÈME
récurrents, où **un seul schedule existe par type** (ancrage quotidien, évaluation de la supervision, etc. —
`SystemJobDefinitions`, `company_id` système). C'est une **contrainte assumée** : deux schedules distincts
partageant le même `JobType` et la même portée tenant se dé-dupliqueraient mutuellement. La discriminer par
`schedule.Id` n'est PAS souhaitable (deux schedules du même type de fan-out empileraient à nouveau le worker —
le défaut même que A6-scale-2 corrige). Si un besoin futur exigeait plusieurs cadences pour un même type, il
faudrait des types de job distincts, pas une clé de dé-dup plus fine.

#### 5.3 Budget de temps par tenant (A6-scale-3, A6-runtime-4)

`TenantJobRunner` accepte un `TenantJobRunnerOptions.PerTenantTimeout` optionnel : quand il est posé, chaque
`job.ExecuteAsync` d'un tenant tourne sous un `CancellationTokenSource` lié (linked CTS) ; un tenant qui dépasse
le budget devient un `TenantJobFailure` **isolé** (la `OperationCanceledException` du budget est convertie en
`TimeoutException`), sans interrompre les tenants suivants. Le filtre d'`OperationCanceledException` clé sur le
token de l'**appelant** spécifiquement (`cancellationToken.IsCancellationRequested`) — un budget par tenant
n'est jamais confondu avec une annulation d'appelant, et une annulation d'appelant a toujours **précédence**
(elle abandonne tout le run, A6-runtime-4).

**Opt-in, désactivé par défaut (`null`)** : un budget par défaut agressif transformerait un ancrage WORM
légitimement lent (gros lot, base saturée) en faux échec → piste d'audit non scellée. L'opérateur fixe le
budget par déploiement (`TenantJobs:PerTenantTimeout`) à partir des durées observées. Règle produit : ne jamais
auto-échouer un scellement valide.

#### 5.4 Témoin de vie au grain run-entier (A6-scale-5) — accepté

Le dead-man's-switch lit l'achèvement du fan-out ENTIER (`GetLastCompletedAtByTypeAsync`, base système), pas la
fraîcheur par-tenant. Une fraîcheur par-tenant exigerait un suivi d'exécution tenant-scopé (changement
d'architecture) ; à l'échelle visée ce n'est pas justifié. Le résidu (un tenant en échec dans un run par
ailleurs réussi) est **déjà** porté par le signal structuré `HasFailures` (§4) + les états document. Décision :
**conserver le grain run-entier** ; ne pas inventer de règle d'alerte par-tenant (catalogue F12 §5.2 fermé).

## Conséquences

**Positif** : une mécanique unique, testée (unit + intégration sur 2 bases tenant), réutilisable par
TRK06/PIP01/PIP03/SUP01 sans réinventer de boucle de tenants ; invariant de mutation du tenant
préservé ; aucune dépendance au module Job (découplage) ; aucun fichier `Stratum.*` modifié. Depuis
l'amendement §4 : un fan-out partiellement échoué, un run sans tenant actif et un job de fan-out
récurrent jamais planifié sont tous des signaux explicites (Warning), sans nouvelle règle fiscale ni
règle d'alerte inventée.

**À la charge des consommateurs (TRK06/PIP01/PIP03/SUP01)** : déclarer leur `ITenantJob` et planifier
un job **système** (via le module Job) dont le handler appelle
`ITenantJobRunner.RunForAllTenantsAsync(...)`. Patron documenté dans
`docs/architecture/tenant-jobs.md` (référencé par `module-rules.md` §6). Un nouveau job de fan-out
récurrent DOIT être déclaré dans `SystemJobDefinitions` (classe adéquate) pour entrer dans le filet de
démarrage. **Depuis RDL08 (§5.1), tout `ITenantJob` DOIT être idempotent** : la reprise après crash/annulation
repose sur une ré-exécution au tick suivant (pas de reaper).

**Contrat d'enregistrement** : `AddTenantJobs()` (Common) enregistre `ITenantJobRunner` ; le Host
DOIT aussi enregistrer un `ITenantScopeFactory` (fait par `AddStratumMultiTenancy`). Sans cette
implémentation, la résolution du runner échoue à la première utilisation (pas de faux vert silencieux).

## Alternatives rejetées

- **Nouveau module sous `src/Modules/TenantJobs`** : impose l'obligation documentaire de module et
  une frontière `Contracts`-only pour un mécanisme transverse que tous les modules consomment ;
  surcoût sans bénéfice (Common est déjà l'assembly universellement référencée). **Rejetée.**
- **Exposer un `IMutableTenantContext` public enregistré largement** : n'importe quelle couche
  pourrait alors muter le tenant et provoquer une fuite cross-tenant — exactement l'invariant que le
  socle protège en gardant `MutableTenantContext` `internal` au Host. **Rejetée.**
- **Déplacer `MutableTenantContext` dans `Common`** : contredit le choix délibéré du socle (commentaire
  du fichier) et élargit la surface de mutation. **Rejetée.**
- **Le runner ouvre lui-même une connexion tenant (`ITenantConnectionFactory.OpenAsync(tenantId)`) et
  la passe au job** : le job ne pourrait plus injecter ses repositories scoped normaux (qui lisent
  `IConnectionFactory`) — il faudrait réécrire chaque consommateur autour d'une connexion explicite,
  perdant la transparence « réutilise la tenancy ». **Rejetée** (mais reste possible pour un job qui
  veut une connexion brute : `TenantJobContext.Services` l'expose).
- **(Amendement §4) Retry/dead-letter automatique d'un run partiel au niveau du runner** : double
  l'escalade déjà portée (états document + dead-man's-switch sourcés F12 §5.2) sans fiabilité
  supplémentaire, et masque l'erreur de fond. **Rejetée** au profit du signal structuré.
- **(Amendement §4) Règle d'alerte de supervision « ancrage/purge échoué »** : serait une règle hors
  de l'ensemble fermé F12 §5.2 (règle inventée, CLAUDE.md n°2). **Rejetée** au profit du signal
  structuré + filet de démarrage.

## Références

- `orchestration/items/SOL.yaml` (SOL06), `orchestration/items/RDL.yaml` (RDL07), `blueprint.md` §7
- `docs/architecture/module-rules.md` §6, §11 ; `docs/architecture/tenant-jobs.md`
- `docs/architecture/provenance-socle-stratum.md` §4.4, §4.12, §4.14
- `tasks/analyse-impact-pivot-plateforme.md` (constat module Job sans tenant)
- `tasks/redline-adr-005-006-007.md` (RDL07 : A6-runtime-1, A6-cons-2, A6-runtime-3 ;
  RDL08 : A6-scale-1/2/3/5, A6-runtime-2/4, A6-di-2)
- `src/Common/Abstractions/Jobs/TenantJobRunnerOptions.cs`, `IRecurringJobEnqueueGuard.cs` (RDL08, §5)
- `docs/conception/F12` §5.1 (cadences sourcées), §5.2 (catalogue fermé des règles d'alerte)
- `docs/adr/socle/ADR-0011-database-per-tenant.md`
