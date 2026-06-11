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

### Règles d'alerte SUP01b — logique pure (INV-SUPERVISION-009, 010, 011, 012)
- `AgentMuteAlertRuleTests` (registre d'agents + paramétrage tenant fictifs) — clé/gravité stables ;
  déclenche au-delà du seuil par défaut (24 h) ; ne déclenche pas en deçà ni PILE au seuil (« > » strict) ;
  un agent jamais vu compte son silence depuis l'enregistrement (déclenche si > seuil, sinon non) ;
  un agent révoqué est exclu ; aucun agent → pas d'alerte ; la surcharge tenant resserre ET relâche le seuil ;
  défaut appliqué quand la company existe mais sans seuils.
- `BlockedDocumentsAlertRuleTests` — clé/gravité (Avertissement) ; déclenche si le plus ancien Blocked dépasse
  5 j ; pas en deçà ni pile à 5 j ; pas de Blocked → pas d'alerte ; la surcharge tenant resserre le seuil.
- `PaRejectedDocumentsAlertRuleTests` — clé/gravité (Critique) ; déclenche si le plus ancien RejectedByPa
  dépasse 2 j ; pas en deçà ni pile à 2 j ; pas de rejet → pas d'alerte ; la surcharge tenant relâche le seuil.

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
- `SupervisionRulesIntegrationTests` — les règles SUP01b sur PostgreSQL réel (bases isolées portant les
  schémas Documents + TenantSettings + Supervision) : la règle « documents bloqués » lit un vrai document
  bloqué (`PostgresDocumentQueries.GetOldestDocumentInStateAsync`), déclenche, ne re-déclenche pas
  (anti-bruit), puis s'auto-résout quand le document est pris en charge ; la règle « rejets PA » déclenche
  sur un vrai RejectedByPa (gravité Critique) ; la surcharge de seuil par tenant (vrai
  `PostgresTenantSettingsQueries` sur une ligne `alert_thresholds` seedée) SUPPRIME une alerte que le défaut
  aurait levée ; la règle « agent muet » (source agent stubbée — impl interne à Ingestion) déclenche puis
  s'auto-résout via le store d'alertes RÉEL ; isolation cross-tenant prouvée par le vrai `TenantJobRunner`
  (un document bloqué chez alpha n'alerte QUE alpha). (INV-SUPERVISION-010, 011, 012, 013)
