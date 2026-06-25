# ADR-0035 — Recherche & index GED : `tsvector` PostgreSQL derrière `IDocumentSearchIndex`, projection asynchrone reconstructible, graphe borné bidirectionnel

- **Statut** : Proposé (2026-06-25).
- **Date** : 2026-06-25
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Liakont.Modules.Ged` non démarré,
  **aucun code**). Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**,
  pas l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR
  **dérive de** la conception `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` (statut « proposition NON RATIFIÉE »)
  et n'invente **aucune** règle fiscale, légale ou probante (CLAUDE.md n°2). Il est la **sœur** d'ADR-0032 (le
  méta-modèle dont il consomme les vues `current_*` et les axes), d'ADR-0034 (le canal d'ingestion dont les
  événements **peuplent** l'index) et d'ADR-0036 (le journal qui **trace** toute recherche/consultation produite ici) ;
  il **dépend** d'ADR-0033 (coffre tiers, fast-follow GED20) **seulement** pour ce qui touche l'intégrité du paquet à
  l'affichage d'une fiche. Cet ADR ne tranche **que** la couche « retrouver » : recherche multidimensionnelle, plein
  texte, graphe et portail.
- **Numérotation** : ADR-**0035**. La numérotation libre de la GED (F19 §9) commence à **0032** (le repo contient déjà
  DEUX `ADR-0031` — `-cablage-cycle-run-agent…` et `-licence-fluentassertions…`). Plan d'ADR GED : **0032** méta-modèle,
  **0033** coffre tiers / option C (fast-follow GED20), **0034** ingestion générique, **0035** recherche & index (ce
  document), **0036** journal de consultation.
- **Contexte décisionnel** : `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §2.3 (recherche NON promue en axe en V1),
  §2.4 (flux MVP de bout en bout), §6.1 (projection asynchrone), §6.2 (recherche multi-axes + facettes),
  §6.3 (`tsvector` + `unaccent` + source du texte), §6.4 (traversée de graphe bidirectionnelle bornée),
  §6.5 (droits & confidentialité server-side), §6.7 (portail Blazor), §8 (invariants/tests), §11 (D10/D11) ; sources
  socle/code réelles citées par F19 : `src/Host/Liakont.Host/Security/RolePermissionCatalog.cs` (matrice de permissions
  à amender), `src/Common/UI/Components/DeclaredListPage.razor.cs` (mode chargement-tout à NE PAS réutiliser pour la
  recherche), `src/Modules/Archive/Application/ReadableDocumentRenderer.cs` (rendu lisible **réservé au fiscal**),
  `src/Modules/Archive/Domain/IArchiveStore.cs` (lecture write-once `ReadAsync`), `src/Modules/Archive/Contracts/`
  (`IArchiveVerifier`, réservé aux `ManagedDocument` fiscaux) ; ADR liés : ADR-0032, ADR-0033, ADR-0034, ADR-0036,
  ADR-0017 (`-pont-role-permission-claims-oidc`).

## Contexte

La GED de F19 apporte trois valeurs : **indexer** sur des axes dynamiques, **relier** des documents à des entités
métier polymorphes (graphe), et **ingérer** des documents métier arbitraires. Cet ADR ne traite que la **finalité de
restitution** : *retrouver*. Le piège central est que « retrouver » se décline en quatre canaux d'accès (recherche par
axe, facettes, plein texte, traversée de graphe) et que **chacun** est un canal de fuite potentiel d'une donnée marquée
confidentielle (F19 §6.5). La décision doit donc graver, **dans le SQL lui-même**, le même prédicat de confidentialité
sur les quatre canaux — pas en prose, sinon le canal de fuite se déplace simplement de l'axe vers la facette ou vers le
graphe (F19 §6.2/§6.4, RL-31).

