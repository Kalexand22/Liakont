# GED24 — Relations objet-à-objet avancées (inférence/héritage) sur le graphe générique borné

Fast-follow HORS gate (F19 §10). Enrichit `ged_index.entity_relations` avec `relation_type`
`inferred`/`inherited` par inférence/héritage BORNÉS, append-only, GÉNÉRIQUES (aucun type métier
en dur), BORNÉS (anti-DoS, comme la traversée §6.4).

## Conception

- Inférence = fermeture transitive bornée d'un `relation_kind` déclaré `transitive`.
- Héritage = un enfant hérite les relations (autres genres) de ses ancêtres via un `relation_kind`
  déclaré `hierarchical`.
- Substrat traversé = relations ASSERTÉES (`direct`/`extracted`) — jamais les dérivées → convergent,
  idempotent, borné.
- Émission per-seed (comme la traversée est per-entité) : from_entity_id = graine, borne de
  profondeur = paramètre tenant (F19 §6.4), anti-cycle.
- Règles = config tenant en base (`ged_catalog.relation_inference_rules`, vide à la création) —
  aucun vocabulaire métier en dur.

## Étapes

- [ ] Migration V018 `ged_catalog.relation_inference_rules` (config mutable tenant, checks mode/depth, unique)
- [ ] Domain : RelationInferenceMode (vocab fermé), RelationInferenceRule (validation + borne dure),
      EntityRelationEdge, DerivedRelation, RelationInferenceEngine (pur, borné, anti-cycle)
- [ ] Domain : EntityRelation (modèle d'append, miroir DocumentAxisLink, ck_er_no_self/source/type)
- [ ] Application : IRelationInferenceRuleStore, IEntityRelationGraphReader ; + AppendRelationAsync sur IGedIndexUnitOfWork
- [ ] Contracts : InferEntityRelationsCommand (per-seed)
- [ ] Infrastructure : Postgres stores + reader (CTE récursif borné) + handler + AppendRelationAsync UoW + DI
- [ ] Tests unitaires : engine (transitif/héritage/borne/anti-cycle/générique), rule, EntityRelation
- [ ] Tests intégration : migration (checks/unique) + bout-en-bout handler (append inferred/inherited, idempotent, append-only)
- [ ] verify-fast + run-tests + codex-review clean

## Review
(à compléter)
