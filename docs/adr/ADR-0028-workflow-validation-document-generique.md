# ADR-0028 — Workflow de validation de document générique et réutilisable : module `Liakont.Modules.DocumentApproval` (machine fermée par purpose ; `SelfBilledAcceptance` en est une projection restreinte)

- **Statut** : Proposé (2026-06-16).
- **Date** : 2026-06-16
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Liakont.Modules.DocumentApproval` non
  démarré, **aucun code**). Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la
  **cible**, pas l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test.
  Cet ADR est une **ADR-fille d'ADR-0022** et une **sœur d'ADR-0024/0025/0026/0027** : il tranche une **frontière de
  comportement** (un workflow de validation générique, dont le couplage à l'émission passe par des ports) ; il ne
  tranche **aucun point fiscal**. La **machine d'acceptation self-billing d'ADR-0024 reste la source de vérité** et
  **inchangée** — le générique se **dérive d'elle**, pas l'inverse (§4).
- **Numérotation** : ADR-**0028**. Plan d'ADR-filles du lot signature (F17 §9) : 0027 (abstraction
  `ISignatureProvider`), **0028** (ce module générique), 0029 (Yousign), 0030 (Wacom). L'**amendement formel
  d'ADR-0024** (journal `self_billed_acceptance_log` → `document_approval_log`, F15 §1.9) et les ADR de plug-in sont
  gravés séparément (lot SIG, item **SIG02**) ; le **refactor** de `SelfBilledAcceptance` pour déléguer à
  `DocumentApproval` est porté par **SIG05**. Cet ADR pose la **conception cible** ; il ne réécrit pas encore le
  texte d'ADR-0024 (SIG02) ni le code MND déjà livré (SIG05).
- **Contexte décisionnel** : `docs/conception/F17-Signature-Validation-Document.md` §3 (workflow générique), §4
  (intégration self-billing, machine inchangée), §10 (points ouverts), §11 (garde-fous P1) ;
  `docs/adr/ADR-0024-workflow-acceptation-self-billed-gate.md` (machine `SelfBilledAcceptance`, port `ISelfBilledGate`,
  INV-ACCEPT-1..6) ; `docs/adr/ADR-0027-abstraction-signature-capacites.md` (l'abstraction consommée) ;
  `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (SOL06 : `ITenantJobRunner`) ; patrons réels :
  `src/Modules/Mandats/**` (agrégat mutable + journal `self_billed_acceptance_log` append-only par double trigger,
  UoW transactionnelle) ; `src/Modules/TvaMapping/**` (UoW + trigger) ; `src/Modules/Documents/**`
  (`DocumentState.Blocked`) ; `src/Modules/Archive/Contracts/` (`IArchiveService`).

## Contexte

Karl demande un **workflow de validation de document RÉUTILISABLE** : un même mécanisme doit servir la signature du
**contrat de mandat**, l'acceptation de l'**avoir 261**, l'approbation d'un **relevé d'avancement** de chantier, un
**circuit comptable multi-paliers**, et un **document co-signé N parties** — tout en **réutilisant** la machine
d'acceptation d'auto-facture déjà conçue (ADR-0024) **sans la corrompre**.

Le module **`Liakont.Modules.Validation` existe déjà** (règles métier EN 16931). Nommer le nouveau module
`Validation.Workflow` l'imbriquerait sous le préfixe racine `Liakont.Modules.Validation.*` (risque : une règle
NetArchTest ou un filtre DI ciblant `Liakont.Modules.Validation*` attraperait le mauvais module). **Décision :
module racine `Liakont.Modules.DocumentApproval`** (non collisionnant).

