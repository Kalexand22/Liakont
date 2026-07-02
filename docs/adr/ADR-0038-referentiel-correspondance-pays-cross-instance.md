# ADR-0038 — Référentiel de correspondance pays (ISO 3166) : normalisation cross-instance au read-time plateforme, hors agent

- **Statut** : Accepté (2026-07-01) — **implémenté** (branche `feat/recette-encheres-referentiel-pays`).
- **Date** : 2026-07-01
- **Nature** : cet ADR **précède** le chantier d'implémentation. Il corrige un écart de recette EncheresV6 (Karl,
  2026-07-01) : la table de correspondance des codes pays non-ISO (`ENG/SCO/WAL/NIR→GB`, `JAP→JP`) est
  **codée en dur dans l'AGENT** (`EncheresV6RowMapper.NonIsoCountryCodeMap`), sans **aucun accès console** —
  ce qui contredit le principe « une table de correspondance qui varie par source = paramétrage, **jamais
  codée en dur** » (F04 §2.4 ; CLAUDE.md n°7). Les sections **Décision** et **Invariants** sont **normatives**
  (cible, pas état du code). Aucune règle fiscale n'est inventée (CLAUDE.md n°2) : la validation ISO 3166-1
  (BT-55) et son aiguillage existent déjà — cet ADR ne décide **que** l'emplacement et le cycle de vie de la
  **normalisation** d'un code source vers son code ISO.

- **Numérotation** : ADR-**0038** (prochain libre après ADR-0037 ; `0031` attribué deux fois, jamais réutilisé).

- **Contexte décisionnel** : recette EncheresV6 (`tasks/bugs-recette-encheres-b2c.md`, « Table de correspondance
  pays »). Sources code réelles : `agent/src/Liakont.Agent.Adapters.EncheresV6/EncheresV6RowMapper.cs`
  (`NonIsoCountryCodeMap` :53-69, `NormalizeCountryCode` :730-748, `MapAddress` :727 seul appelant ;
  `NormalizeCurrency` :842-856 = normalisation SŒUR conservée) ; validation :
  `src/Modules/Validation/Domain/Rules/BuyerIdentityRule.cs` (BT-55, `BUYER_COUNTRY_INVALID`, Blocking) +
  `src/Modules/Validation/Domain/Identity/CountryCodeValidator.cs` (liste ISO 3166-1 alpha-2) ; read-time
  plateforme : `src/Modules/Pipeline/Infrastructure/PivotEmitterEnricher.cs` (patron d'enrichissement
  read-time, jamais à l'ingestion — RB9/ADR-0031), `Check/DocumentCheckEvaluator.cs`,
  `Send/SendTenantJob.cs`, `Check/DocumentContentReplayService.cs` (affichage) ; anti-doublon sur pivot
  SOURCE : `src/Modules/Ingestion/Infrastructure/Handlers/Commands/IngestDocumentBatchHandler.cs` (hash à
  l'ingestion) ; patron table système : `src/Modules/Ingestion/Infrastructure/PostgresSourceTaxRegimeWriter.cs`
  + `Migrations/V005__create_source_tax_regimes_table.sql`, `ISystemConnectionFactory` ; patron audit
  append-only d'un mapping ÉDITÉ : `src/Modules/TvaMapping/Application/MappingChangeLogEntry.cs` ; chiffrement
  N/A. ADR liés : ADR-0031 (enrichissement read-time), ADR-0004 (périmètre agent = extraction+transport).

## Contexte

Le code pays d'une adresse arrive de la source EncheresV6 sous des formes **non-ISO** (`ENG`, `SCO`, `WAL`,
`NIR` pour le Royaume-Uni ; `JAP` pour le Japon). La plateforme valide le code contre la **liste ISO 3166-1
alpha-2** (`CountryCodeValidator`) via la règle **Blocking BT-55** (`BuyerIdentityRule`,
`BUYER_COUNTRY_INVALID`). Aujourd'hui, l'**agent** normalise ces codes AVANT transport
(`NonIsoCountryCodeMap` + `NormalizeCountryCode`, fail-closed : un code inconnu reste brut et sera bloqué).

Deux problèmes :

1. **Aucun accès console.** La table est un `Dictionary` **compilé dans l'agent** : ajouter `BEL→BE` (relevé
   Karl : `BEL` non mappé → transporté brut → bloqué BT-55) exige un redéploiement d'agent. Or une table de
   correspondance qui **varie par source** est du **paramétrage** (F04 §2.4 ; CLAUDE.md n°7) — pas du code.
   La Supervision (ou une surface de paramétrage) doit l'exposer ; elle a **0 surface pays** aujourd'hui.

