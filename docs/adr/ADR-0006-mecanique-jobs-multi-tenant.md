# ADR-0006 — Mécanique de jobs multi-tenant dans `src/Common`, basculement de tenant derrière une abstraction

- **Statut** : Accepté (2026-06-04). Mise en œuvre : item SOL06.
- **Date** : 2026-06-04
- **Contexte décisionnel** : `orchestration/items/SOL.yaml` (SOL06), `blueprint.md` §7 (multi-tenancy),
  `docs/architecture/module-rules.md` §6, `docs/conception/F12`, `CLAUDE.md` n°9 (requêtes
  tenant-scopées), `tasks/analyse-impact-pivot-plateforme.md` (constat : le module Job vendored n'a
  pas de résolution de tenant), `docs/architecture/provenance-socle-stratum.md` §4.4 et §4.12.

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

## Conséquences

**Positif** : une mécanique unique, testée (unit + intégration sur 2 bases tenant), réutilisable par
TRK06/PIP01/PIP03/SUP01 sans réinventer de boucle de tenants ; invariant de mutation du tenant
préservé ; aucune dépendance au module Job (découplage) ; aucun fichier `Stratum.*` modifié.

**À la charge des consommateurs (TRK06/PIP01/PIP03/SUP01)** : déclarer leur `ITenantJob` et planifier
un job **système** (via le module Job) dont le handler appelle
`ITenantJobRunner.RunForAllTenantsAsync(...)`. Patron documenté dans
`docs/architecture/tenant-jobs.md` (référencé par `module-rules.md` §6).

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

## Références

- `orchestration/items/SOL.yaml` (SOL06), `blueprint.md` §7
- `docs/architecture/module-rules.md` §6, §11 ; `docs/architecture/tenant-jobs.md`
- `docs/architecture/provenance-socle-stratum.md` §4.4, §4.12, §4.14
- `tasks/analyse-impact-pivot-plateforme.md` (constat module Job sans tenant)
- `docs/adr/socle/ADR-0011-database-per-tenant.md`