La deuxième force en présence est l'**évolutivité du backend de recherche** sans corruption de l'architecture. PostgreSQL
`tsvector` suffit au V1 ; OpenSearch (volume) et pgvector (sémantique) sont des fast-follow réels (F19 §2.3/§10, items
GED21/GED22). La doctrine produit (blueprint règle 6, CLAUDE.md n°8) impose une **abstraction à capacités** plutôt qu'un
`if (index is OpenSearch)`. **Mais** F19 §2.3 tranche que la recherche **n'est PAS promue en 5ᵉ axe enfichable en V1** :
on pose une **abstraction interne** `IDocumentSearchIndex` (une seule implémentation `tsvector`), pas un axe de plug-in
public — pour éviter le code dormant (RL-26) tout en gardant la frontière propre le jour où un 2ᵉ backend est livré.

La troisième force est la **cohérence avec la règle d'immuabilité** (CLAUDE.md n°4). L'index plein texte est un **dérivé**
reconstructible : il doit pouvoir être tronqué et reconstruit (`DELETE`+rebuild) lors d'un changement de configuration
`tsvector`, d'un correctif de pondération ou d'un backfill. Le piège serait de poser ce `search_vector` **sur le pivot
mutable** `managed_documents` : on aurait alors deux foyers de vérité (le pivot + l'index), non réconciliables, et une
colonne dérivée mêlée à une table append-only-par-chaînage. F19 §6.1/§6.3 tranche le **foyer unique** : la table dérivée
`ged_index.document_search`. Étant un dérivé, sa reconstruction **ne viole pas** la règle 4 — point à documenter
explicitement pour éviter un faux-P1 en review.

La quatrième force est la **provenance honnête** : F19 (RL-13) interdit de présenter `tsvector`/`unaccent` comme
« réutilisés ». Vérification faite, **aucun** `tsvector` ni `unaccent` n'existe dans le repo aujourd'hui (grep des
migrations) : ce provisionnement est **NEUF** et l'extension `unaccent` exige le droit superuser au moment de la
migration.

## Décision

### 1. Recherche derrière l'abstraction **INTERNE** `IDocumentSearchIndex` (PG `tsvector` en V1), NON promue en axe enfichable

Le moteur de recherche est consommé derrière l'abstraction **interne** `IDocumentSearchIndex` (`Ged.Contracts`),
**implémentation unique en V1 = PostgreSQL `tsvector`** (F19 §2.3). Cette abstraction **n'est PAS** un 5ᵉ axe de plug-in
public au sens de F19 §2.3 (le 5ᵉ axe enfichable est le coffre tiers `ISealedArchiveProvider`, ADR-0033) : c'est une
frontière **interne** au module `Ged`, posée pour qu'un 2ᵉ backend devienne un plug-in fast-follow sans réécrire les
pages ni les handlers.

- **OpenSearch** (montée en charge / volume) = **GED21**, plug-in fast-follow ; **pgvector** (recherche sémantique) =
  **GED22**, plug-in fast-follow. Aucun n'est livré en V1 et **aucun code dormant** n'est posé (RL-26).
- **Aucun `if (index is OpenSearch)` ni flag produit doublonnant une capacité de backend (P1, CLAUDE.md n°8).** Le
  routage vers un backend alternatif, le jour venu, passe par la **capacité déclarée** du plug-in, jamais par un test de
  type concret ni un flag de configuration produit.

### 2. Peuplement par **PROJECTION ASYNCHRONE** ; foyer **UNIQUE** du FTS = table dérivée `ged_index.document_search`

L'index se peuple par **consommation de `ManagedDocumentReceivedV1`** (canal GED, ADR-0034) **et** des événements de
mapping/archivage GED — **JAMAIS** par abonnement à `DocumentReceivedV1` (qui déclenche le pipeline fiscal d'émission ;
F19 §2.4/§6.1). Le recalcul du `search_vector` est **asynchrone** (event handler), pour découpler la latence d'ingestion
de l'indexation.

```sql
-- ged_index.document_search : FOYER UNIQUE du FTS document, dérivé reconstructible (index GIN)
CREATE TABLE IF NOT EXISTS ged_index.document_search (
    managed_document_id uuid        NOT NULL,
    search_vector       tsvector    NOT NULL,
    refreshed_utc       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_document_search PRIMARY KEY (managed_document_id)
);
CREATE INDEX ix_document_search_gin ON ged_index.document_search USING GIN (search_vector);
```

- **Le pivot `managed_documents` ne porte AUCUNE colonne `search_vector`** (F19 §3.4.1, ligne 211) : sinon **double-source
  non réconciliable** entre le pivot mutable et l'index.
- **Reconstructibilité documentée (anti faux-P1)** : `document_search` étant un **dérivé**, il peut être tronqué et
  reconstruit (`DELETE`+rebuild) — par exemple au changement de config `tsvector`, à un correctif de pondération, ou à un
  backfill (GED10) — **sans violer la règle 4** (CLAUDE.md n°4 protège les tables d'audit et le coffre WORM, pas un index
  dérivé). Le coffre WORM (`IArchiveStore`) et les change-logs restent, eux, strictement append-only.
- **Asymétrie ASSUMÉE** : le FTS d'`entity_instances` (recherche d'entités) est, en V1, **inline-en-place** sur la table
  d'entités, **pas reconstructible-par-rebuild** comme `document_search`. Si le besoin de reconstruction apparaît, il
  migrera vers une table dérivée `entity_search` symétrique. Cette asymétrie est **assumée** en V1, non un oubli.

