# ADR-0024 — Workflow d'acceptation de l'autofacturation : agrégat `SelfBilledAcceptance` distinct + port `ISelfBilledGate` (ne pas étendre la machine d'émission)

- **Statut** : Proposé (2026-06-15).
- **Date** : 2026-06-15
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Mandats` non démarré, **aucun code**).
  Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**, pas l'état du
  code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR est une
  **ADR-fille d'ADR-0022 §6 point (a)** : il tranche une **frontière de comportement** (où vit le workflow d'acceptation,
  comment il bloque l'émission) ; il ne tranche **aucun point fiscal** — la **valeur** du délai de contestation et
  l'articulation de l'avoir restent **NON TRANCHÉES dans F15 §6.4/§6.5** (décision expert-comptable).
- **Numérotation** : ADR-**0024**. ADR-**0023** est **réservé à la lib PDF/A-3 du lot Factur-X (F16)** — travail en
  cours ; les ADR-filles `Mandats` partent donc de 0024 (gravé en F15 §7). 0025 = allocation BT-1 hybride + re-clé
  F06 §4 ; 0026 = `IPaInboundClient` (capacité B).
- **Contexte décisionnel** : `docs/conception/F15-Autofacturation-Mandat.md` §1.5 (acceptation tacite vs écrit,
  sourcé BOI-TVA-DECLA-30-20-10 du 13/08/2021), §2.3 (workflow), §2.4 (garde-fous), §6.4/§6.5 (NON TRANCHÉ) ;
  `docs/adr/ADR-0022-mandant-tiers-premiere-classe-module-mandats.md` §6 point (a) (cette ADR-fille) ;
  `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (SOL06 : `ITenantJobRunner`/`ITenantScopeFactory`) ;
  patterns réels `src/Modules/Documents/**` (machine `DocumentState`, état `Blocked` existant, `DocumentEvent`
  append-only) et `src/Modules/TvaMapping/**` (UoW transactionnelle, journal append-only par trigger base).

## Amendement 2026-06-16 (lot SIG, item SIG02) — `SelfBilledAcceptance` projeté via `DocumentApproval` ; journal `self_billed_acceptance_log` → `document_approval_log`

> **Source : `docs/conception/F17-Signature-Validation-Document.md` §3/§4/§9 + `docs/adr/ADR-0028-workflow-validation-document-generique.md`.**
> Cet amendement est **textuel et normatif** (gravé par SIG02) ; le **refactor du code** self-billing (déléguer à
> `DocumentApproval`) est porté par **SIG05**.

ADR-0028 grave un workflow de validation **générique** `Liakont.Modules.DocumentApproval` dont la machine
`SelfBilledAcceptance` du présent ADR est une **PROJECTION restreinte** (pas une instanciation 1-pour-1, pas une
extension). En conséquence :

