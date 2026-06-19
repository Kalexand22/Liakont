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

**Périmètre de l'item MND02 (workflow d'acceptation — ADR-0024, F15 §2.3 ; machine déléguée à
DocumentApproval depuis SIG05 / ADR-0024 amendé par ADR-0028)** :
- la **machine fermée** `PendingAcceptance → {Accepted, TacitlyAccepted, Contested}` (aucun retour arrière,
  INV-ACCEPT-4) est désormais portée par le module générique **DocumentApproval** (purpose
  `SelfBilledAcceptance`) ; `mandats.self_billed_acceptances` est réduit à la **companion fiscale** (clé
  `(company_id, document_id)`, colonnes : `allocated_number` BT-1/MND05 + `pending_since`) ;
- le **journal append-only** est `documentapproval.document_approval_log` (INV-ACCEPT-5 amendé per ADR-0024 /
  F17 §4) : CHAQUE transition (création incluse) écrit une ligne dans la MÊME transaction DocumentApproval,
  immuable par double trigger base ;
- le cycle de vie est piloté via `ISelfBilledAcceptanceCommands` (port Mandats) qui délègue à
  `DocumentApproval.Contracts` — aucune logique dupliquée ;
- l'état calculé **« gate ouvert »** (`IsAccepted` = Accepted ou TacitlyAccepted) est projeté depuis l'état
  DocumentApproval vers le vocabulaire fiscal (`SelfBilledAcceptanceStateMap`).
- Hors MND02 : la **garde** d'émission (port `ISelfBilledGate` → MND03), la **bascule tacite** par job
  (MND04), l'**allocation** du BT-1 fiscal (MND05), et l'**avoir 261** d'un `Contested` (NON TRANCHÉ F15 §6.5).

**Périmètre de l'item MND03 (garde d'émission — ADR-0024 §3, F15 §2.3, INV-ACCEPT-2)** :
- le **port `ISelfBilledGate`** (Contracts) + son implémentation `SelfBilledGate` (lit `IsAccepted` via
  `ISelfBilledAcceptanceQueries`, **fail-closed** si aucune acceptation) — par **inversion de dépendance** ;
- le **branchement pipeline** : `DocumentCheckEvaluator` (source UNIQUE de la décision de blocage : CHECK +
  recheck + réconciliation des avoirs) interroge le gate pour un pivot `IsSelfBilled` et **maintient `Blocked`**
  (nouveau motif, **aucun** nouvel état `DocumentState`) un document dont l'acceptation n'est pas acquise ;
  un recheck post-acceptation rouvre le gate (Blocked → ReadyToSend). Couverture : CHECK initial testé par
  `DocumentReceivedConsumerTests.SelfBilled_*` ; recheck testé par `DocumentRecheckServiceTests.SelfBilled_*`
  (gate ouvert → ReadyToSend, gate fermé → stays Blocked) ; la réconciliation des avoirs partage la même
  source de décision mais n'est pas couverte séparément par un test unitaire du cas self-billed. Ce dernier
  chemin est volontairement NON exercé : un avoir auto-facturé (261) y serait évalué contre une acceptation
  portée par son PROPRE `document_id` (inexistante — l'acceptation est celle de la facture d'origine) ⇒
  blocage fail-closed permanent. Or le passage d'un avoir 261 par `ISelfBilledGate` est **NON TRANCHÉ**
  (F15 §6.5 item 5) ; le blocage est la direction SÛRE (CLAUDE.md n°2/3), mais asserter un comportement par
  un test reviendrait à trancher une règle fiscale non décidée — à câbler/tester lors de l'arbitrage F15 §6.5.
- Hors MND03 : la création de l'enregistrement `SelfBilledAcceptance` à la détection d'un document self-billed
  n'est câblée par aucun item du lot à ce stade (le gate « interroge » = lecture seule, fail-closed) — à
  rattacher au flux de bout en bout avant la recette `GATE_AUTOFACTURATION`.

**Périmètre de l'item MND05 (allocation hybride du BT-1 fiscal — ADR-0025, F15 §3)** :
- la **séquence par mandant** `MandatSequence` (clé `(company_id, mandant_id)`, `Prefix` figé seedé depuis le
  mandant, `NextValue` en **`bigint`** — jamais float) et son **allocateur** `ISelfBilledNumberAllocator` ;
- l'allocation **GET-OR-CREATE idempotente sur la clé source** (`source_reference`, table mémoire
  `mandat_number_allocations` immuable) : une ré-extraction relit le même BT-1, jamais ré-alloué (INV-BT1-2) ;
- le **verrou de séquence par mandant** (`FOR UPDATE`) : allocations concurrentes d'un même mandant sérialisées,
  sans doublon ni trou (INV-BT1-4), au plus tard avant l'envoi, après acceptation ;
- l'**assignation** du BT-1 fiscal sur l'acceptation (`self_billed_acceptances.allocated_number`), **HORS du
  payload hashé** — `CanonicalJson` n'est pas modifié, `Number` reste l'identifiant source (INV-BT1-1, ADR-0007 préservé) ;
- fail-closed : source vide, mandant inconnu ou acceptation absente ⇒ rejet (CLAUDE.md n°3).
- Hors MND05 : la re-clé anti-doublon F06 + index d'unicité 389 (**MND06**) ; la projection BT-3=389 vers la PA
  (**MND07**) ; la résolution du mandant à partir du document (portée par l'appelant pipeline, MND07).

**Hors périmètre de MND01/MND02 (items suivants du lot, F15/ADR-filles)** :
- `MandatSequence` + numérotation BT-1 par mandant → **MND05** (ADR-0025, livré ci-dessus) ;
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
  - `self_billed_acceptances` (MND02 / SIG05) : **companion fiscale** seulement (`allocated_number` BT-1 + `pending_since`) ; l'état et le journal ont été relocalisés dans DocumentApproval par la migration V010.
  - `mandat_sequences` (MND05) : séquence de numérotation BT-1 par mandant (clé `(company_id, mandant_id)`, `next_value` bigint, **mutable** sous verrou `FOR UPDATE`).
  - `mandat_number_allocations` (MND05) : mémoire d'idempotence `source_reference → BT-1 fiscal`, **immuable** par double trigger (un numéro alloué ne change jamais).
- **Lit / écrit** : uniquement son propre schéma, toujours scopé par `company_id` (CLAUDE.md n°9, INV-MANDATS-1).
- **Interdits** (module-rules §2) : montant sur le mandat, règle fiscale inventée, donnée client embarquée,
  lecture cross-tenant, mandant traité en sous-tenant, chemin d'update/delete sur le journal.
- **Surface publique** : `Contracts/` uniquement (`IMandatsQueries`, `ISelfBilledAcceptanceQueries`,
  `ISelfBilledGate` (MND03, verdict `SelfBilledGateDecision`), `ISelfBilledNumberAllocator` (MND05), DTOs
  `MandantDto`/`MandatDto`/`MandatChangeLogEntryDto`/`SelfBilledAcceptanceDto`/`SelfBilledAcceptanceLogEntryDto`).
  L'abstraction d'unité de travail d'écriture (`IMandatsUnitOfWork`) vit dans `Application` ; son implémentation
  Postgres est **interne** au module. Depuis SIG05, `ISelfBilledAcceptanceUnitOfWork` est supprimée : les
  écritures d'acceptation passent par `IDocumentApprovalWorkflow` (DocumentApproval.Contracts).
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