### 3. Provisionnement `tsvector`/`unaccent` **NEUF** (RL-13), config `'french'` (FR-only)

**À ne PAS présenter comme « réutilisé »** : aucun `tsvector`/`unaccent` n'existe dans le repo aujourd'hui (RL-13,
vérifié). La migration GED neuve pose :

- `CREATE EXTENSION IF NOT EXISTS unaccent` (**droit superuser au moment de la migration** — contrainte d'exploitation à
  documenter) ;
- un **wrapper IMMUTABLE** d'`unaccent()` (la fonction native est seulement `STABLE`) pour l'usage en **colonne générée /
  expression d'index** (PostgreSQL refuse une fonction non `IMMUTABLE` dans une expression d'index) ;
- la configuration **`'french'`** (FR-only, aligné F10). Le **multilingue** (config `tsvector` ≠ `french`) est **❓ NON
  TRANCHÉ** (D11) : contenu non-FR best-effort en V1, multilingue = fast-follow.

### 4. Recherche multi-axes **CORRECTE** + prédicat de confidentialité **matérialisé dans le SQL** (RL-31)

La recherche par axe lit la vue `ged_index.current_axis_links` (ADR-0032 : exclut les liens rétractés/superséedés via
`supersedes_id`, RL-24). La conjonction multi-axes compte les **critères réellement satisfaits**, jamais un
`count(DISTINCT code)` naïf qui donnerait un **faux positif** sur un axe multi-valeur (F19 §6.2) :

```sql
-- Documents portant 'acheteur'=@a ET 'numero_lot'=@l (conjonction robuste aux axes multi-valeurs) :
SELECT dal.managed_document_id
FROM   ged_index.current_axis_links dal
JOIN   ged_catalog.axis_definitions ad ON ad.id = dal.axis_id
WHERE  (ad.is_confidential = false OR @hasConfidentialRight)   -- prédicat de confidentialité OBLIGATOIRE (RL-31, anti-oracle)
GROUP BY dal.managed_document_id
HAVING count(DISTINCT CASE
         WHEN ad.code='acheteur'   AND dal.normalized_value=@a THEN 'a'
         WHEN ad.code='numero_lot' AND dal.normalized_value=@l THEN 'b' END) = 2;
```

- **PRÉDICAT DE CONFIDENTIALITÉ `(ad.is_confidential = false OR @hasConfidentialRight)` MATÉRIALISÉ DANS LE SQL** (RL-31,
  anti-oracle), **pas en prose** — **non négociable**. Tout SQL de recherche/facette est une esquisse dont ce prédicat est
  la partie figée.
- **Facettes** restreintes aux axes `is_facetable` : `count(*) GROUP BY (axis_id, normalized_value)` portant le **même**
  prédicat de confidentialité. Une facette sur un axe confidentiel masqué **ne révèle jamais de comptes** (anti-oracle) —
  le masquage est **server-side**, jamais une simple omission d'affichage UI (F19 §6.5).
- **Plein texte** (`websearch_to_tsquery('french', @q)`, pondération `setweight` titre=A / axes searchables=B) : les axes
  confidentiels sont **exclus du `search_vector` partagé** en V1 (F19 §6.5), de sorte que le FTS ne fige **aucune** valeur
  confidentielle (cf. INV-GED-07).