Le piège central : la machine d'ADR-0024 est **fermée** (`PendingAcceptance → {Accepted, TacitlyAccepted,
Contested}`, 1 initial + 3 terminaux), interdit tout retour arrière (INV-ACCEPT-4) et donne à `Contested` un **sens
fiscal précis** (contestation dans le délai → avoir 261). Un workflow générique ne doit **ni** étendre cette machine
gelée, **ni** fusionner `Contested` avec un état de rejet de signature.

## Décision

### 1. Module racine `Liakont.Modules.DocumentApproval` (pattern Stratum)

`Contracts/Domain/Application/Infrastructure/Web` + `MODULE.md`/`INVARIANTS.md`. Nom **non collisionnant** avec le
module `Validation` existant (tranché ici, avant gravure du code en SIG04).

### 2. Agrégat `DocumentValidation`, clé `(company_id, document_id, validation_purpose, attempt)`

État **mutable** (écrasé à chaque transition, comme `SelfBilledAcceptance`), tenant-scopé. L'`attempt` (entier ≥ 1)
autorise une **nouvelle tentative** après un échec terminal sans muter l'historique (voir §6). Pour le purpose
self-billing, `attempt` reste **1** (pas de ré-essai — §4/§6).

### 3. Machine **FERMÉE** à **7 états DISTINCTS**, à arêtes directes `PendingValidation` → terminal

```
PendingValidation ─┬─▶ Validated             (complétion synchrone : Recorded, ou preuve rattachée en direct)
                   ├─▶ TacitlyValidated      (bascule TenantJobRunner)
                   ├─▶ Rejected / Contested  (DEUX états DISTINCTS — voir légende)
                   ├─▶ Expired               (purposes signature uniquement)
                   └─▶ ValidationInProgress  (OPTIONNEL ; purposes signature ASYNCHRONES uniquement)
ValidationInProgress ─┬─▶ Validated / TacitlyValidated   (complétion / bascule)
                      └─▶ Rejected / Expired             (refus / délai)
