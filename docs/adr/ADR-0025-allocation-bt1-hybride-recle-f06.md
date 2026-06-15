# ADR-0025 — Allocation hybride du BT-1 en autofacturation (389) et re-clé anti-doublon F06 §4 par mandant

- **Statut** : Proposé (2026-06-15).
- **Date** : 2026-06-15
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Mandats` non démarré, **aucun code**).
  Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**, pas l'état du
  code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR est une
  **ADR-fille d'ADR-0022 §6 point (b)** : il tranche **comment le BT-1 fiscal est alloué** en 389 et **comment la clé
  anti-doublon F06 §4 bascule** — il **amende F06 §4** (réécriture jointe). Il ne tranche **aucun point fiscal**
  (la **valeur** du préfixe/racine et le statut d'assujettissement restent paramétrage tenant ; F15 §6).
- **Numérotation** : ADR-**0025**. ADR-**0023** est réservé à la lib PDF/A-3 du lot Factur-X (F16, travail en
  cours) ; ADR-**0024** = workflow d'acceptation + `ISelfBilledGate` (ADR sœur). 0026 = `IPaInboundClient`
  (capacité B).
- **Contexte décisionnel** : `docs/conception/F15-Autofacturation-Mandat.md` §3 (allocation hybride), §1.4/§1.8
  (numérotation sourcée BOI-TVA-DECLA-30-20-20-10 du 18/10/2013 + Annexe 7 G1.42/G1.45), §6.7 (trous débloqués),
  §6.10 (re-clé F06 conditionnée) ; `docs/conception/F06-Tracking-Piste-Audit.md` §4 (**amendé par cet ADR**) ;
  `docs/adr/ADR-0007-serialisation-canonique-pivot.md` (Number **non optionnel**, **ordre de déclaration** §1/§3 ;
  Number = 2ᵉ propriété du canonique, `CanonicalJson.cs:45`) ; `docs/adr/ADR-0022-...md` §6 point (b) ; briques réelles : `SourceReference` au pivot
  (`PivotDocumentDto.cs`, écrit inconditionnellement `CanonicalJson.cs:49-50`), `PayloadHasher` (payload_hash),
  `DuplicateCheckRequest`/`DocumentDuplicatePolicy` (anti-doublon F06 §4, **sans champ `MandatId` à ce jour**).

## Contexte

Aujourd'hui le `Number` (BT-1) **vient de la source**, est obligatoire, et **ancre l'idempotence à trois niveaux** :
le `payload_hash` d'ingestion (ADR-0007), l'anti-doublon F06 `(supplier_siren, document_number)`, et l'`external_id`
côté PA (Super PDP **déduplique au numéro**). Or la loi impose en 389 une **séquence chronologique distincte par
mandant** (BOI-TVA-DECLA-30-20-20-10 §120/§130 ; Annexe 7 G1.42/G1.45 : « racine propre au mandataire », unicité
**bloquante** sur `(BT-1, BT-2, BT-30 = SIREN du mandant)`). Un legacy n'a aucune raison de produire cette séquence
(preuve : `Kerport_Fact` numérote **globalement**, `F00172473`, pas par armement). Deux exigences entrent donc en
collision : **l'idempotence interne** (qui veut une clé source stable) et **la numérotation fiscale par mandant**
(qui interdit de réutiliser le numéro brut de la source comme BT-1).

## Décision

### 1. **Dédoubler** ce que `Number` confond : clé d'idempotence interne ≠ BT-1 fiscal

- **Clé d'idempotence interne** = un **identifiant interne de la source**, **déjà présent au pivot et déjà hashé** :
  `SourceReference` (écrit inconditionnellement par `CanonicalJson`, à côté de `Number`). Le `payload_hash` reste
  **inchangé** — **aucun format canonique conditionnel au flux** (ADR-0007 préservé).
- **BT-1 fiscal 389** = **alloué par le module `Mandats`** via une **séquence par mandant** (`MandatSequence`),
  par un **`get-or-create` sur la clé source** : alloué une fois et mémorisé ; toute ré-extraction **relit le même
  numéro**, jamais ré-alloué.

### 2. **L'idempotence remplace l'atomicité**

L'allocation (schéma `mandats`) et l'écriture du document (schéma `documents`) sont **deux transactions** non
atomisables à travers la frontière de module. On ne cherche **pas** l'atomicité transverse : on rend chaque étape
**idempotente et rejouable** sur la clé source stable — exactement comme l'écriture du document l'est déjà
(`DocumentId` + `ON CONFLICT (id) DO NOTHING`). Un crash entre allocation et écriture est rattrapé par relecture
get-or-create, **sans** double allocation ni trou évitable.

### 3. ADR-0007 **préservé** : ne pas retirer `Number` du hash pour le seul 389

On **ne retire PAS** `Number` du payload canonique pour le cas 389 (ce serait un format conditionnel au flux qui
casse la reproductibilité octet-par-octet et amenderait la règle additive d'ADR-0007). En 389, `Number` **reste
toujours rempli** (par l'identifiant source interne, porté par `SourceReference`) ; le **BT-1 fiscal alloué par
mandant est une valeur SÉPARÉE, hors du payload hashé**, assignée **à l'émission** côté plateforme.

### 4. La garde d'ingestion 389 est **substituée**, pas **affaiblie**

En 389, la garde exige un **identifiant source interne NON NUL** à la place du `Number` : jamais d'acceptation d'un
389 sans clé d'idempotence. C'est une **substitution d'invariant** (CLAUDE.md n°3 : ne pas affaiblir une
validation), pas une levée de contrôle. Un 389 sans clé source → **rejet**, jamais « laisser passer ».

### 5. Numérotation : **séquence par mandant**, allouée **au plus tard avant envoi**

`MandatSequence (company_id, mandant_id, Prefix, NextValue)` — `NextValue` en **`bigint`** (jamais float ; le
mandat ne porte aucun montant). La séquence est **chronologique, continue, distincte par mandant** (§1.4) et porte
une **racine/préfixe propre** (Annexe 7 G1.42/G1.45 + BOFiP §130). Allocation **juste avant l'envoi** après
Validation/acceptation (§6.7 : l'unicité CTC ne bloque que les doublons, pas les trous → ce choix minimise les
trous). « Chronologique » et « continue » sont **deux invariants distincts** (ordre cohérent vs sans trou) ;
l'ordre n'est pas garanti sous allocation concurrente d'un même mandant → l'allocation prend un **verrou de
séquence par mandant** (jamais cross-tenant).

### 6. **Re-clé anti-doublon F06 §4** : `(tenant, mandant_id, document_number)` en 389, atomique côté SQL

La clé fonctionnelle anti-doublon **bascule** pour les documents sous mandat de `(supplier_siren, document_number)`
vers **`(tenant, mandant_id, document_number)`** (le « supplier » fiscal est le mandant). Mise en œuvre : champ
**`MandatId` additif (nullable)** dans `DuplicateCheckRequest` (`null` = non-389 → clé historique inchangée) +
**index d'unicité SQL** sur la clé 389. La bascule est **atomique côté base** (l'index garantit l'unicité), **jamais**
une simple « neutralisation du SIREN » applicative (qui ouvrirait une fenêtre de doublon sur un 389 en attente
ré-extrait). **F06 §4 est amendé en conséquence** (réécriture jointe à cet ADR). La règle
`(supplier_siren, document_number)` **reste valide pour les non-389**.

**Désambiguïsation `document_number`** : en 389, le `document_number` de la clé anti-doublon est le **BT-1 fiscal
alloué par mandant** (le numéro source n'alimente que l'idempotence interne via `SourceReference`, §1) ; le pipeline
peuple `DuplicateCheckRequest.DocumentNumber` avec ce **BT-1 alloué**, pas avec le numéro brut de la source.

### 7. Alignement avec l'unicité **CTC plateforme**

Le contrôle d'unicité bloquant de la plateforme (Annexe 7 G1.42/G1.45) porte sur **`(BT-1, BT-2, BT-30 = SIREN du
fournisseur)`**, où le fournisseur BT-30 est le **mandant** en 389. La re-clé interne `(tenant, mandant_id,
document_number)` est **cohérente** avec ce contrôle (le `mandant_id` interne mappe le SIREN mandant BT-30) : on ne
se contente pas de l'anti-doublon interne, on aligne sur la clé que la PA/PPF rejettera.

### 8. Portée : structure et comportement, **aucun code, aucune décision fiscale**

Cet ADR **n'écrit aucun code**. Il ne fixe pas la **valeur** du préfixe/racine (paramétrage tenant), ni le statut
d'assujettissement (F15 §6, EC). Il n'introduit **aucun** nouveau mécanisme transverse hors la séquence par mandant
et le champ `MandatId` additif.

## Invariants

- **INV-BT1-1** — En 389, `Number` (BT-1 du payload hashé) **reste rempli** par l'identifiant source interne
  (`SourceReference`) ; le **BT-1 fiscal** est une valeur séparée **hors payload hashé**. Le `payload_hash`
  (ADR-0007) est **identique** au cas non-389 (test : round-trip canonique inchangé, pas de branche de format).
- **INV-BT1-2** — L'allocation du BT-1 fiscal est **idempotente** : ré-extraction d'un même document source →
  **même** BT-1, **jamais** ré-allocation (test : double appel get-or-create → un seul numéro consommé).
- **INV-BT1-3** — Un document 389 **sans clé d'idempotence source** est **rejeté** (substitution d'invariant, pas
  affaiblissement) — test explicite.
- **INV-BT1-4** — La séquence est **par mandant** et **tenant-scopée** (`MandatSequence` clé
  `(company_id, mandant_id)`, verrou par mandant, jamais cross-tenant) ; `NextValue` en `bigint`, **jamais float**.
- **INV-BT1-5** — Anti-doublon : deux 389 de **même numéro** mais **mandants différents** → **Send** ; **même
  mandant** + ré-extraction → **Blocked** (tests cartésiens) ; la clé `(supplier_siren, document_number)` **reste**
  appliquée aux **non-389** (aucune régression).
- **INV-BT1-6** — La re-clé est portée par un **index d'unicité SQL** (atomique), **jamais** par une neutralisation
  applicative du SIREN.

## Conséquences

**Positif** : on respecte la numérotation fiscale par mandant **sans** casser l'idempotence ni ADR-0007 ; le P1
« allocation vs écriture non atomiques » est résolu par l'idempotence (pas par une transaction distribuée fragile) ;
l'anti-doublon devient **correct** en 389 (clé par mandant) **sans** régression sur les non-389 ; on s'aligne sur le
contrôle d'unicité que la PA/PPF appliquera (G1.42/G1.45). Réutilisation des briques existantes (`SourceReference`,
`payload_hash`, `DuplicateCheckRequest`) — **aucun** mécanisme transverse nouveau hors le champ `MandatId` et la
séquence par mandant.

**À la charge du(des) lot(s) d'implémentation** : `MandatSequence` + allocateur get-or-create idempotent (verrou par
mandant) ; champ `MandatId` additif dans `DuplicateCheckRequest` + index d'unicité SQL `(tenant, mandant_id,
document_number)` ; assignation du BT-1 fiscal hors payload hashé, à l'émission ; **migration F06** (l'index
historique reste pour les non-389) ; tests : INV-BT1-1..6, round-trip canonique inchangé, cartésien anti-doublon.

**Limite** : cet ADR ne tranche **ni** le workflow d'acceptation (ADR-0024), **ni** la capacité B (ADR-0026), **ni**
les **valeurs** fiscales (préfixe/racine, statut d'assujettissement — F15 §6). L'acceptabilité doctrinale de trous
résiduels (continuité §1.4) reste un point EC **non bloquant** pour cette décision (allocation au plus tard).
La **numérotation et la re-clé anti-doublon de l'avoir auto-facturé (261)** — introduit par ADR-0024 §5 — sont
**reportées** : cet ADR borne sa décision (`MandatSequence`, re-clé, INV-BT1-*) au **389** ; savoir si le 261
partage la `MandatSequence` du mandant relève de **F15 §6.5 (NON TRANCHÉ)** — ne pas l'inventer.

## Alternatives rejetées

- **Réutiliser le numéro brut de la source comme BT-1 fiscal** : viole BOI-TVA-DECLA-30-20-20-10 (séquence distincte
  par mandant) ; un legacy numérote globalement. **Rejetée.**
- **Retirer `Number` du hash canonique pour le 389** (format conditionnel au flux) : casse la reproductibilité
  octet-par-octet et amende la règle additive d'ADR-0007. **Rejetée** (cf. Décision §3).
- **Allouer le BT-1 dès l'ingestion** : maximise les trous (tout document avorté en Validation consomme un numéro)
  et tend la contrainte « continue » sans bénéfice. **Rejetée** au profit de l'allocation au plus tard (§5).
- **Re-clé anti-doublon par simple neutralisation du SIREN** : ouvre une fenêtre de doublon sur un 389 en attente
  ré-extrait (la clé ne discrimine plus par mandant). **Rejetée** au profit d'une re-clé atomique `(tenant,
  mandant_id, document_number)` (§6).
- **Transaction distribuée allocation↔écriture** (deux schémas, deux modules) : fragile, couple `Mandats` et
  `Documents`. **Rejetée** au profit de l'idempotence rejouable (§2).

## Références

- `docs/conception/F15-Autofacturation-Mandat.md` §3 (allocation hybride, règle nette, garde-fous), §1.4/§1.8
  (numérotation sourcée + Annexe 7 G1.42/G1.45), §6.7 (trous débloqués)
- `docs/conception/F06-Tracking-Piste-Audit.md` §4 — **amendé par cet ADR** (réécriture de la règle anti-doublon
  pour intégrer le cas 389, la clé non-389 restant figée)
- `docs/adr/ADR-0007-serialisation-canonique-pivot.md` (Number non optionnel, **ordre de déclaration** ; Number =
  2ᵉ propriété du canonique, `CanonicalJson.cs:45`) ; `docs/adr/ADR-0022-...md` §6 point (b) ; **ADR-0024** (acceptation, ADR sœur)
- Briques réelles : `SourceReference` (`PivotDocumentDto.cs`, `CanonicalJson.cs:49-50`), `PayloadHasher`,
  `DuplicateCheckRequest`/`DocumentDuplicatePolicy`. BOI-TVA-DECLA-30-20-20-10 (18/10/2013) ; Annexe 7 V1.9 G1.42/G1.45.