### 5. **Source du texte indexable** explicite (pas de faux « plein texte »)

F19 §6.3 distingue **recherche sur AXES** (garantie V1) et **recherche sur CONTENU** (conditionnelle à la disponibilité
d'une couche texte). La source du texte indexable est explicite :

1. document portant un **rendu lisible** (factures via `ReadableDocumentRenderer` — **réservé au fiscal**, RL-16 — ou
   `ReadableHtml` fourni avec le paquet GED) → **extraction texte du HTML** ;
2. **PDF à couche texte** → extraction de la couche texte en V1 ; **sinon étiqueté « cherché sur métadonnées seulement »** ;
3. **scan image** → **OCR explicitement fast-follow (GED23)** ; le document est **clairement non-full-text en V1** — **pas
   de faux « plein texte » silencieux** sur une image sans OCR.

**Distinction d'invariant** : un document **sans couche texte disponible** reste retrouvable **par ses axes** (V1), il
n'est simplement pas retrouvable **par son contenu** tant que l'OCR (GED23) n'est pas livré. On **n'affirme jamais** une
capacité plein texte qu'on ne livre pas (CLAUDE.md n°3, « bloquer/DEFER plutôt qu'affirmer faux »).

### 6. Traversée de graphe **BIDIRECTIONNELLE et BORNÉE** (confidentialité héritée, matérialisée dans le SQL)

La traversée d'entités (F19 §6.4) lit la vue `ged_index.current_entity_relations` (exclut les relations
rétractées/superséedées, RL-24), est **bidirectionnelle**, et porte une **borne de profondeur DURE** (paramètre tenant,
défaut **4** ; jamais infinie = garde anti-DoS) :

```sql
WITH RECURSIVE reach AS (
    SELECT @rootEntityId AS entity_id, 0 AS depth, ARRAY[@rootEntityId]::uuid[] AS path
  UNION ALL
    SELECT nxt.entity_id, r.depth + 1, r.path || r.entity_id
    FROM   reach r
    JOIN   ged_index.current_entity_relations er                                          -- exclut rétractées/superséedées (RL-24)
           ON (er.from_entity_id = r.entity_id OR er.to_entity_id = r.entity_id)          -- BIDIRECTIONNEL
    CROSS JOIN LATERAL (SELECT CASE WHEN er.from_entity_id = r.entity_id
                                    THEN er.to_entity_id ELSE er.from_entity_id END AS entity_id) nxt
    JOIN   ged_index.entity_instances ei ON ei.id = nxt.entity_id
    JOIN   ged_catalog.entity_types  et ON et.id = ei.entity_type_id
    WHERE  r.depth < @maxDepth                                                            -- borne dure (défaut 4, paramètre tenant)
      AND  NOT nxt.entity_id = ANY(r.path)                                                -- anti-cycle
      AND  (et.is_confidential = false OR @hasConfidentialRight)                          -- confidentialité héritée du type d'entité (RL-31)
)
SELECT DISTINCT del.managed_document_id, del.role, r.entity_id, r.depth
FROM   reach r
JOIN   ged_index.current_document_entity_links del ON del.entity_id = r.entity_id;        -- exclut rétractés/superséedés (RL-24)
```

- **Anti-cycle** par `path` (`NOT nxt.entity_id = ANY(r.path)`) et **pagination KEYSET** côté SQL : **jamais `OFFSET`,
  jamais de chargement-tout en mémoire** (le portail consomme une page déjà bornée côté SQL — pas le mode chargement-tout
  de `DeclaredListPage`, RL-20).
- **Confidentialité héritée** : `entity_relations` **n'a PAS** de colonne `is_confidential` ; la confidentialité d'une
  **relation s'hérite des `entity_types` à ses deux extrémités**. La traversée **joint** `entity_instances → entity_types`
  et exclut toute entité dont le type est confidentiel sans le droit. Le prédicat
  `(et.is_confidential = false OR @hasConfidentialRight)` est **matérialisé dans le corps SQL** (RL-31), de sorte que le
  canal de fuite **ne se déplace pas** de l'axe vers le graphe.

