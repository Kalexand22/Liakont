# GED08 — Recherche : document_search (tsvector + unaccent IMMUTABLE) + facettes + confidentialité matérialisée + graphe borné

Segment `ged` (feat/ged), sous-branche `feat/ged-GED08`. Blueprint `module-work-item`.
Source : F19 §6.1-§6.4 + ADR-0035. Dépend de GED04 (UoW/liens d'axe) + GED07 (archivage) — done.

## Décisions de conception (issues de l'exploration)

- **document_search** = table dérivée reconstructible (foyer UNIQUE du FTS document ; `managed_documents`
  ne porte pas de `search_vector`). Peuplée par **projection asynchrone** = un SECOND
  `IIntegrationEventConsumer<ManagedDocumentReceivedV1>` enregistré APRÈS l'indexeur GED05b
  (le dispatcher socle invoque les consumers dans l'ordre d'enregistrement, séquentiellement →
  la projection lit l'index committé). Reconstructible : la projection lit UNIQUEMENT la base tenant
  (title + `current_axis_links` searchables non-confidentiels), jamais le staging → réutilisable par le
  backfill GED10.
- **unaccent** : `CREATE EXTENSION IF NOT EXISTS unaccent` + wrapper IMMUTABLE `ged_index.immutable_unaccent`
  (le `unaccent(text)` natif est STABLE ; forme 2-args `unaccent('unaccent', $1)` = IMMUTABLE) — RL-13.
- **FTS** : `to_tsvector('french', immutable_unaccent(...))`, `websearch_to_tsquery('french', immutable_unaccent(@q))`,
  setweight (title=A, axes searchables=B), index GIN. Config `'french'` (D11). Axes confidentiels EXCLUS du
  vector au BUILD (INV-GED-10 : `liakont.ged.confidential` n'ouvre PAS le FTS en V1).
- **Recherche multi-axes** : conjonction robuste aux axes multi-valeur via
  `HAVING count(DISTINCT CASE WHEN ad.code=@code_i AND dal.normalized_value=@val_i THEN 'c_i' END)=N`
  (jamais `count(DISTINCT code)` naïf). Valeurs filtre normalisées via `ValueNormalizer` (type-aware) pour
  matcher `normalized_value` (casefold à l'écriture).
- **Prédicat de confidentialité MATÉRIALISÉ dans le SQL** (RL-31) sur axe/facette/graphe :
  `(ad.is_confidential = false OR @hasConfidentialRight)` / `(et.is_confidential = false OR @hasConfidentialRight)`.
  Anti-oracle : un critère/facette/racine confidentiel sans le droit → aucun résultat, jamais un count révélateur.
- **Facettes** : `count(*) GROUP BY (ad.code, normalized_value)` restreint aux `is_facetable`, même prédicat conf.
- **Graphe** : CTE récursif BIDIRECTIONNEL sur `current_entity_relations`, borne de profondeur DURE (clamp),
  anti-cycle (tableau de chemin), pagination keyset, confidentialité héritée des `entity_types` aux 2 extrémités
  ET à la racine (INV-GED-09). Retourne les documents atteignables (`current_document_entity_links`).
- **Pagination keyset** partout (RL-20) : `WHERE managed_document_id > @after ORDER BY managed_document_id LIMIT @n`
  (jamais OFFSET ni chargement-tout). Le tri par pertinence ts_rank = fast-follow (OpenSearch GED21).
- **Abstraction** : `IDocumentSearchIndex` public (Application/Index — Application n'a pas d'IVT) ;
  impl `internal sealed PostgresDocumentSearchIndex` (Infrastructure/Index, IVT Tests). Tenant-scope = la connexion.

## Étapes

- [x] V020__create_ged_index_document_search.sql (extension unaccent + wrapper IMMUTABLE + table + GIN)
- [x] Application/Index : IDocumentSearchIndex + DocumentSearchQuery/Result + GraphExplorationQuery/Result + AxisFilter/Facet
- [x] Infrastructure/Index/PostgresDocumentSearchIndex.cs (RefreshDocumentAsync + SearchAsync + ExploreGraphAsync)
- [x] Infrastructure/Index/ManagedDocumentSearchProjector.cs (2e consumer, tenant-scope, appelle RefreshDocumentAsync)
- [x] GedModuleRegistration : enregistrer IDocumentSearchIndex + le projector APRÈS l'indexeur
- [x] Tests.Integration : projection, multi-axes (faux positif multi-valeur), confidentialité (axe+facette+graphe),
      unaccent (accent-insensible), graphe borné/anti-cycle/bidirectionnel, reconstructible (DELETE+rebuild),
      isolation tenant (≥ 2 bases), + test de bout-en-bout via le vrai dispatcher socle
- [x] verify-fast + run-tests verts ; codex-review propre (round 3 CLEAN)

## Review (fin de session)

**Livré** (commits 7a134fbc + e76d9ce4, mergés dans feat/ged @ ecccbf56) :
- Migration V020 : `CREATE EXTENSION unaccent` + wrapper IMMUTABLE `ged_index.immutable_unaccent` (RL-13) +
  table dérivée reconstructible `ged_index.document_search` (PK managed_document_id, search_vector tsvector, GIN).
- `IDocumentSearchIndex` (Application/Index) + DTOs ; `PostgresDocumentSearchIndex` : projection async
  `RefreshDocumentAsync` (titre A + axes searchables non-conf B, 'french', unaccent), recherche multi-axes robuste
  au multi-valeur (CASE code+valeur), facettes, prédicat de confidentialité matérialisé (RL-31, anti-oracle),
  traversée graphe bidirectionnelle bornée (borne dure de profondeur, anti-cycle, keyset).
- `ManagedDocumentSearchProjector` : 2e consumer de ManagedDocumentReceivedV1 après l'indexeur (§6.1).
- 18 tests d'intégration GED nouveaux (17 recherche/graphe + 1 pipeline dispatcher réel) ; SCENARIOS.md/INVARIANTS.md à jour.

**Vérif** : verify-fast PASS (plateforme .NET 10 + agent net48 + onsite-client) ; run-tests PASS (7583 tests, 0 échec) ;
codex-review round 1 = 0 P1 / 2 P2 → corrigés → round 3 CLEAN.

**P2 accepté et documenté** (round 2) : la matérialisation du CTE récursif d'exploration de graphe n'est pas bornée
en NOMBRE de chemins simples (le tableau de chemin ne borne que la longueur/termine les cycles). Acceptée pour V1 :
la borne DURE de profondeur (MaxAllowedDepth=8) rend l'ensemble FINI ; rayon d'impact = un seul tenant, écran
opérateur authentifié (pas un vecteur DoS anonyme) ; le passage à l'échelle sur gros corpus est le backend OpenSearch
derrière `IDocumentSearchIndex` (GED21). Commentaire du code rendu exact. Un cap de lignes rendrait des résultats
partiels/faux ; un dedup visited-set n'est pas exprimable en CTE récursif standard.