1. **La machine self-billing reste EXACTEMENT celle d'ADR-0024** : `PendingAcceptance → {Accepted, TacitlyAccepted,
   Contested}`, **4 états (1 initial + 3 terminaux)**, **sans** `ValidationInProgress` ni `Expired` ni `Rejected`.
   Elle est implémentée **via** `DocumentApproval` avec une **garde de purpose** (`SelfBilledAcceptance`) qui **exclut**
   `ValidationInProgress`/`Expired`/`Rejected` et **conserve** `Contested` (sens fiscal : avoir 261). **Pas de fusion
   de deux notions fiscales par renommage** (`Contested` ≠ `Rejected`). Correspondance : `Accepted` ≡ `Validated`,
   `TacitlyAccepted` ≡ `TacitlyValidated`.
2. **INV-ACCEPT-1, INV-ACCEPT-2, INV-ACCEPT-3, INV-ACCEPT-4 et INV-ACCEPT-6 sont INCHANGÉS.** Le test produit
   cartésien d'INV-ACCEPT-4 est **re-prouvé sur le purpose** `SelfBilledAcceptance` (4 états, aucun retour arrière).
3. **INV-ACCEPT-5 est AMENDÉ** : le journal du purpose self-billing devient **`document_approval_log`** (mêmes
   garanties : append-only en transaction, **double trigger base** `BEFORE UPDATE OR DELETE` + `BEFORE TRUNCATE`,
   tenant-scopé `company_id NOT NULL`), qui **REMPLACE** `self_billed_acceptance_log` — **pas de double
   journalisation**.
4. ⚠️ **Cet amendement couvre TOUTES les mentions de `self_billed_acceptance_log` dans le présent ADR** (§6
   « Décision », « À la charge du(des) lot(s) d'implémentation » des Conséquences, **et** INV-ACCEPT-5), chacune
   marquée d'un encart **« Amendé 2026-06-16 (SIG02) »** ci-dessous. Sinon un lot d'implémentation créerait la
   migration de l'**ancien** journal d'après une section non amendée (faux-vert F17 §4).
5. **Migration (à la charge de SIG05) :** reprise/abandon de `self_billed_acceptance_log` **selon l'état de MND au
   moment du refactor** — si MND02 a déjà livré la table `self_billed_acceptance_log`, **migration de bascule** vers
   `document_approval_log` **sans perte d'historique** ; sinon (MND02 écrit directement `document_approval_log`),
   aucune table héritée à migrer. **Pas de double journalisation** dans les deux cas.

Le port `ISelfBilledGate` (Mandats.Contracts) et le branchement pipeline **restent la frontière** (inchangés) ;
en interne, `Mandats` implémentera `ISelfBilledGate` en **déléguant à `DocumentApproval` par ses Contracts**
(NetArchTest). La **Règle de gate** (3 conditions) est gravée en ADR-0028 §5 (cohérente avec le présent ADR : la
condition « acceptation expresse » ne s'applique qu'à une transition `Accepted`/`Validated` **expresse**, jamais à
`TacitlyAccepted`/`TacitlyValidated`).

## Contexte

L'art. 289 I-2 CGI subordonne l'émission au nom et pour le compte du vendeur à **l'acceptation de la facture par
l'assujetti**. Le BOFiP BOI-TVA-DECLA-30-20-10 (F15 §1.5) distingue deux régimes :

- **mandat tacite** → **chaque** facture exige une **acceptation formelle et expresse** du mandant ;
- **mandat écrit et préalable** → pas d'authentification facture par facture **(§300)** ; l'acceptation peut
  résulter de la **non-contestation** au-delà d'un **délai stipulé au contrat de mandat** (§390).

Il faut donc un état d'**acceptation par document** qui **conditionne l'émission** : tant qu'un 389 n'est pas
accepté (expressément, ou tacitement quand le mandat l'autorise), il **ne doit pas partir** vers la PA. La
question tranchée ici : **où vit cet état, et comment bloque-t-il l'émission sans corrompre la machine à états du
document ni la piste d'audit ?**

## Décision

### 1. Le workflow d'acceptation est un **agrégat distinct `SelfBilledAcceptance`**, pas un état de `DocumentState`

L'acceptation est un cycle **orthogonal** à l'émission (elle peut être en attente, basculer tacitement par un job,
être contestée, indépendamment de l'avancement technique du document). On **n'étend pas** la machine d'émission
`DocumentState` (module `Documents`) avec des états « en attente d'acceptation / contesté » : cela mêlerait deux
responsabilités et fragiliserait une machine déjà éprouvée. `SelfBilledAcceptance` vit dans le module `Mandats`
(schéma `mandats`, tenant-scopé, ADR-0022), clé `(company_id, document_id)`.

### 2. Machine d'état **fermée** `PendingAcceptance → {Accepted, TacitlyAccepted, Contested}`

- `PendingAcceptance` : état initial à la création du document self-billed.
- `Accepted` : acceptation **expresse** enregistrée (opérateur/mandant).
- `TacitlyAccepted` : bascule **automatique** (job) au-delà du délai, **uniquement** sous mandat écrit (voir §4).
- `Contested` : contestation enregistrée dans le délai → la correction se fait par **avoir + nouvelle facture**
  (§5), **jamais** par un retour arrière d'état.

La liste est **fermée** (aucune transition hors de ce graphe ; test produit cartésien des transitions). États
terminaux du point de vue de l'émission : `Accepted` et `TacitlyAccepted` ouvrent le gate ; `PendingAcceptance` et
`Contested` le ferment.

### 3. Couplage à l'émission par un **port `ISelfBilledGate`** interrogé par le pipeline **avant l'envoi**

Le pipeline interroge `ISelfBilledGate` (exposé par les `Contracts` du module `Mandats`) **avant la transition vers
l'envoi** : si le document est self-billed et que son `SelfBilledAcceptance` n'est pas dans `{Accepted,
TacitlyAccepted}`, l'émission est **bloquée**. Le document est **maintenu dans l'état `Blocked` existant** de
`DocumentState` (nouveau **motif** de blocage, **aucun nouvel état** ajouté à la machine d'émission). Le gate est
un **port** (inversion de dépendance) : `Documents`/`Pipeline` ne dépendent que de l'abstraction, jamais du module
`Mandats` concret (frontière NetArchTest, CLAUDE.md n°6).

### 4. Bascule tacite **pilotée par `TenantJobRunner` (SOL06)**, conditionnée au mandat écrit

La bascule `PendingAcceptance → TacitlyAccepted` est effectuée par un **job multi-tenant** réutilisant
`TenantJobRunner` (ADR-0006/0016), **jamais** une boucle maison ni un timer ad hoc. Le job ne bascule un document
que si **`Mandat.EstEcrit = true` ET `Mandat.ContestationDelay` non null**, au-delà de
`DeadlineUtc = PendingSince + ContestationDelay`. Si `EstEcrit = false` (mandat tacite) **ou** `ContestationDelay`
null, la bascule tacite est **impossible** : seule une acceptation **expresse** débloque le document (sinon il
reste `Blocked`). C'est la traduction directe de F15 §1.5 / BOFiP §290-§390.

### 5. La correction d'un document **`Contested`** = **avoir (261) + nouvelle facture**, jamais un retour arrière

Conformément à F15 §2.3, une auto-facture contestée n'est **pas** « annulée » par un retour d'état : elle est
corrigée par un **avoir auto-facturé (BT-3 = 261**, code sourcé en F15 §6.5/§1.8**)** suivi d'une nouvelle facture.
L'agrégat `SelfBilledAcceptance` du document initial reste `Contested` (trace immuable). ❓ **NON TRANCHÉ (F15
§6.5, fiscal) :** si l'avoir 261 de correction ré-entre lui-même dans un cycle d'acceptation, et son articulation
vs F07-F08 — cet ADR **ne le tranche pas** (bloquer plutôt qu'inventer). De même, la **numérotation et la re-clé
anti-doublon du 261** sont **reportées** : ADR-0025 borne sa décision au **389** (savoir si le 261 partage la
`MandatSequence` du mandant relève de F15 §6.5, **NON TRANCHÉ** — ne pas l'inventer).

### 6. Toute transition écrit une entrée **append-only** dans la **même transaction**

`SelfBilledAcceptance` est un **état mutable** (écrasé à chaque transition) — contrairement au registre `TvaMapping`
append-only pur. La traçabilité est assurée par le journal **`self_billed_acceptance_log`** : **chaque** transition
écrit une ligne de journal **dans la MÊME transaction** que la mutation d'état (UoW sur le moule
`PostgresTvaMappingUnitOfWork`/`TransactionScope`), journal immuable par **trigger base** (UPDATE/DELETE/TRUNCATE
rejetés). C'est l'application d'**INV-MANDATS-3** (ADR-0022).

> **Amendé 2026-06-16 (SIG02) :** le journal devient **`document_approval_log`** (mêmes garanties : append-only en
> transaction, **double trigger** `BEFORE UPDATE OR DELETE` + `BEFORE TRUNCATE`, tenant-scopé `company_id NOT NULL`),
> qui **remplace** `self_billed_acceptance_log` — **pas de double journalisation**. Voir « Amendement 2026-06-16 »
> en tête + ADR-0028 §7.

### 7. Portée : structure et comportement, **aucun code, aucune décision fiscale**

Cet ADR **n'écrit aucun code**. Il ne fixe **pas** la **valeur** du délai de contestation (donnée du contrat de
mandat, paramétrage tenant — F15 §6.4), ni l'articulation fiscale de l'avoir de correction (F15 §6.5). Il
n'introduit **aucun** nouveau mécanisme transverse (réutilisation de `DocumentState.Blocked`, `TenantJobRunner`,
UoW + trigger append-only).

## Invariants

- **INV-ACCEPT-1** — Le workflow d'acceptation est un **agrégat distinct** ; **aucun** état n'est ajouté à la
  machine `DocumentState` du module `Documents` (le blocage réutilise l'état `Blocked` existant via un motif).
- **INV-ACCEPT-2** — `ISelfBilledGate` est interrogé **avant toute émission** d'un document self-billed ; un
  document dont l'acceptation n'est pas dans `{Accepted, TacitlyAccepted}` **ne peut pas** être envoyé (test :
  un 389 `PendingAcceptance`/`Contested` → `Blocked`, jamais `Sent`).
- **INV-ACCEPT-3** — La bascule `TacitlyAccepted` n'a lieu **que si `EstEcrit = true` ET `ContestationDelay` non
  null** ET `now ≥ DeadlineUtc` ; sous mandat tacite, **seule** une acceptation expresse débloque (test cartésien
  tacite/écrit × délai null/non-null).
- **INV-ACCEPT-4** — La machine d'état est **fermée** : toute transition hors de
  `PendingAcceptance → {Accepted, TacitlyAccepted, Contested}` est rejetée (test produit cartésien) ; aucun retour
  arrière depuis un état terminal.
- **INV-ACCEPT-5** *(amendé 2026-06-16, SIG02 — journal `document_approval_log`)* — **Toute** transition d'état écrit
  une entrée **`document_approval_log`** (⟵ ex-`self_billed_acceptance_log`, **remplacé**, pas de double
  journalisation) dans la **MÊME transaction** (test « pas de transition sans ligne de journal ») ; le journal est
  immuable par **double trigger base** (UPDATE/DELETE/TRUNCATE rejetés) et **tenant-scopé** (`company_id` NOT NULL).
  Voir « Amendement 2026-06-16 » en tête + ADR-0028 §7.
- **INV-ACCEPT-6** — La bascule tacite passe **exclusivement** par `TenantJobRunner` (SOL06) ; **aucune** boucle
  ni timer maison ; l'échec d'un tenant n'affecte pas les autres (isolation `TenantJobRunner`).

## Conséquences

**Positif** : la machine d'émission `DocumentState` reste inchangée et protégée ; l'acceptation est auditable de
bout en bout (journal append-only par transition) ; le couplage par port `ISelfBilledGate` préserve la frontière
inter-modules (NetArchTest) et permet de tester le pipeline avec un gate factice ; on **réutilise intégralement**
`DocumentState.Blocked`, `TenantJobRunner` et le moule UoW/trigger append-only — **aucun mécanisme transverse
nouveau**, **aucun code `Stratum.*` vendored modifié**.

**À la charge du(des) lot(s) d'implémentation** : agrégat `SelfBilledAcceptance` + machine fermée ; port
`ISelfBilledGate` dans les `Contracts` de `Mandats` + branchement pipeline avant émission ; migration
`document_approval_log` *(amendé 2026-06-16, SIG02 — ⟵ ex-`self_billed_acceptance_log`, **remplacé** ; reprise/abandon
de l'ancienne table selon l'état de MND au refactor SIG05, sans perte d'historique ni double journalisation)* (table +
fonction `reject_*_mutation` + triggers `BEFORE UPDATE OR DELETE` et `BEFORE TRUNCATE`, gabarit `V004` TvaMapping) ;
job `SelfBilledAcceptanceTenantJob` (Trigger + FanOutHandler +
enregistrement Host, gabarit `DailyAnchoring`/`Reconciliation`) ; tests : gate bloque l'émission, cartésien
tacite/écrit × délai, « pas de transition sans ligne de journal », rejet UPDATE/DELETE/TRUNCATE, scoping
cross-tenant ≥ 2 bases.

**Limite** : cet ADR ne fixe **ni** la **valeur** du délai (F15 §6.4, EC du tenant), **ni** l'articulation fiscale
de l'avoir de correction (F15 §6.5), **ni** l'allocation du BT-1 (ADR-0025), **ni** la réception (ADR-0026).

## Alternatives rejetées

- **Étendre `DocumentState` avec des états `PendingAcceptance`/`Contested`** : mêle l'acceptation (cycle métier
  orthogonal, piloté par un job et par le mandant) avec l'émission technique ; fragilise une machine éprouvée et
  rend le test combinatoire ingérable. **Rejetée.**
- **Bloquer l'émission par un simple booléen `IsAccepted` sur le document** : perd la traçabilité des transitions
  (qui/quand/comment l'acceptation a basculé), la distinction expresse/tacite, et l'audit append-only exigé par la
  responsabilité croisée mandant/mandataire. **Rejetée.**
- **Bascule tacite par un timer/boucle dédiés** : duplique un mécanisme que `TenantJobRunner` (SOL06) fournit déjà,
  sans l'isolation par tenant ni le fan-out testés. **Rejetée.**
- **Acceptation tacite par défaut sous mandat tacite** (pour « simplifier ») : **viole** BOFiP §290 (acceptation
  expresse exigée sous mandat tacite) et CLAUDE.md n°2/3 (affaiblissement de validation). **Rejetée.**

## Références

- `docs/conception/F15-Autofacturation-Mandat.md` §1.5 (BOFiP acceptation/contestation), §2.3 (workflow), §2.4
  (garde-fous), §6.4/§6.5 (NON TRANCHÉ : valeur délai, articulation avoir)
- `docs/adr/ADR-0022-mandant-tiers-premiere-classe-module-mandats.md` §6 point (a), INV-MANDATS-1/3/4 ;
  `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (SOL06)
- Patterns réels imités : `src/Modules/Documents/**` (`DocumentState.Blocked`, `DocumentEvent` append-only) ;
  `src/Modules/TvaMapping/**` (UoW transactionnelle, journal append-only par double trigger) ; `TenantJobRunner`
- ADR-fille sœur : **ADR-0025** (allocation BT-1 hybride + re-clé F06 §4) ; **ADR-0026** (`IPaInboundClient`,
  capacité B). CGI art. 289 I-2 ; BOI-TVA-DECLA-30-20-10 (13/08/2021).
