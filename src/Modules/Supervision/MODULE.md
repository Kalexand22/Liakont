# Module Supervision

## Purpose

Détecte de façon **proactive** les défaillances opérationnelles de chaque tenant de l'instance et en avertit
l'opérateur (F12 §5). C'est **la plateforme qui détecte l'absence** (panne silencieuse), pas l'agent qui
signale sa présence : un **dead-man's switch** (job planifié, toutes les 15 min) évalue des **règles
d'alerte** sur tous les tenants. L'e-reporting ayant des échéances légales, une panne silencieuse = un
client en non-conformité sans le savoir — d'où une supervision **côté plateforme**.

**Périmètre de l'item SUP01a** (infrastructure ; les 8 règles concrètes sont SUP01b) :
- Le **scaffold** du module (Contracts/Domain/Application/Infrastructure, pattern Stratum).
- L'**entité `Alert`** (déclenchement, gravité, auto-résolution, acquittement opérateur) : état OPÉRATIONNEL
  **mutable** — à NE PAS confondre avec la piste d'audit append-only (`DocumentEvent`), qui est immuable.
- La **persistance** (schéma `supervision`, table `alerts`) + les **lectures** (`IAlertQueries`, consommées
  par le dashboard SUP02) + l'**acquittement** (`IAlertAcknowledgementService`).
- Le **moteur de règles** (`IAlertRule` + `IAlertEvaluationService`) avec la mécanique **anti-bruit /
  auto-résolution** : une alerte active ne se re-déclenche pas ; elle se résout quand la condition disparaît
  et peut se re-déclencher ensuite. Un échec de règle est ISOLÉ (les autres règles continuent) et remonté.
- Le **dead-man's switch** : un job SYSTÈME (`SupervisionEvaluationTrigger` +
  `SupervisionEvaluationFanOutHandler`) qui fait le **fan-out** de `SupervisionEvaluationTenantJob` sur tous
  les tenants actifs via `ITenantJobRunner` (SOL06) — le SEUL code cross-tenant du produit (en lecture).

Sont **hors périmètre SUP01a** (items suivants) : les **8 règles concrètes** (agent muet, run manqué, file
de push, documents bloqués, rejets PA, échéance déclarative, SIREN non publié, version d'agent obsolète) et
leurs seuils par défaut / surcharge tenant (**SUP01b**) ; le **dashboard** opérateur (**SUP02**) ; les
**notifications email** + le transport SMTP réel (**SUP03**). SUP01a ne livre **aucune règle concrète** (au
plus une règle d'exemple côté tests, pour prouver le moteur).

## Boundaries

- **Schéma owné** : `supervision` (PostgreSQL, base **par tenant**).
  - `alerts` : les alertes du tenant (déclenchement, auto-résolution, acquittement). Table **mutable**
    (état opérationnel), **pas** une table d'audit.
- **Isolation tenant** : **par la CONNEXION** — la connexion EST le tenant (database-per-tenant,
  blueprint §7). Le dead-man's switch évalue chaque tenant via `TenantJobRunner` (une exécution par base) ;
  les alertes sont persistées dans la base du tenant évalué. Le dashboard d'instance (SUP02) **agrège** ces
  lectures tenant par tenant — **seul cas cross-tenant** du produit, en **lecture seule** (blueprint §7
  règle 2 ; CLAUDE.md n°9/17). La colonne `tenant_id` de `alerts` est l'**exception documentée** à « aucune
  colonne de tenant » (module-rules §6) : elle nomme le tenant pour que le dashboard cross-tenant étiquette
  l'alerte sans ambiguïté ; elle n'autorise aucune requête cross-tenant en écriture.
- **Interdits** (module-rules §2, §6) : toute boucle « pour chaque tenant » maison (le fan-out passe
  EXCLUSIVEMENT par `TenantJobRunner`, SOL06) ; toute logique métier (TVA, validation, états) ; toute
  invention de seuil/cadence fiscale (les seuils viennent de F12 §5.2, l'échéance déclarative n'est calculée
  que si `reportingFrequency` est renseigné — SUP01b) ; tout secret en clair.
- **Surface publique** : `Contracts/` uniquement (`IAlertQueries` + `AlertDto` ; `IAlertAcknowledgementService`).
  Le moteur (`IAlertEvaluationService`), le point d'extension `IAlertRule`, l'entité `Alert` et le store
  (`IAlertStore`) sont **internes** au module (consommés par le job de supervision et, pour `IAlertRule`,
  implémentés par SUP01b dans ce même module).

## Published Events

Aucun (item SUP01a). Le module ne publie pas d'événement d'intégration ; les notifications email (SUP03)
seront déclenchées à partir des alertes, pas via l'outbox.

## Consumed Events

Aucun (item SUP01a). Le module n'est pas piloté par des événements : son moteur est déclenché
**périodiquement** par le job système (dead-man's switch) — c'est le principe du switch (détecter une
ABSENCE ne peut pas reposer sur la réception d'un événement). Les règles concrètes (SUP01b) **liront** des
données d'autres modules via leurs `Contracts` (heartbeats, documents bloqués, rejets PA) — toujours en
lecture, tenant-scopée par la connexion.

## Dependencies

- `Stratum.Common.Abstractions` (`ITenantJob`, `TenantJobContext`, `ITenantJobRunner`, `TenantJobRunSummary`,
  `TenantJobFailure` — mécanique multi-tenant SOL06).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `MigrationAssembliesOptions`, Dapper/Npgsql/DbUp).
- `Stratum.Modules.Job.Contracts` (`IJobHandler<T>` — le dead-man's switch est un job système ; accès
  inter-module par les Contracts uniquement, module-rules §3 / CLAUDE.md n°14).