### 7. Portail Blazor (pages vue-pure + handlers ; pagination keyset ; intégrité GED par re-lecture du coffre)

Pages **maître dans `Liakont.Host`** (page mince + vue-pure bUnit + `IGedQueries` **internal** via `InternalsVisibleTo`),
**aucune logique métier dans les pages** (déléguée aux handlers MediatR). **Toute page sans test bUnit/Playwright = P1**
(CLAUDE.md règle review 19, F19 §6.7).

| Écran | Route | Données |
|---|---|---|
| Recherche | `/ged/recherche` | `IDocumentSearchIndex.SearchAsync` (paginée SQL, keyset) + facettes |
| Fiche document | `/ged/document/{id}` | `IGedDocumentQueries.GetAsync` + aperçu (`ReadableHtml` du paquet) + **intégrité GED** |
| Exploration objet | `/ged/objet/{entityType}/{id}` | `IGedGraphQueries.ExploreAsync` (traversée bornée §6) |

- **Pagination keyset (RL-20)** : la page Recherche **n'utilise pas** le mode chargement-tout de `DeclaredListPage` ; elle
  consomme des pages déjà bornées côté SQL via `IDocumentSearchIndex.SearchAsync`. **Acceptance : aucun chemin ne
  matérialise l'intégralité du corpus.**
- **Intégrité d'une fiche GED** = **re-lecture** `IArchiveStore.ReadAsync(_ged/…)` du paquet, comparée au `content_hash`
  **indexé** (F19 §3.4.1 : `content_hash` SET-ONCE à l'archivage). L'`IArchiveVerifier` (chaîne de hashes + ancrage
  RFC 3161) est **réservé** à un `ManagedDocument` **fiscal** (`archive_entry_id` non nul) — un document GED-seul n'entre
  pas dans la chaîne fiscale (option C, ADR-0033). Le « lien coffre » **ouvre/atteste**, ne modifie jamais. Les montants
  affichés restent `decimal` ; la fiche **ne recalcule rien**.
- **Permissions** : `liakont.ged.read` / `liakont.ged.export` / `liakont.ged.confidential` projetées par
  `RolePermissionCatalog` (ADR-0017). Tant que la matrice n'est pas amendée (GED06), **aucune permission GED n'est
  accordée** (bloquer plutôt qu'inventer ; jamais une permission **socle** accordée à un rôle Liakont, cf. FIX07c/RL-35).
  L'amendement de la matrice est porté par **GED06**, pas par cet ADR.

## Invariants

- **INV-GED-07** — **WORM-neutralité / intégrité produit souveraine.** Côté recherche : le FTS **ne fige aucune valeur
  confidentielle** (les axes confidentiels sont exclus du `search_vector` partagé) ; l'index `document_search` est un
  **dérivé reconstructible** qui ne touche **ni** la chaîne de hashes fiscale, **ni** le coffre WORM, **ni** un change-log
  append-only (sa reconstruction `DELETE`+rebuild ne viole pas la règle 4).
- **INV-GED-09** — **Graphe borné + bidirectionnel.** La traversée est **bidirectionnelle**, avec **borne de profondeur
  dure** (paramètre tenant, jamais infinie), **anti-cycle**, **pagination keyset** (jamais `OFFSET`, jamais chargement-tout
  en mémoire) ; la confidentialité d'une relation **s'hérite des `entity_types`** des extrémités (`entity_relations` n'a
  pas de colonne `is_confidential`).
- **INV-GED-10** — **Confidentialité : masquage server-side** sur **tous les canaux de restitution** (lecture par axe, facette, graphe,
  plein texte, **export et log** — cf. ADR-0036), le prédicat `(is_confidential = false OR @hasConfidentialRight)` étant **matérialisé dans le SQL** (RL-31,
  anti-oracle), jamais une simple omission UI. Le **chiffrement au repos** des valeurs d'axes confidentielles reste **❓ NON
  TRANCHÉ** (D9, owner sécurité).

## Conséquences

**Positif** : un **seul** mécanisme de restitution (`IDocumentSearchIndex`) sert recherche, facettes et plein texte, avec
un point d'extension propre vers OpenSearch/pgvector **sans code dormant** ; le foyer unique `document_search` (dérivé
reconstructible) écarte la double-source du pivot mutable et reste reconstructible sans violer l'immuabilité ; le prédicat
de confidentialité matérialisé dans le SQL ferme **les quatre** canaux de fuite (axe, facette, graphe, plein texte) d'un
seul geste, testable ; la pagination keyset borne mémoire et DoS du graphe ; l'intégrité de fiche par re-lecture du coffre
distingue proprement document GED-seul (option C) et document fiscal (`IArchiveVerifier`). **Aucun mécanisme transverse
nouveau** : on réutilise les vues `current_*` (ADR-0032), les événements GED (ADR-0034), `RolePermissionCatalog`
(ADR-0017) et `IArchiveStore` ; **aucun code `Stratum.*` vendored modifié.**

**À la charge du(des) lot(s) d'implémentation** (items GEDxx de F19 §10) :
- **GED06** : amender `RolePermissionCatalog` (ADR-0017) des permissions `liakont.ged.read`/`export`/`confidential` ;
  tant que non amendée, aucune permission GED accordée.
- **GED08** : migration `tsvector`/`unaccent` (`CREATE EXTENSION` + wrapper IMMUTABLE), table `ged_index.document_search`
  (GIN), projection asynchrone (consommateur `ManagedDocumentReceivedV1` + événements mapping/archivage), recherche par
  axe + facettes + plein texte avec **prédicat de confidentialité matérialisé** (RL-31), traversée de graphe
  bidirectionnelle bornée keyset.
- **GED09a/b/c** : pages `/ged/recherche`, `/ged/document/{id}`, `/ged/objet/{type}/{id}` (vue-pure + bUnit/Playwright,
  aucune logique métier en page, pagination keyset, intégrité GED par re-lecture du coffre).
- **GED10** : backfill rétroactif (chemin direct idempotent) reconstruisant `document_search` sans toucher le flux fiscal.
- **Tests d'acceptance** (F19 §8) : isolation cross-tenant sur recherche/facettes/graphe/liens (document du tenant B
  invisible depuis A) ; confidentialité masquée en **lecture, facette, graphe, export, log** ; portail testé sur chaque
  page ; aucun chemin matérialisant l'intégralité du corpus.

