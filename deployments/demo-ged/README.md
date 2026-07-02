# Démo GED — généricité prouvée par configuration (GED10)

Ces seeds **fictifs** montrent qu'un **produit générique** (module `Ged`) indexe deux métiers radicalement
différents **sans un seul `ALTER TABLE`** : tout le métier est du **paramétrage tenant** (règle 7 — aucun
vocabulaire métier n'est codé en dur dans `src/Modules/Ged/**`).

| Métier | Fichier | Axes | Entité |
|---|---|---|---|
| Ventes aux enchères | [`encheres/ged-catalog.sql`](encheres/ged-catalog.sql) | `numero_lot` (multi), `numero_vente` | `acheteur` |
| Situations de travaux (BTP) | [`btp/ged-catalog.sql`](btp/ged-catalog.sql) | `numero_situation`, `mois`, `montant_ht_cumule` (EUR, échelle 2), `avancement_pct` (%, échelle 0) | `chantier` |

Le **même** `ged_index.document_axis_links` porte un montant en **EUR** et un avancement en **%** : c'est la preuve
de généricité de F19 §11 D12.

## Application (base DU TENANT, schémas `ged_catalog`/`ged_index` déjà migrés)

```sh
psql "$TENANT_CONNECTION" -f encheres/ged-catalog.sql
psql "$TENANT_CONNECTION" -f btp/ged-catalog.sql
```

Les scripts sont **idempotents** (`ON CONFLICT`). Ensuite, les documents « bordereau_acheteur » / « situation_travaux »
ingérés par le canal GED sont mappés sur ces axes ; le **backfill rétroactif** (GED10) indexe en plus le corpus fiscal
déjà scellé (chemin direct idempotent).

> Données **fictives** : aucun SIREN/tenant/compte réel (CLAUDE.md n°7). Un déploiement client réel fournit ses
> propres profils/axes, jamais recopiés du code.
