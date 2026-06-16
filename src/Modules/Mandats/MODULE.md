# Module Mandats

## Purpose

Socle de l'**autofacturation sous mandat** (type 389, art. 289 I-2 CGI — F15, ADR-0022). Le module
porte le **mandant** (le vendeur au nom et pour le compte duquel le tenant émet) comme **tiers récurrent
de première classe** du tenant — **jamais un sous-tenant** (blueprint §7) — et le **cycle de vie du
mandat** (clause, caractère écrit/tacite, statut d'assujettissement déclaré, délai de contestation,
validation humaine, révocation).

**Périmètre de l'item MND01 (FONDATION — aucune décision fiscale, structure seulement)** :
- le **modèle** : agrégats `Mandant` (clé `(company_id, reference)`) et `Mandat`
  (clé `(company_id, mandant_id, reference)`, gabarit FORT `MappingTable`) ;
- la **persistance** PostgreSQL par tenant (schéma `mandats`, migrations DbUp) ;
- le **journal append-only** `mandat_change_log` (registre + cycle de vie), immuable par double trigger base ;
- le **suspendu-par-défaut** : `AssujettissementStatus`/`ContestationDelay` `null`, mandat non validé ou
  révoqué ⇒ **389 suspendu** (`Mandat.IsSelfBillingSuspended`), jamais un défaut inventé.

**Périmètre de l'item MND02 (workflow d'acceptation — ADR-0024, F15 §2.3)** :
- l'agrégat **`SelfBilledAcceptance`** (clé `(company_id, document_id)`, état mutable) et sa **machine fermée**
  `PendingAcceptance → {Accepted, TacitlyAccepted, Contested}` (aucun retour arrière, INV-ACCEPT-4) ;
- le **journal append-only** `self_billed_acceptance_log` : CHAQUE transition (création incluse) écrit une
  ligne dans la MÊME transaction (INV-ACCEPT-5), immuable par double trigger base ;
- l'état calculé **« gate ouvert »** (`IsAccepted` = Accepted ou TacitlyAccepted) exposé aux Contracts.
- Hors MND02 : la **garde** d'émission (port `ISelfBilledGate` → MND03), la **bascule tacite** par job
  (MND04), l'**allocation** du BT-1 fiscal (MND05), et l'**avoir 261** d'un `Contested` (NON TRANCHÉ F15 §6.5).

**Périmètre de l'item MND03 (garde d'émission — ADR-0024 §3, F15 §2.3, INV-ACCEPT-2)** :
- le **port `ISelfBilledGate`** (Contracts) + son implémentation `SelfBilledGate` (lit `IsAccepted` via
  `ISelfBilledAcceptanceQueries`, **fail-closed** si aucune acceptation) — par **inversion de dépendance** ;
- le **branchement pipeline** : `DocumentCheckEvaluator` (source UNIQUE de la décision de blocage : CHECK +
  recheck + réconciliation des avoirs) interroge le gate pour un pivot `IsSelfBilled` et **maintient `Blocked`**
  (nouveau motif, **aucun** nouvel état `DocumentState`) un document dont l'acceptation n'est pas acquise ;
  un recheck post-acceptation rouvre le gate (Blocked → ReadyToSend).
- Hors MND03 : la création de l'enregistrement `SelfBilledAcceptance` à la détection d'un document self-billed
  n'est câblée par aucun item du lot à ce stade (le gate « interroge » = lecture seule, fail-closed) — à
  rattacher au flux de bout en bout avant la recette `GATE_AUTOFACTURATION`.

**Hors périmètre de MND01/MND02 (items suivants du lot, F15/ADR-filles)** :
- `MandatSequence` + numérotation BT-1 par mandant → **MND05** (ADR-0025) ;
- port `ISelfBilledGate` + branchement pipeline → **MND03** ; bascule tacite (`TenantJobRunner`) → **MND04** ;
- re-clé anti-doublon F06 → **MND06** ; projection BT-3=389 vers la PA → **MND07** ;
- les écrans console (registre, édition) — le squelette `Web/MandatsEndpointMapping.cs` est posé, sans route.

Aucune **décision fiscale** n'est prise ici : statuts d'assujettissement admis, valeur du délai, avoir 261
restent **NON TRANCHÉS** (F15 §6) — un item qui les rencontre **bloque**, il ne devine pas.

## Boundaries

- **Schéma owné** : `mandats` (PostgreSQL, base **par tenant**, database-per-tenant ADR-0011).
  - `mandants` : registre des mandants (référence, raison sociale, n° TVA BT-31 nullable, SIREN, préfixe).
  - `mandats` : mandats (clause, écrit/tacite, statut, délai, validation, révocation).
  - `mandat_change_log` : journal **append-only** (registre + cycle de vie), immuable par double trigger.
  - `self_billed_acceptances` (MND02) : état d'acceptation par document (clé `(company_id, document_id)`, état mutable).
  - `self_billed_acceptance_log` (MND02) : journal **append-only** des transitions d'acceptation, immuable par double trigger.
- **Lit / écrit** : uniquement son propre schéma, toujours scopé par `company_id` (CLAUDE.md n°9, INV-MANDATS-1).
- **Interdits** (module-rules §2) : montant sur le mandat, règle fiscale inventée, donnée client embarquée,
  lecture cross-tenant, mandant traité en sous-tenant, chemin d'update/delete sur le journal.
- **Surface publique** : `Contracts/` uniquement (`IMandatsQueries`, `ISelfBilledAcceptanceQueries`,
  `ISelfBilledGate` (MND03, verdict `SelfBilledGateDecision`), DTOs
  `MandantDto`/`MandatDto`/`MandatChangeLogEntryDto`/`SelfBilledAcceptanceDto`/`SelfBilledAcceptanceLogEntryDto`).
  Les abstractions d'unité de travail d'écriture (`IMandatsUnitOfWork`, `ISelfBilledAcceptanceUnitOfWork`)
  vivent dans `Application` ; les implémentations Postgres sont **internes** au module.
- **Web** : `Web/MandatsEndpointMapping.cs` — squelette de montage (aucune route en MND01).

## Published Events

Aucun.

## Consumed Events

Aucun.

## Dependencies

- `Stratum.Common.Abstractions` (`ConflictException`).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `TransactionScope`, `MigrationAssembliesOptions`,
  Dapper/Npgsql/DbUp).
- Aucune dépendance sur un autre module métier (frontière NetArchTest, INV-MANDATS-2).