**Limite** : cet ADR ne livre **ni** OpenSearch (GED21), **ni** pgvector (GED22), **ni** l'OCR (GED23), **ni** les
relations objet-à-objet avancées (GED24), **ni** la valeur probante d'un coffre tiers (ADR-0033/GED20). Il ne fixe **ni**
le seuil chiffré de bascule `tsvector → OpenSearch` (D10), **ni** la configuration multilingue (D11), **ni** le chiffrement
au repos des valeurs confidentielles (D9).

### Points NON TRANCHÉS (F19 §11 — défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| D10 | Volumétrie cible par tenant & **seuil chiffré** de bascule `tsvector → OpenSearch` ; synchronisation index synchrone vs asynchrone | Projection **asynchrone** (latence d'ingestion découplée) ; fixer une volumétrie de référence pour rendre « au volume » vérifiable | Produit + exploitation |
| D11 | **Multilingue** du contenu indexé (config `tsvector` ≠ `french`) | V1 `french` (FR-only aligné F10) ; contenu non-FR best-effort ; multilingue fast-follow | Produit |

Aucun de ces points ne stalle le dev : ce sont des **défauts paramétrables / fast-follow**, pas des gates. Le **chiffrement
au repos** des valeurs confidentielles (D9, owner sécurité) reste également ouvert et n'empêche pas le masquage server-side
(INV-GED-10) ; il ne conditionne que la protection **au repos**.

## Alternatives rejetées

- **Poser `search_vector` sur le pivot mutable `managed_documents`** : crée une **double-source non réconciliable**
  (pivot + index) et mêle une colonne dérivée à une table dont l'historique se révise par chaînage. **Rejetée** — foyer
  **unique** `ged_index.document_search`, dérivé reconstructible (F19 §6.1/§6.3).
