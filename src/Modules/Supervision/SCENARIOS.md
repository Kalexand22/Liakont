# Scénarios de test — module Supervision

## Unit (`Liakont.Modules.Supervision.Tests.Unit`)

### Entité Alert (INV-SUPERVISION-005, 006)
- `AlertTests` — `Raise` initialise un état actif (tenant/règle/gravité, `TriggeredUtc`, non résolue, non
  acquittée) ; `Raise` exige tenant et règle (texte non vide) ; `Resolve` renseigne `ResolvedUtc` et rend
  l'alerte inactive ; `Resolve` deux fois → exception ; `Acknowledge` renseigne l'opérateur + l'horodatage
  SANS résoudre ; `Acknowledge` sans identité → exception ; `Acknowledge` puis `Resolve` reste cohérent.

### Moteur d'évaluation — anti-bruit / auto-résolution (INV-SUPERVISION-003, 004, 007, 009)
- `AlertEvaluationServiceTests` (store en mémoire, horloge figée, règles factices) —
  règle qui se déclenche sans alerte active → 1 alerte créée ;
  règle qui se déclenche avec alerte active → AUCUNE nouvelle alerte (anti-bruit) ;
  règle qui ne se déclenche plus avec alerte active → AUTO-RÉSOLUTION ;
  règle qui ne se déclenche pas sans alerte → aucune écriture ;
  re-déclenchement APRÈS résolution → NOUVELLE alerte ;
  plusieurs règles dispatchées indépendamment ;
  une règle qui lève est ISOLÉE (les autres s'évaluent, l'échec figure dans le bilan) ;
  aucune règle enregistrée → aucune alerte (SUP01a ne livre aucune règle) ;
  annulation propagée (jamais avalée comme un échec de règle).

### Dead-man's switch — job multi-tenant (INV-SUPERVISION-001, 002, 007)
- `SupervisionJobTests` — `SupervisionEvaluationTenantJob` résout le moteur depuis le scope du tenant et
  l'exécute (`Name == "sup.evaluation"`) ; le job LÈVE si l'évaluation remonte des échecs de règles ;
  `SupervisionEvaluationFanOutHandler` fait tourner le job pour tous les tenants via le runner.

## Integration (`Liakont.Modules.Supervision.Tests.Integration`, PostgreSQL réel)

- `AlertLifecycleIntegrationTests` — round-trip sur la base du tenant : insertion via le store, lecture des
  alertes actives / récentes / par identifiant (`IAlertQueries`), auto-résolution (l'alerte sort des
  actives), acquittement (`IAlertAcknowledgementService` — opérateur journalisé, alerte toujours active),
  acquittement d'une alerte absente → `false`. (INV-SUPERVISION-005, 006)
- `AlertConcurrencyIntegrationTests` — absence de lost-update entre auto-résolution et acquittement
  (mises à jour ciblées par opération) : un acquittement avec snapshot périmé ne RESSUSCITE pas une alerte
  résolue ; une résolution avec snapshot périmé n'EFFACE pas un acquittement. (INV-SUPERVISION-005)
- `AlertAntiNoiseIntegrationTests` — moteur sur base réelle : un déclenchement crée une alerte ; un second
  cycle de la même règle ne crée PAS de doublon (anti-bruit, index unique partiel) ; la disparition de la
  condition résout l'alerte ; un nouveau déclenchement après résolution crée une nouvelle alerte. (INV-SUPERVISION-003, 004)
- `DeadMansSwitchMultiTenantIntegrationTests` — le VRAI `TenantJobRunner` (SOL06) parcourt DEUX bases tenant
  réelles via un `ITenantScopeFactory` de test : chaque tenant reçoit sa propre alerte dans SA base
  (isolation), l'alerte porte le bon `tenant_id` ; un tenant en échec n'empêche pas l'autre (bilan du
  runner). (INV-SUPERVISION-002, 007, 008)