```

**7 états distincts** : `PendingValidation`, `ValidationInProgress` (optionnel, purposes async), `Validated`,
`TacitlyValidated`, **`Rejected`** (terminal de refus des purposes signature), **`Contested`** (terminal de refus du
self-billing, sens fiscal : avoir 261), `Expired`. ⚠️ `Rejected` et `Contested` sont **deux états séparés**,
**pas** un renommage ; **aucun purpose n'utilise les deux**.

- `PendingValidation` est l'état initial et a des **arêtes DIRECTES vers les terminaux** : c'est le chemin du
  `Recorded` (acceptation enregistrée, sans demande externe) et de toute complétion synchrone. `ValidationInProgress`
  est un **intermédiaire OPTIONNEL, jamais un passage obligé** (sinon un document `Recorded`/self-billing
  n'atteindrait jamais un état ouvrant le gate).
- `ValidationInProgress` n'est atteint **que quand la demande est réellement EN COURS** — déterminé par le
  **RÉSULTAT** de `RequestSignatureAsync` (`Pending` vs `Completed`), **jamais** par une égalité d'enum de
  `CompletionTransport` (un `[Flags]` combinable, ADR-0027). **Pas de retour à `PendingValidation`.**
- `Validated` : validation **expresse** (`Recorded` OU preuve SES/AES/QES rattachée). **N'ouvre PAS le gate
  inconditionnellement** (Règle de gate §5).
- `TacitlyValidated` : bascule par `TenantJobRunner` (SOL06) au-delà de `DeadlineUtc`, **seulement si la politique
  du purpose l'autorise** ; rend le gate **éligible sous réserve de la Règle de gate §5**.
- `Rejected` / `Contested` / `Expired` : **ferment le gate, terminaux** ; correction = document compensatoire,
  **jamais** de retour arrière.

La liste est **fermée** (test produit cartésien des transitions, aucune transition hors graphe).

### 4. `validation_purpose` déclare un **SOUS-GRAPHE AUTORISÉ explicite** ; le self-billing est une projection restreinte

`enum ValidationPurpose` fixe pour chaque purpose : (a) le **sous-graphe autorisé** (garde de purpose testée),
(b) le **port de couplage**, (c) la **politique de bascule tacite**. Le graphe de §3 est l'**union** de tous les
purposes ; **aucun purpose n'accède à toutes les arêtes**.

| Purpose | Port de couplage | Bascule tacite | Niveau eIDAS |
|---|---|---|---|
| `SelfBilledAcceptance` (ADR-0024) | `ISelfBilledGate` (`Mandats.Contracts`, consommé par `Pipeline`) | mandat écrit + délai (INV-ACCEPT-3) | tenant (défaut `Recorded`) |
| `MandateSignature` (contrat de mandat, ADR-0022) | `IMandateSignatureGate` | aucune (signature expresse) | ❓ tenant (reco AES) |
| `CreditNoteAcceptance` (avoir 261) | `ICreditNoteAcceptanceGate` (`Mandats.Contracts`, consommé par `Pipeline`) | ❓ owner EC | ❓ tenant |
| `ProgressStatementApproval` (relevé chantier) | `IReleasableDocumentGate` | selon contrat | ❓ tenant |
| `MultiTierAccountingApproval` (multi-paliers) | `IMultiTierApprovalGate` | selon politique | s.o. |
| `MultiPartySignature` (co-signé N parties) | `IMultiPartySignatureGate` | timeout `TenantJobRunner` | ❓ tenant |

**Sous-graphe `SelfBilledAcceptance` = `{PendingValidation, Validated, TacitlyValidated, Contested}`** (= machine
ADR-0024 EXACTE : 1 initial + 3 terminaux). `ValidationInProgress`/`Expired`/`Rejected` sont **HORS** de ce
sous-graphe (toute transition vers eux pour ce purpose est **rejetée** par la garde de purpose, testée). Le
vocabulaire fiscal d'ADR-0024 est **conservé** : `Validated` ≡ `Accepted`, `TacitlyValidated` ≡ `TacitlyAccepted`,
**`Rejected` n'est PAS utilisé** ; le purpose garde **`Contested`** (sens fiscal). **Pas de fusion de deux notions
fiscales par renommage.** C'est ce sous-graphe (et non « le graphe est inchangé ») qui garantit INV-ACCEPT-4.

> **`DocumentApproval` est une GÉNÉRALISATION dont `SelfBilledAcceptance` est une PROJECTION restreinte**, pas une
> instanciation 1-pour-1. Conséquence sur ADR-0024 : **INV-ACCEPT-1..4 et 6 inchangés** ; **INV-ACCEPT-5 est
> AMENDÉ** — le journal du purpose self-billing devient **`document_approval_log`** (mêmes garanties : append-only
> en transaction, double trigger base, tenant-scopé `company_id NOT NULL`), qui **remplace** `self_billed_acceptance_log`
> (**pas de double journalisation**). ⚠️ Cet amendement doit couvrir **TOUTES** les mentions de
> `self_billed_acceptance_log` dans ADR-0024 (§6 « Décision » + « À la charge des lots d'implémentation » +
> INV-ACCEPT-5) — sinon un lot créerait la migration de l'ancien journal (faux-vert). **La gravure formelle de cet
> amendement est portée par SIG02 ; le refactor du code self-billing (déléguer à `DocumentApproval`) par SIG05.**
> Le test produit cartésien d'INV-ACCEPT-4 est **re-prouvé sur le purpose** (4 états, aucun retour arrière).

### 5. RÈGLE DE GATE (définitive)

Un gate de purpose (dont `ISelfBilledGate`) **ouvre** pour un document **ssi**, sur sa **tentative la plus récente**
(`attempt` max, voir §6), les **trois** conditions sont réunies :

1. l'état ∈ `{Validated, TacitlyValidated}` — condition **NÉCESSAIRE, non suffisante** ; **ET**
2. la preuve attachée satisfait le **niveau requis CONFIGURÉ par le tenant** pour ce purpose : `Recorded` (défaut)
   n'exige aucune preuve ; si le tenant exige `SES`/`AES`/`QES`, un `SignatureProof.Level` **≥ niveau requis** doit
   être attaché — **un `Recorded` nu NE satisfait PAS une exigence AES/QES** (sinon l'exigence serait contournable),
   et une bascule `TacitlyValidated` (sans preuve) ne satisfait que `Recorded` ; **ET**
3. **pour le purpose self-billing, et UNIQUEMENT sur une transition `Validated` EXPRESSE** : une **acceptation
   enregistrée EXPLICITE** selon la **modalité configurée par le tenant** (défaut : geste opérateur/mandant tracé
   append-only — F17 §1.1). ⚠️ **La condition 3 NE s'applique PAS à `TacitlyValidated`** : l'acceptation **tacite**
   est régie par ses propres gardes (INV-ACCEPT-3 : `EstEcrit = true` ET `ContestationDelay` non null ET délai
   écoulé), jamais par la forme expresse — lui appliquer la condition 3 **bloquerait à tort** une tacite valide
   (contradiction ADR-0024).

Le durcissement du point 2 vient du **CHOIX du tenant** (paramétrage), **jamais** d'une obligation légale produit
(F17 §1.1). **POINT NON NÉGOCIABLE (CLAUDE.md n°2/3) :** la signature est le **moyen** d'atteindre `Validated` quand
le tenant l'active ; **elle ne durcit JAMAIS le gate au nom de la LOI** et ne l'**affaiblit** jamais. Un document non
validé reste `Blocked`, **même « signé »** ; un tenant en `Recorded` n'est **jamais** bloqué du seul fait de
l'absence de fournisseur de signature.

### 6. Ré-essai par nouvel `attempt` (purposes signature) ; self-billing EXCLU

`Expired`/`Rejected` étant **terminaux et immuables**, on ne « rouvre » jamais une tentative : une nouvelle demande
crée un **nouvel `attempt`** (nouvelle ligne `DocumentValidation`, historique précédent intact — append-only).

- **Invariant : au plus UNE tentative NON terminale à la fois** par `(document_id, purpose)` — **index unique
  partiel `(company_id, document_id, validation_purpose)` filtré sur les états NON terminaux** (tenant-scopé comme la
  clé d'agrégat ; gouverne la **création**).
- **Le gate lit la tentative la PLUS RÉCENTE (`attempt` max), indépendamment de sa terminalité** ; que son état
  soit `Validated`/`TacitlyValidated` est une condition **nécessaire non suffisante** (Règle de gate §5). « Lire la
  tentative active » serait faux : après un succès terminal, aucune tentative non terminale n'existe.
- ⚠️ **Garde anti-race** : la création de l'`attempt` N+1 est conditionnée, **dans la même transaction**, à ce que
  l'`attempt` N soit un **échec terminal** (`Expired`/`Rejected`). Sinon (N `Pending` complété en `Validated` par un
  webhook concurrent au moment où l'on crée N+1) un N+1 non terminal **masquerait le succès N et refermerait le gate
  à tort**. **Test de concurrence requis.**
- ⚠️ **Le purpose self-billing est EXCLU du ré-essai** : `Contested` est **définitif** (ADR-0024) — une auto-facture
  contestée n'est **jamais** rouverte par un nouvel `attempt`, la correction passe par **avoir 261 + nouvelle
  facture (nouveau `document_id`)**. Le self-billing **n'a pas d'`Expired`** (sous-graphe 4 états).

### 7. Journal append-only `document_approval_log` (mutation + journal dans la MÊME transaction)

`DocumentValidation` étant un état **mutable**, la traçabilité est assurée par **`document_approval_log`** :
**chaque** transition (création incluse) écrit une ligne **dans la MÊME transaction** que la mutation (UoW sur le
moule `PostgresTvaMappingUnitOfWork` / `self_billed_acceptance_log`), journal immuable par **double trigger base**
(`BEFORE UPDATE OR DELETE` + `BEFORE TRUNCATE`, UPDATE/DELETE/TRUNCATE rejetés), **tenant-scopé** (`company_id NOT
NULL`).

> **Réconciliation webhook tardive vs append-only / terminalité.** Si `GetSignatureStatusAsync` ou un webhook arrive
> **après** un état terminal : (1) **idempotence par `event_id`** → un événement déjà traité est **ignoré** (pas de
> nouvelle ligne) ; (2) un événement qui **tenterait** une transition interdite depuis un terminal est **rejeté** par
> la machine fermée et **journalisé comme tentative rejetée** (la ligne enregistre la tentative, **sans** muter
> l'état terminal). Ainsi « pas de transition sans ligne de journal » ET « pas de retour arrière » coexistent.

### 8. Agrégation N-parties / multi-paliers par **SLOTS identifiés idempotents** (jamais un compteur)

Pour `MultiPartySignature` / `MultiTierAccountingApproval`, `Validated` n'est atteint **qu'à complétude**. ⚠️ **PAS
un compteur `ReceivedApprovals++`** (un webhook rejoué ou une double approbation d'un même signataire ouvrirait le
gate prématurément). On modélise un **ensemble FIXE de slots identifiés** (`ApprovalSlot { SignerId/TierId, State,
ProofId? }`, défini **à la création**), chaque slot **idempotent par `SignerId`** (une 2ᵉ preuve du même signataire
**ne remplit rien de plus**). **Complétude = tous les slots DISTINCTS remplis**, jamais un total d'événements.

- **Niveau de preuve évalué PAR SLOT** : la condition 2 de la Règle de gate (§5) s'évalue **par slot** (« **tous**
  les slots ≥ niveau requis »), jamais sur un niveau agrégé (sinon un slot sous-niveau passerait = faux-vert). Test
  dédié.
- **Terminaison négative — décision PRISE par cet ADR (F17 §3.3 délègue la règle à 0028) :** un slot **refusé**
  bascule l'agrégat **immédiatement** en `Rejected` (`Contested` pour un purpose à sens fiscal), **sans attendre le
  timeout des autres slots**. Justification : un document co-signé dont **une** partie refuse ne peut **jamais**
  atteindre la complétude ; attendre le timeout ne ferait que retarder l'inévitable et créerait une ambiguïté
  `Expired` vs `Rejected`. Le `TenantJobRunner` reste responsable de la bascule `Expired` des slots **en attente**
  (timeout). Ceci est une **décision d'architecture de machine à états** (aucun contenu fiscal/juridique), à
  **prouver par test** (slot refusé → `Rejected` immédiat ; les slots restants n'ouvrent jamais le gate).

### 9. Frontières inter-modules (chaîne NetArchTest sur les 5 purposes + arête WORM)

```
Documents/Pipeline ──▶ ISelfBilledGate (et autres ports de purpose)   (jamais Mandats/DocumentApproval/Signature concrets)
Mandats            ──▶ DocumentApproval.Contracts                      (jamais le Domain)
DocumentApproval   ──▶ Signature.Contracts                            (jamais Yousign/Wacom concret)
Transmission       ──▶ (aucun plug-in signature)
Plug-in signature  ──▶ Signature.Contracts + Common                   (jamais un autre module ni un autre plug-in)
Job de drain WORM  ──▶ Archive.Contracts (IArchiveService)            (jamais Archive.Domain/IArchiveStore ni backend concret)
```

Assertions **NetArchTest** à écrire (`src/Modules/<module>/Tests.Unit/ArchitectureTests.cs` + un test agrégé) :

- **Règle générale = la vraie frontière** : aucun module ne dépend des couches `Domain`/`Application`/`Infrastructure`
  d'un autre module ; seuls les `*.Contracts` sont autorisés. ⚠️ **NE PAS** écrire un
  `NotHaveDependencyOn("Liakont.Modules.Mandats")` global — il rejetterait la référence **légitime** à
  `Mandats.Contracts`.
- Le **consommateur de `ISelfBilledGate` est `Liakont.Modules.Pipeline`** (pas `Documents`) : `Pipeline` peut
  dépendre de `Mandats.Contracts`, **jamais** de `Mandats.Domain`/`.Application`/`.Infrastructure`.
- `Mandats` → `NotHaveDependencyOnAny(DocumentApproval.Domain/.Application/.Infrastructure)` (Contracts seul).
- `DocumentApproval` → `NotHaveDependencyOnAny("Liakont.Modules.Signature.PlugIns.*")`.
- **Arête WORM** : le rapatriement coffre passe par `Archive.Contracts` (`IArchiveService`), **jamais**
  `Archive.Domain` (`IArchiveStore`) ni un backend concret ; il est porté par le **job de drain**, jamais par un
  plug-in. Assertion : le job `NotHaveDependencyOnAny("Liakont.Modules.Archive.Domain", "…Stores.*")`.
- La chaîne est vérifiée pour **les 5 purposes** (nommer pour chacun le module exposeur du port et le consommateur),
  pas seulement le self-billing.

### 10. Portée : structure et comportement, **aucun code, aucune décision fiscale**

Cet ADR **n'écrit aucun code** (livré à partir de SIG04). Il ne fixe **ni** la valeur d'un délai (paramétrage
tenant), **ni** un niveau eIDAS par défaut (paramétrage tenant, ADR-0027 §7), **ni** l'articulation fiscale de
l'avoir 261. Il **n'introduit aucun mécanisme transverse nouveau** (réutilisation de `DocumentState.Blocked`,
`TenantJobRunner`, UoW + double trigger append-only, `Archive.Contracts`).

## Invariants

- **INV-APPROVAL-1** — Module racine **`Liakont.Modules.DocumentApproval`** (non collisionnant avec
  `Liakont.Modules.Validation`) ; couplage à l'émission par **ports** (un par purpose), jamais une dépendance
  concrète (NetArchTest).
- **INV-APPROVAL-2** — Machine **FERMÉE** à **7 états distincts** (`Rejected` ≠ `Contested`) ; toute transition hors
  graphe est rejetée (test produit cartésien) ; **aucun retour arrière** depuis un terminal ; `PendingValidation` a
  des **arêtes directes vers les terminaux** (chemin `Recorded`/synchrone atteignable) et `ValidationInProgress` est
  un intermédiaire **optionnel**.
- **INV-APPROVAL-3** — Chaque `validation_purpose` déclare un **sous-graphe autorisé explicite** ; le sous-graphe
  **`SelfBilledAcceptance` = 4 états** (`{PendingValidation, Validated, TacitlyValidated, Contested}`, =
  ADR-0024 ; `ValidationInProgress`/`Expired`/`Rejected` rejetés par la garde de purpose — test). **Re-preuve du
  cartésien INV-ACCEPT-4 sur le purpose.**
- **INV-APPROVAL-4** — **RÈGLE DE GATE** : le gate ouvre **ssi** (1) état ∈ `{Validated, TacitlyValidated}` **ET**
  (2) niveau de preuve ≥ exigence tenant (**par slot** en N-parties ; `Recorded` nu ne franchit pas AES/QES) **ET**
  (3) pour le self-billing **sur transition `Validated` expresse uniquement**, acceptation enregistrée explicite. La
  condition 3 **ne s'applique pas** à `TacitlyValidated`. Le gate n'est **ni durci ni affaibli** au nom d'une
  obligation inexistante (test : tenant `Recorded` jamais bloqué du seul fait de l'absence de fournisseur ; gate
  self-billing reste actif même « signé »).
- **INV-APPROVAL-5** — Ré-essai par nouvel **`attempt`** réservé aux purposes **SIGNATURE** ; **index unique partiel
  `(company_id, document_id, validation_purpose)` sur les non-terminaux** (≤ 1 non terminale) ; **garde anti-race**
  à la création d'un `attempt` (l'`attempt` N doit être un échec terminal, même transaction — test de concurrence) ;
  le gate lit la tentative **la plus récente**. **Self-billing EXCLU** : `Contested` définitif, correction = avoir
  261 + nouveau `document_id`.
- **INV-APPROVAL-6** — **Toute** transition (création incluse) écrit une ligne **`document_approval_log`** dans la
  **MÊME transaction** ; journal immuable par **double trigger** (UPDATE/DELETE/TRUNCATE rejetés) et **tenant-scopé**
  (`company_id NOT NULL`). Un événement tardif depuis un terminal est **idempotent par `event_id`** (ignoré) ou
  **journalisé comme tentative rejetée** sans muter le terminal.
- **INV-APPROVAL-7** — Agrégation N-parties par **slots identifiés idempotents par `SignerId`** (jamais un
  compteur) ; complétude = **tous** les slots distincts remplis ; **niveau de preuve évalué PAR slot** ; un slot
  **refusé** → `Rejected`/`Contested` **immédiat** (§8). Tests : doublon d'un signataire n'ouvre jamais le gate ;
  slot sous-niveau n'ouvre jamais le gate ; slot refusé bascule immédiatement.
- **INV-APPROVAL-8** — Frontières (chaîne §9) prouvées par NetArchTest **sur les 5 purposes** + **arête WORM** (job
  de drain → `Archive.Contracts`, jamais `Archive.Domain`).

## Conséquences

**Positif** : un **seul** mécanisme sert 5 purposes ; la machine d'émission `DocumentState` et la machine
`SelfBilledAcceptance` d'ADR-0024 restent **inchangées et protégées** (le générique se dérive d'elles) ; le couplage
par ports préserve la frontière inter-modules (NetArchTest) et permet de tester le pipeline avec des gates factices ;
slots idempotents et garde anti-race ferment des faux-verts concrets (webhook rejoué, complétion concurrente). On
**réutilise** `DocumentState.Blocked`, `TenantJobRunner`, le moule UoW/double-trigger et `Archive.Contracts` —
**aucun mécanisme transverse nouveau, aucun code `Stratum.*` vendored modifié.**

**À la charge du(des) lot(s) d'implémentation** (SIG04 et suivants) : module `DocumentApproval`
(agrégat + machine fermée + sous-graphes par purpose + slots + `attempt` + index partiel + garde anti-race) ; ports
de purpose dans les `Contracts` des modules exposeurs ; migration `document_approval_log` (table + fonction de rejet
+ triggers `BEFORE UPDATE OR DELETE` et `BEFORE TRUNCATE`, gabarit `V004` TvaMapping) ; job de bascule tacite et de
timeout (`TenantJobRunner`) ; job de drain WORM via `Archive.Contracts` ; tests : cartésien fermé, sous-graphe
self-billing 4 états, Règle de gate (3 conditions, `Recorded` nu vs AES/QES, condition 3 hors tacite), concurrence
attempt, slots idempotents + niveau par slot + slot refusé, append-only (rejet UPDATE/DELETE/TRUNCATE), scoping
cross-tenant ≥ 2 bases, chaîne NetArchTest 5 purposes + arête WORM. **SIG02** grave l'amendement formel d'ADR-0024
(journal) ; **SIG05** refactore `SelfBilledAcceptance` pour déléguer à `DocumentApproval`.

**Limite** : cet ADR ne grave **ni** les fournisseurs concrets (ADR-0029/0030), **ni** l'amendement textuel
d'ADR-0024 (SIG02), **ni** le refactor du code self-billing (SIG05).

### Points NON TRANCHÉS (F17 §10 — défaut défendable pris, le client tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| 9 | Le 261 ré-entre-t-il dans un cycle d'acceptation (purpose `CreditNoteAcceptance`) | **défaut : oui** — le 261 est self-billed → même discipline d'acceptation que le 389 (conservateur ; aucune valeur fiscale inventée). L'**existence** du purpose `CreditNoteAcceptance` reste conditionnée à F15 §6.5 | tenant + son EC |
| 4 | « Acceptation expresse » = quelle forme (condition 3 du gate) | **acceptation enregistrée EXPLICITE** (geste opérateur/mandant tracé append-only) ; modalité paramétrable (jusqu'à l'accusé de réception horodaté) | tenant + son EC |

Aucun de ces points ne stalle le dev : ce sont des **défauts paramétrables**, pas des gates (F17 §10). La règle de
**terminaison négative N-parties** (§8) n'est **pas** un point ouvert fiscal/juridique : c'est une décision
d'architecture de machine à états **prise ici** (slot refusé → terminal immédiat), à prouver par test.

## Alternatives rejetées

- **Étendre la machine `SelfBilledAcceptance` d'ADR-0024 aux 7 états** (ou fusionner `Contested`↔`Rejected` par
  renommage et ajouter `ValidationInProgress`/`Expired` au self-billing) : **viole** la règle « machine à états »,
  INV-ACCEPT-1/4 et le sens fiscal de `Contested`. **Rejetée** — le générique se **dérive** de la machine gelée
  (projection restreinte, garde de purpose).
- **Une seule arête sortante `PendingValidation → ValidationInProgress`** (esquisse initiale) : rendrait un document
  `Recorded`/self-billing **incapable** d'atteindre un état ouvrant le gate (`ValidationInProgress` interdit au
  self-billing). **Rejetée** — arêtes directes vers les terminaux.
- **Compteur `ReceivedApprovals++` pour le multi-parties** : un webhook rejoué / double approbation ouvrirait le gate
  prématurément. **Rejetée** — slots identifiés idempotents, complétude = slots distincts.
- **Lire « la tentative active »** : faux après un succès terminal (aucune tentative non terminale n'existe).
  **Rejetée** — le gate lit la tentative **la plus récente** (`attempt` max).
- **Ré-essai par retour arrière depuis un terminal** : viole l'append-only / l'immuabilité. **Rejetée** — nouvel
  `attempt` (historique intact) ; self-billing **exclu** (`Contested` définitif).
- **Double journalisation `self_billed_acceptance_log` + `document_approval_log`** : redondance et risque de
  divergence. **Rejetée** — `document_approval_log` **remplace** l'ancien journal (amendement ADR-0024, SIG02).
- **Rapatriement WORM par le plug-in signature ou via `Archive.Domain`** : viole la frontière (CLAUDE.md n°6).
  **Rejetée** — job de drain via `Archive.Contracts`.

## Références

- `docs/conception/F17-Signature-Validation-Document.md` §3 (workflow générique), §4 (intégration self-billing,
  machine inchangée), §10 (points ouverts), §11 (garde-fous P1).
- `docs/adr/ADR-0024-workflow-acceptation-self-billed-gate.md` (machine `SelfBilledAcceptance`, `ISelfBilledGate`,
  INV-ACCEPT-1..6 ; INV-ACCEPT-5 amendé par SIG02) ; `docs/adr/ADR-0027-abstraction-signature-capacites.md`
  (`ISignatureProvider` consommé) ; `docs/adr/ADR-0022-mandant-tiers-premiere-classe-module-mandats.md` ;
  `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (SOL06).
- Patrons réels imités : `src/Modules/Mandats/**` (agrégat mutable + journal append-only par double trigger, UoW) ;
  `src/Modules/TvaMapping/**` (UoW + trigger `V004`) ; `src/Modules/Documents/**` (`DocumentState.Blocked`) ;
  `src/Modules/Archive/Contracts/` (`IArchiveService`).
- ADR-filles suivantes du lot (SIG02) : **ADR-0029** (Yousign), **ADR-0030** (Wacom). CGI art. 289 I-2 ;
  BOI-TVA-DECLA-30-20-10 (13/08/2021) ; règlement UE 910/2014 (eIDAS).