- **Promouvoir la recherche en 5ᵉ axe enfichable public dès V1** : poserait du **code dormant** (RL-26) pour un seul backend
  livré et étendrait inutilement la surface de plug-in. **Rejetée** — abstraction **interne** `IDocumentSearchIndex`,
  OpenSearch/pgvector en fast-follow seulement quand un 2ᵉ backend est réellement livré (F19 §2.3).
- **`count(DISTINCT code)` naïf en recherche multi-axes** : donne un **faux positif** sur un axe multi-valeur. **Rejetée** —
  on compte les **critères réellement satisfaits** (F19 §6.2).
- **Pagination par `OFFSET` ou chargement-tout en mémoire** (mode `DeclaredListPage`) : ne borne ni la mémoire ni le coût,
  et expose un DoS sur le graphe. **Rejetée** — **pagination keyset** côté SQL, page déjà bornée (RL-20).
- **Traversée de graphe unidirectionnelle** : raterait les documents reliés par une arête entrante. **Rejetée** — traversée
  **bidirectionnelle** (`from_entity_id = r.entity_id OR to_entity_id = r.entity_id`), bornée et anti-cycle (F19 §6.4).
- **Porter `is_confidential` sur `entity_relations`** : dupliquerait l'information et ouvrirait une divergence. **Rejetée** —
  la confidentialité d'une relation **s'hérite des `entity_types`** des extrémités, sans colonne sur la relation (F19 §6.4).
- **Faux « plein texte » sur un scan image sans OCR** : affirmerait une capacité non livrée. **Rejetée** — document
  **clairement non-full-text en V1**, OCR fast-follow (GED23) ; recherche par axes garantie, par contenu conditionnelle
  (CLAUDE.md n°3).
- **Prédicat de confidentialité « en prose » uniquement** (documenté mais pas dans le SQL) : laisse passer une
  implémentation qui omet le filtre sur l'un des quatre canaux (oracle de confidentialité). **Rejetée** — prédicat
  `(is_confidential = false OR @hasConfidentialRight)` **matérialisé dans le SQL** des quatre canaux (RL-31, non
  négociable).

## Références

- `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §2.3 (recherche non promue en axe), §2.4 (flux MVP), §6.1 (projection
  asynchrone), §6.2 (recherche multi-axes + facettes + prédicat RL-31), §6.3 (`tsvector` + `unaccent` + source du texte),
  §6.4 (graphe bidirectionnel borné), §6.5 (confidentialité server-side ; D9), §6.7 (portail Blazor), §8 (tests/DoD,
  jeu `INV-GED-NNN`), §11 (D9/D10/D11) ; INV-GED-07/09/10, INV-ARCH-GED-2.
- ADR liés : `docs/adr/ADR-0032-…` (méta-modèle GED, vues `current_*`, axes typés) ; `docs/adr/ADR-0034-…` (canal
  d'ingestion `ManagedDocumentReceivedV1` peuplant l'index) ; `docs/adr/ADR-0033-…` (export / intégrité du paquet, coffre
  tiers fast-follow GED20, option C) ; `docs/adr/ADR-0036-…` (journal de consultation : toute recherche/consultation
  produite ici est tracée) ; `docs/adr/ADR-0017-pont-role-permission-claims-oidc.md` (permissions GED, confidentialité).
- Code réel cité (non modifié par cet ADR) : `src/Host/Liakont.Host/Security/RolePermissionCatalog.cs` (matrice à amender,
  GED06) ; `src/Common/UI/Components/DeclaredListPage.razor.cs` (mode chargement-tout à éviter, RL-20) ;
  `src/Modules/Archive/Application/ReadableDocumentRenderer.cs` (rendu lisible **réservé au fiscal**, RL-16) ;
  `src/Modules/Archive/Domain/IArchiveStore.cs` (`ReadAsync` write-once, re-lecture d'intégrité GED) ;
  `src/Modules/Archive/Contracts/` (`IArchiveVerifier`, réservé aux `ManagedDocument` fiscaux).
- Provisionnement PostgreSQL : extension `unaccent` (droit superuser à la migration), wrapper IMMUTABLE d'`unaccent()`,
  configuration de recherche `'french'` ; `websearch_to_tsquery`, `setweight`, index GIN sur `tsvector`.