2. **Mauvais emplacement (agent).** L'agent est **extraction + transport** (ADR-0004) ; la normalisation
   d'identité/de référentiel appartient à la **plateforme** ([[prefer-simplest-correct-architecture]]).

**Le vrai moteur du déplacement est l'ÉDITABILITÉ**, pas la « pureté » de l'agent. Nuance importante (relevée
en revue de conformité) : `NormalizeCurrency` (`EURO→EUR`) **reste** dans l'agent — c'est un **alias ISO 4217
fermé, universel, sans effet d'aiguillage fiscal**. Le code **pays**, lui, est un **référentiel OUVERT**
(l'opérateur doit pouvoir l'étendre) **et à conséquence fiscale** (il pilote l'aiguillage UE/hors-UE). C'est
cette double propriété — **ouvert + à impact fiscal** — qui justifie de le sortir dans un référentiel
paramétrable **audité**, pas un argument de pureté (qui condamnerait aussi la devise). L'asymétrie
devise-reste / pays-sort est donc **assumée** et tracée ici.

## Décision

### 1. L'agent transporte le code pays BRUT

On **supprime** `NonIsoCountryCodeMap` (:53-69) et `NormalizeCountryCode` (:730-748) ;
`MapAddress` (:727) passe `countryCode: NullIfBlank(countryCode)` (transport brut). On **ne touche pas** à
`ExportZone`/`ComposeRegimeKey` (:588-624, zone d'export F03 §2.8, autre concern) ni à `NormalizeCurrency`
(:842-856, alias devise fermé — **conservé sciemment**, voir Contexte). Les 2 tests agent
(`EncheresV6RowMapperTests` :154-194) sont **recastés** en comportement : `ENG`/`JAP`/`ZZ` sont désormais
transportés **BRUTS** (la normalisation a migré côté plateforme).

### 2. Normalisation au READ-TIME plateforme (CHECK + SEND + affichage), JAMAIS dans l'empreinte anti-doublon

Un nouveau `PivotCountryNormalizer` (Pipeline.Infrastructure, **miroir de `PivotEmitterEnricher`**)
reconstruit le pivot en normalisant `Customer.Address.CountryCode` via le référentiel (§4). Il est appliqué
au **read-time** en **trois points**, exactement comme l'enrichissement émetteur :

- **CHECK** (`DocumentCheckEvaluator`, avant mapping/validation) → `BuyerIdentityRule` (BT-55) voit le code
  **ISO** ;
- **SEND** (`SendTenantJob`, avant sérialisation PA/Factur-X) → le **payload sortant** porte le code ISO ;
- **AFFICHAGE** (`DocumentContentReplayService`) → l'opérateur voit le code normalisé (parité).

**JAMAIS à l'ingestion.** L'anti-doublon F06 hashe le pivot **SOURCE** à l'ingestion
(`IngestDocumentBatchHandler`) : normaliser AVANT le hash ferait **diverger l'empreinte à chaque édition du
référentiel** → fausse détection d'altération source (INV). C'est précisément la raison pour laquelle
`PivotEmitterEnricher` enrichit au read-time et jamais à l'ingestion (RB9/ADR-0031). Le mot « ingestion » du
besoin de recette = **côté plateforme** (vs agent), réalisé au read-time du pipeline.

### 3. Fail-closed : un code non mappé reste BRUT → bloqué BT-55 (validation Blocking INCHANGÉE)

La normalisation tourne **avant** BT-55, sans jamais l'affaiblir (CLAUDE.md n°3/11). Un alias connu
(`ENG→GB`) devient un code ISO **valide** → passe BT-55 ; un code **non mappé** (`ZZ`) reste **brut** →
**bloqué** BT-55 (parité exacte avec l'agent actuel). `BuyerIdentityRule`/`CountryCodeValidator` ne sont
**pas modifiés**.

### 4. Référentiel CROSS-INSTANCE universel, dans un home NEUTRE (PAS Supervision)

Le référentiel est une table **universelle** (`BEL→BE` vrai pour tout tenant), **sans colonne `tenant_id`**,
en **base système**, lue via `ISystemConnectionFactory` (patron `source_tax_regimes`). Elle vit dans un home
**NEUTRE** — un module dédié minimal `Liakont.Modules.Reference` (Contracts + Infrastructure) — **et NON dans
Supervision**. Motif (revue de conformité) : Supervision est un module **tenant-scopé** dont la seule surface
cross-tenant est une **agrégation en LECTURE SEULE** ; il **observe** déjà le pipeline
(`Supervision → Ingestion/Documents/TenantSettings.Contracts`). Y greffer une table éditée que le **pipeline
fiscal** (chemin chaud CHECK/SEND) devrait consommer **inverserait** la relation d'observation et diluerait
l'invariant « Supervision = read-only cross-tenant » (CLAUDE.md n°9). Le pipeline consomme
`Reference.Contracts.ICountryAliasReferential` (module → **Contracts** uniquement, frontière n°6 respectée) ;
il ne dépend **jamais** de Supervision.

### 5. Mutations APPEND-ONLY (discipline `MappingChangeLog`), validées à l'écriture

Le référentiel pilote l'**aiguillage fiscal** (UE/hors-UE) : un alias **faux-mais-valide** (`ENG→FR`)
passerait BT-55 et **mis-router**ait silencieusement le document (validé mais faux — CLAUDE.md n°2/3). Deux
garde-fous **non négociables**, calqués sur le mapping TVA (mapping ÉDITÉ à impact fiscal, pas sur
`source_tax_regimes` qui est une métadonnée auto-observée) :

1. **Journal APPEND-ONLY** de chaque mutation (ajout/modif/suppression d'alias), écrit dans la **même
   transaction** que l'upsert, immuable, avec **auteur** — discipline `MappingChangeLogEntry` (CLAUDE.md
   n°4). Pas de simple colonne `updated_by` mutable.
2. **Validation de la CIBLE à l'écriture** : seul un **vrai code ISO 3166-1 alpha-2**
   (`CountryCodeValidator`) est storable en cible (un `XX` garbage est refusé à l'écriture, pas seulement en
   aval par BT-55). La clé source est normalisée (Trim + MAJ).

### 6. Console de gestion (gabarit design-system), gate `liakont.settings`

Une page Host sur `<DeclaredListPage>` (gabarit `AdminAgents`/`Supervision.razor`, **jamais** de grille
maison — P1 [[console-web-stratum-design-system]]) : liste + surface add/edit, **mutation via commande
MediatR** (parité audit). Gate **`LiakontPermissions.Settings`** sous un menu **Paramétrage** — **pas**
`liakont.supervision` (permission documentée « lecture seule, aucune action mutante » : gater une écriture
avec elle se contredit). Aucune logique métier dans la page (déléguée au handler — CLAUDE.md n°19). **Test
bUnit obligatoire** (page Blazor sans test = P1, review n°19).

### 7. Seed universel, cache invalidé à l'écriture, aucun package

Les alias **universels** `ENG/SCO/WAL/NIR→GB`, `JAP→JP` sont **seedés** dans la migration (faits ISO, pas
donnée client — ils restent éditables en console). Le référentiel est **caché en mémoire** (singleton)
invalidé à chaque écriture admin (chemin chaud CHECK/SEND, petit volume). **Aucun** package NuGet.

## Invariants

- **INV-REF-CTRY-01** — Le référentiel de correspondance pays est une table **cross-instance universelle**
  (aucun `tenant_id`), en **base système** via `ISystemConnectionFactory`, dans un home **neutre**
  (`Reference`), **jamais** dans Supervision. Le pipeline le consomme via **Contracts** uniquement.

- **INV-REF-CTRY-02** — La normalisation s'applique au **read-time** (CHECK, SEND, affichage) et **jamais à
  l'ingestion** : l'empreinte anti-doublon F06 reste calculée sur le pivot **SOURCE** (une édition du
  référentiel ne peut pas déclencher de fausse altération source).

- **INV-REF-CTRY-03** — La validation **Blocking BT-55** n'est pas affaiblie : un code non mappé reste brut
  et **bloque** (fail-closed). Seul un **vrai code ISO 3166-1 alpha-2** est storable comme **cible** d'alias
  (validé à l'écriture), et toute mutation est **journalisée append-only** (même transaction, auteur) —
  discipline `MappingChangeLog` (CLAUDE.md n°4), car le pays pilote l'aiguillage fiscal.

## Conséquences

**Positif** : la correspondance pays devient du **paramétrage éditable** (F04 §2.4) avec accès console, au
lieu d'un `Dictionary` compilé dans l'agent ; l'agent revient à **extraction + transport brut** (ADR-0004) ;
la normalisation vit au bon endroit (read-time plateforme, patron `PivotEmitterEnricher`) **sans polluer
l'empreinte anti-doublon** ; la validation Blocking BT-55 est **inchangée** (fail-closed préservé) ; les
mutations sont **auditées append-only** et la cible **validée ISO** (anti mis-routage silencieux) ; aucun
package ; home neutre → **aucune inversion** de dépendance Pipeline↔Supervision.

**À la charge du(des) lot(s) d'implémentation** : suppression `NonIsoCountryCodeMap` + `NormalizeCountryCode`
(agent) + recast des 2 tests agent en comportement (ENG/JAP/ZZ bruts) ; module `Reference`
(Contracts `ICountryAliasReferential` lecture + commande admin ; Infrastructure store système +
**migration** table + **journal append-only** + validation ISO d'écriture + cache invalidé) ;
`PivotCountryNormalizer` (Pipeline.Infrastructure, null-safe, idempotent) câblé en **3 points** (CHECK/SEND/
affichage) + `ProjectReference` Pipeline→Reference.Contracts ; page console `<DeclaredListPage>` + registre de
colonnes + entrée nav sous Paramétrage, gate `liakont.settings` ; **tests** : agent (bruts), normalizer (ENG→GB,
JAP→JP, inconnu brut, null-safe, idempotent), CHECK (ENG passe BT-55, ZZ bloqué), **SEND** (payload sortant
porte le code ISO), **affichage** (replay normalisé), store système (round-trip via `ISystemConnectionFactory`,
sans `tenant_id`), **journal d'audit** d'une mutation, **invalidation de cache** CHECK/SEND après édition,
bUnit page + gate de permission. Si une garde de frontière est voulue (Pipeline ne référence pas
Reference.Infrastructure), **écrire un vrai harnais de frontière** (aucun n'existe pour Pipeline aujourd'hui) —
ne **pas** prétendre « NetArchTest vert ».

**Limite** : cet ADR normalise `Customer.Address.CountryCode` (toutes les parties source EncheresV6 y
atterrissent ; `Supplier`/`Payee` = null/rempli plateforme). Si un futur producteur (autofacturation 389, F15)
remplit `Supplier` avec un pays source, **étendre** le normalizer (garde à commenter). Il ne fusionne pas non
plus l'override SMTP/pays par tenant (aucun ici).

### Points NON TRANCHÉS (défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|-------|------------------------|-------|
| D1 | Home exact du référentiel neutre (module dédié `Reference` vs adjacent `TenantSettings`/`Settings` vs `Common`). | **TRANCHÉ à l'implémentation → module dédié minimal `Liakont.Modules.Reference`** (Contracts + Infrastructure) — frontière la plus propre (Pipeline → Contracts, aucune inversion). Alternative si le module gêne : porter le store dans un module déjà cross-instance en écriture, Supervision restant en lecture. Sans impact sur le reste du design. | Karl + implémentation |
| D2 | TOCTOU : référentiel édité **entre** CHECK et SEND → le payload envoyé peut diverger du code validé. | Accepté comme **read-time-courant** (même sémantique que `PivotEmitterEnricher`) ; SEND ne doit pas émettre **en silence** un code désormais dé-aliasé (re-normalise au SEND, le fail-closed rejoue). À surveiller si un besoin de gel apparaît. | Karl |
| D3 | Documents déjà ingérés : leur pivot source porte le code brut (empreinte inchangée) ; ils remontent normalisés au prochain read-time. | **Aucune migration de données** en build ([[calibrer-severite-review-sur-stade-build]]) ; une ré-extraction ré-émet normalement. À signaler au client au déploiement, jamais cadré en incident prod. | — |

### Note d'implémentation (2026-07-01)

- **D1 tranché → module dédié `Liakont.Modules.Reference`** (`Contracts` + `Infrastructure`), câblé dans
  `AppBootstrap` (`AddReferenceModule()`). Pipeline ne référence que `Reference.Contracts`
  (`ICountryAliasReferential`) — frontière n°6/14 respectée.
- **Validation ISO de la cible — duplication assumée, verrouillée par un test de parité.** `CountryCodeValidator`
  (source ISO 3166-1) vit dans `Validation.Domain` ; `Reference` **ne peut pas** le référencer (module → Domain
  d'un autre module = P1, frontière n°14). La liste ISO est donc **dupliquée** dans un
  `IsoCountryReference` **interne** à `Reference.Infrastructure`, et un **test de parité**
  (`IsoCountryReferenceParityTests`, 676 combinaisons alpha-2 + cas limites) — le seul endroit autorisé à
  référencer les DEUX modules (projet de test, hors production) — **échoue** si les deux listes divergent. La
  production reste propre ; la dérive est empêchée par le test, pas par un couplage.
- **Journal append-only** matérialisé par `reference.country_alias_change_log` + **triggers Postgres** qui
  rejettent `UPDATE`/`DELETE` (FOR EACH ROW) et `TRUNCATE` (FOR EACH STATEMENT) ; test d'intégration
  `The_change_log_rejects_update_delete_and_truncate` (Testcontainers) prouve les TROIS rejets sur base réelle
  (dont la purge en masse TRUNCATE).
- **Câblage read-time couvert de bout en bout** : `Replay_Normalizes_The_Buyer_Country_Alias_To_Its_Iso_Code`
  (Pipeline.Tests.Integration) fait remonter un pays legacy `ENG→GB` à travers DI + base réelle (référentiel
  résolu par `DocumentContentReplayService`) — supprimer un appel de câblage fait rougir la suite (anti faux-vert).
- **Garde de frontière Pipeline→Reference.Infrastructure NON écrite** (honnêteté : aucun harnais NetArchTest
  n'existe pour Pipeline aujourd'hui — cf. Conséquences). Non prétendue « verte ».

## Alternatives rejetées

- **Garder la table dans l'agent (statu quo)** : `Dictionary` compilé, aucun accès console, redéploiement
  d'agent pour ajouter un alias — contredit F04 §2.4 / CLAUDE.md n°7 (paramétrage jamais codé en dur), et
  place une correspondance à impact fiscal hors de tout audit. **Rejetée**.
- **Loger le référentiel dans le module Supervision** : Supervision est **tenant-scopé** (sa table vit en base
  tenant, sa seule surface cross-tenant est en LECTURE SEULE) et **observe** le pipeline ; le pipeline fiscal
  ne doit pas en dépendre (inversion + dilution de l'invariant read-only, CLAUDE.md n°9). **Rejetée** — home
  neutre `Reference`.
- **Normaliser à l'ingestion (avant le hash anti-doublon)** : une édition du référentiel ferait diverger
  l'empreinte du pivot source → **fausses altérations** détectées (F06). **Rejetée** — read-time uniquement
  (INV-REF-CTRY-02).
- **`updated_by` mutable façon `source_tax_regimes`** : `source_tax_regimes` est une métadonnée
  **auto-observée** ; un référentiel **édité par l'opérateur à impact fiscal** exige la discipline
  **append-only** du mapping TVA (traçabilité de chaque changement, CLAUDE.md n°4). **Rejetée** — journal
  append-only.
- **Cadrer la table comme « simple miroir de `EURO→EUR` » (n°2 non concerné)** : le pays **pilote
  l'aiguillage** UE/hors-UE, là où la devise n'a aucun effet de routage ; un alias faux-mais-valide
  mis-route en silence. **Rejetée** — cadrage honnête (conséquence fiscale) + audit append-only + validation
  ISO de la cible à l'écriture.
- **Gater la page derrière `liakont.supervision`** : cette permission est documentée « lecture seule, aucune
  action mutante » ; y accrocher une écriture se contredit. **Rejetée** — `liakont.settings` sous Paramétrage.

## Références

- Recette : `tasks/bugs-recette-encheres-b2c.md` (« Table de correspondance pays »). Plan d'implémentation :
  `tasks/plan-referentiel-pays.md`.
- Conception : F04 §2.4 (tables de correspondance = paramétrage, jamais codées en dur), §3.2 (BT-55) ;
  CLAUDE.md n°2 (aucune règle fiscale inventée), n°3/11 (ne pas affaiblir Blocking), n°4 (audit append-only),
  n°5/6 (frontières, agent = extraction+transport), n°7 (donnée qui varie = paramétrage), n°9 (Supervision =
  seul cross-tenant, lecture seule), n°19 (page Blazor sans test = P1).
- Sources code : `agent/.../EncheresV6RowMapper.cs` (`NonIsoCountryCodeMap`/`NormalizeCountryCode`/`MapAddress`,
  `NormalizeCurrency` conservé) ; `Validation/Domain/Rules/BuyerIdentityRule.cs` +
  `Identity/CountryCodeValidator.cs` (BT-55) ; `Pipeline/Infrastructure/PivotEmitterEnricher.cs` (patron
  read-time) + `Check/DocumentCheckEvaluator.cs` + `Send/SendTenantJob.cs` +
  `Check/DocumentContentReplayService.cs` ; `Ingestion/.../IngestDocumentBatchHandler.cs` (hash source) +
  `Ingestion/Infrastructure/PostgresSourceTaxRegimeWriter.cs` + `Migrations/V005…` (patron table système) ;
  `TvaMapping/Application/MappingChangeLogEntry.cs` (patron audit append-only d'un mapping édité) ;
  `Common/Infrastructure/Database/ISystemConnectionFactory.cs`. ADR liés : ADR-0031 (enrichissement
  read-time), ADR-0004 (périmètre agent).
