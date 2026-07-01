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

- [ ] V020__create_ged_index_document_search.sql (extension unaccent + wrapper IMMUTABLE + table + GIN)
- [ ] Application/Index : IDocumentSearchIndex + DocumentSearchQuery/Result + GraphExplorationQuery/Result + AxisFilter/Facet
- [ ] Infrastructure/Index/PostgresDocumentSearchIndex.cs (RefreshDocumentAsync + SearchAsync + ExploreGraphAsync)
- [ ] Infrastructure/Index/ManagedDocumentSearchProjector.cs (2e consumer, tenant-scope, appelle RefreshDocumentAsync)
- [ ] GedModuleRegistration : enregistrer IDocumentSearchIndex + le projector APRÈS l'indexeur
- [ ] Tests.Unit : bits purs (clamp profondeur, construction critères) si extraits
- [ ] Tests.Integration : projection, multi-axes (faux positif multi-valeur), confidentialité (axe+facette+graphe),
      unaccent (accent-insensible), graphe borné/anti-cycle/bidirectionnel, reconstructible (DELETE+rebuild),
      isolation tenant (≥ 2 bases)
- [ ] verify-fast + run-tests verts ; codex-review propre

## Review (rempli en fin de session)
