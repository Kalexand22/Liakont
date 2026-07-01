# Plan — Référentiel de correspondance pays (ISO 3166) hors agent

> Spec : **ADR-0038** (`docs/adr/ADR-0038-referentiel-correspondance-pays-cross-instance.md`).
> Recette : `tasks/bugs-recette-encheres-b2c.md` (« Table de correspondance pays »).
>
> Principe : sortir la table `ENG/JAP/BEL…→ISO` de l'AGENT ; normaliser au **read-time plateforme** (CHECK/
> SEND/affichage), **jamais** dans l'empreinte anti-doublon ; référentiel **cross-instance** éditable en
> console, **audité append-only**, cible **validée ISO** à l'écriture. Home NEUTRE (pas Supervision).

## Lot 1 — Agent : transport brut

- [ ] `EncheresV6RowMapper.cs` : supprimer `NonIsoCountryCodeMap` (:53-69) et `NormalizeCountryCode` (:730-748) ;
  `MapAddress` (:727) → `countryCode: NullIfBlank(countryCode)`. **NE PAS toucher** `ExportZone`/`ComposeRegimeKey`
  (:588-624) ni `NormalizeCurrency` (:842-856, alias devise fermé — conservé sciemment, cf. ADR §Contexte).
- [ ] `EncheresV6RowMapperTests` (:154-194) : recaster les 2 tests → `ENG`/`JAP`/`ZZ` transportés **BRUTS**
  (comportemental, PAS un NetArchTest de contenu). `FR` inchangé.

## Lot 2 — Module `Reference` (home neutre, base système)

- [ ] Nouveau `Liakont.Modules.Reference` (Contracts + Infrastructure) — home neutre (PAS Supervision).
- [ ] `Reference.Contracts` : `ICountryAliasReferential` (lecture : `ResolveAsync(string raw, ct)` +/ou
  `GetAliasesAsync(ct)`) ; commande admin `UpsertCountryAliasCommand` / `RemoveCountryAliasCommand` (MediatR).
- [ ] `Reference.Infrastructure` : `PostgresCountryAliasReferential` via **`ISystemConnectionFactory`** (patron
  `PostgresSourceTaxRegimeWriter/Queries`), table **UNIVERSELLE sans `tenant_id`** ; **cache mémoire singleton**
  invalidé à l'écriture (chemin chaud). **Validation ISO de la cible** à l'upsert (`CountryCodeValidator`).
- [ ] **Journal APPEND-ONLY** des mutations (discipline `MappingChangeLogEntry`) dans la **même transaction**
  que l'upsert (auteur + avant/après). Pas de simple `updated_by` mutable.
- [ ] Migration `V001__create_country_alias_referentiel.sql` : `CREATE SCHEMA reference; CREATE TABLE
  reference.country_alias (source_code text PK MAJ, iso_code text NOT NULL, ...)` + table de journal append-only
  (triggers rejet UPDATE/DELETE) ; **seed universel** `ENG/SCO/WAL/NIR→GB`, `JAP→JP` (faits ISO).
- [ ] `ReferenceModuleRegistration` + enregistrement `MigrationAssembliesOptions`.

## Lot 3 — Pipeline : normalizer read-time (3 points)

- [ ] `PivotCountryNormalizer` (Pipeline.Infrastructure, miroir `PivotEmitterEnricher`) : reconstruit le pivot
  en normalisant `Customer.Address.CountryCode` via `ICountryAliasReferential` ; **null-safe**, **idempotent**
  (code déjà ISO/inconnu inchangé). Commentaire de garde : étendre à `Supplier` si un futur producteur (389/F15)
  le remplit.
- [ ] Câbler AVANT mapping/validation en **3 points** : `DocumentCheckEvaluator` (:105, avant BT-55),
  `SendTenantJob` (:1006, avant sérialisation PA/Factur-X — résoudre le référentiel une fois en tête de job),
  `DocumentContentReplayService` (:140, affichage).
- [ ] `ProjectReference` Pipeline.Infrastructure → **Reference.Contracts** (module→Contracts).

## Lot 4 — Console (gabarit design-system, gate Settings)

- [ ] `ReferentielPays.razor` (Host) sur `<DeclaredListPage>` (gabarit `AdminAgents`/`Supervision.razor`) :
  liste + add/edit, **mutation via MediatR**. `@attribute [Authorize(Policy = LiakontPermissions.Settings)]`
  (**PAS** Supervision). Zéro logique métier.
- [ ] `CountryAliasColumnRegistry` (`ColumnRegistryBase<CountryAliasDto>`) : Code source / Code ISO / Modifié le.
- [ ] Nav : NavNode « Référentiel pays » sous un menu **Paramétrage** (gate `liakont.settings`).

## Lot 5 — Tests

- [ ] Normalizer (unit) : `ENG→GB`, `JAP→JP` (référentiel stubé) ; ISO/inconnu inchangé ; null-safe ; idempotent.
- [ ] CHECK (unit/intégration) : acheteur `ENG` **passe** BT-55 (→GB) ; `ZZ` reste **bloqué** (fail-closed).
- [ ] **SEND** : le payload PA/Factur-X sortant **porte le code ISO** (pas le brut).
- [ ] **Affichage** : replay normalisé.
- [ ] Store (intégration Testcontainers) : upsert/list/resolve en base système via `ISystemConnectionFactory` ;
  table sans `tenant_id` ; casse/espaces normalisés à la clé ; **cible non-ISO refusée à l'écriture**.
- [ ] **Journal d'audit** : une mutation écrit une entrée append-only (auteur), UPDATE/DELETE rejetés.
- [ ] **Invalidation de cache** CHECK/SEND après upsert (un alias ajouté est vu au run suivant).
- [ ] Agent (unit) : ENG/JAP/ZZ transportés bruts.
- [ ] bUnit page + gate de permission (P1 review n°19).
- [ ] (Optionnel) harnais de frontière Pipeline si on veut garder Pipeline↛Reference.Infrastructure (aucun
  n'existe aujourd'hui — ne PAS prétendre « NetArchTest vert »).

## Points ouverts (ADR-0038 §Points NON TRANCHÉS)

- **D1** home exact (défaut : module dédié `Reference`).
- **D2** TOCTOU CHECK→SEND (re-normaliser au SEND, fail-closed rejoue).
- **D3** documents déjà ingérés : pas de migration de données (build) ; ré-extraction ré-émet.
