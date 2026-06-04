# Validation Module

> Module métier Liakont (namespace `Liakont.Modules.Validation.*`). Spec : `docs/conception/F04-Controles-Qualite-Validation.md`. Item fondateur : **VAL01** (framework). Règles métier : VAL02-VAL05.

## Purpose

Détecte **avant l'envoi** tout ce qu'une Plateforme Agréée (PA) rejetterait après envoi (F04 §1) :
position `Extract → CHECK → Send`. Un document qui échoue à un contrôle **bloquant** reste `Blocked`
et n'est jamais transmis (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3).

VAL01 livre le **socle** : le contrat d'une règle (`IDocumentRule`), le résultat agrégé
(`ValidationResult` / `ValidationIssue` / `ValidationSeverity`) et le moteur qui exécute les règles
et agrège leurs anomalies (`ValidationPipeline`). Les règles concrètes arrivent ensuite (identité
VAL02, cohérence VAL03, TVA/avoirs VAL04, garde-fou B2B/B2C VAL05).

Le module **détecte**, il ne **corrige jamais** les données (frontière `Validation` — `module-rules.md` §2).
Toute la logique de validation vit sur la **plateforme**, jamais dans l'agent (CLAUDE.md n°6).

## Boundaries

- **Owns:** le contrat de règle et le moteur d'agrégation (`Contracts` + `Domain`). Aucun schéma de
  base de données : VAL01 est un framework en mémoire, sans persistance propre.
- **Reads:** le document à valider est fourni en entrée (modèle pivot `Liakont.Agent.Contracts.Pivot`,
  lecture seule). Les règles qui ont besoin de paramétrage tenant (profil émetteur, table TVA) le
  liront via les **Contracts** des modules concernés (TenantSettings, TvaMapping), scopé par `CompanyId`.
- **Does NOT:** ne corrige/ne normalise jamais les données (détection seule) ; n'affaiblit jamais une
  anomalie bloquante en alerte (CLAUDE.md n°3) ; n'invente aucune règle fiscale (toute catégorie/seuil
  vient de `docs/conception/F*.md` — CLAUDE.md n°2) ; ne référence aucun module hors de ses `Contracts`.

## Decisions VAL01

- **`IDocumentRule.ValidateAsync` est asynchrone** (la spec F04 §5 illustre une signature synchrone).
  Motif : les règles aval interrogent d'autres modules (unicité du numéro via Documents — VAL03 ;
  couverture du mapping via TvaMapping — VAL04). Un contrat synchrone forcerait une rupture de contrat
  deux items plus loin. La méthode reste pure (sans effet de bord) et retourne la liste vide si conforme.
- **Garantie « jamais de règle silencieuse »** : une règle qui lève une exception est convertie par le
  pipeline en anomalie **bloquante** `RULE_CRASHED` (jamais un passage en succès — CLAUDE.md n°3).
  L'annulation (`OperationCanceledException`) est propagée, jamais convertie en `RULE_CRASHED`.
- **`ValidationResult.IsValid`** (nommage de l'item VAL01) = absence d'anomalie bloquante ; `HasBlockingIssue`
  est l'inverse (le « IsBlocking » de F04 §5). Les alertes (Warning) n'invalident pas le document (F04 §3.3).
- **Persistance différée :** la persistance des anomalies avec le document (F04, console WEB03) relève du
  module **Documents** (lot TRK), absent à ce stade. VAL01 ne porte aucune persistance.

## Decisions VAL02

- **Validateurs élémentaires dans `Domain/Identity`** (`SirenValidator`, `SiretValidator`,
  `FrenchVatNumberValidator`, `CountryCodeValidator`) : fonctions pures, sans dépendance. Algorithmes
  et dérogations (Luhn, exception La Poste 356000000, clé TVA, liste ISO 3166-1 alpha-2) viennent de
  F04 §3.1/§3.2/§4.1/§4.2 — aucune règle inventée (CLAUDE.md n°2). Ils remplaceront à terme la copie
  temporaire `TenantSettings.Domain.Services.SirenValidator` (CFG02) ; consolidation hors périmètre
  VAL02 (frontière inter-modules : TenantSettings ne peut pas référencer `Validation.Domain`).
- **Le SIREN émetteur de référence vient du profil tenant**, lu via `ITenantSettingsQueries`
  (`TenantSettings.Contracts`) scopé par `CompanyId` (note v6, item VAL02) — pas du document. La règle
  vérifie présence + validité + cohérence document↔profil ; tout écart est **bloquant** (CLAUDE.md n°3).
- **`SupplierIdentityRule` / `BuyerIdentityRule`** implémentent `IDocumentRule` (asynchrone côté
  émetteur car il lit le profil tenant ; pur côté acheteur). **Câblage différé** : l'enregistrement DI
  des règles et le branchement du `ValidationPipeline` dans le pipeline d'envoi (lot PIP) ne sont pas
  portés par VAL02 — cohérent avec la posture « persistance/câblage différés » de VAL01. Les règles
  sont consommées par le `ValidationPipeline` existant une fois enregistrées.
- **Code pays = liste ISO 3166-1 alpha-2 officielle (249 codes) embarquée**. `BUYER_COUNTRY_INVALID`
  étant bloquant, une liste incomplète provoquerait un faux blocage : la liste est figée et testée.
  Le Kosovo (XK, user-assigned) n'est pas reconnu — évolution par mise à jour explicite, jamais devinée.

## Published Events

Aucun. (Le résultat de validation est consommé par le pipeline d'envoi — lot PIP — via les `Contracts`.)

## Consumed Events

Aucun.

## Dependencies

- `Liakont.Agent.Contracts` (modèle pivot du document en entrée — `PivotDocumentDto`).
- `Liakont.Modules.TenantSettings.Contracts` (VAL02) : lecture du profil émetteur du tenant
  (`ITenantSettingsQueries.GetTenantProfile`), scopée par `CompanyId`. Frontière inter-modules
  **Contracts-only** respectée (`module-rules.md` §3).
