# ADR-0026 — Capacité B (réception/rapprochement) : contrat `IPaInboundClient` séparé et agrégat `InboundDocument` distinct (esquisse, phase 2)

- **Statut** : Proposé — **esquisse** (2026-06-15). Périmètre **phase 2** (blueprint §13) ; **ne bloque pas** la
  capacité A (émission 389).
- **Date** : 2026-06-15
- **Nature** : cet ADR **précède** le chantier (aucun code) et reste une **esquisse** : il pose les **frontières**
  de la réception pour qu'elles ne soient pas violées par anticipation, **sans** figer le modèle de données ni le
  workflow (qui seront tranchés dans une F-spec dédiée). Il ne tranche **aucun point fiscal** : le statut fiscal
  d'un bordereau acheteur multi-vendeurs (F15 §6.2) et le circuit d'enrôlement/impersonation PA (F15 §6.3) restent
  **NON TRANCHÉS**.
- **Numérotation** : ADR-**0026**. ADR-0023 = lib PDF/A-3 (Factur-X, en cours) ; 0024 = acceptation/`ISelfBilledGate` ;
  0025 = BT-1 hybride + re-clé F06.
- **Contexte décisionnel** : `docs/conception/F15-Autofacturation-Mandat.md` §5 (esquisse capacité B), §6.2/§6.3
  (NON TRANCHÉ) ; `blueprint.md` §6 (frontières : un plug-in PA ne référence que `Transmission.Contracts`), §13
  (phase 2 réception) ; `CLAUDE.md` n°5 (lecture seule, aucune écriture/ordre de virement), n°8 (comportement
  piloté par `PaCapabilities`, jamais par un flag produit ni `if (pa is …)`) ; `docs/adr/ADR-0022-...md` §6 point (c) ;
  repo externe `C:\Source\Liakont-GoToMarket` : `Metiers/Encheres/Roadmap-Gestion-BV-Liakont.md` §2-B.

## Contexte

La capacité B lit le **flux entrant** de la PA (factures fournisseurs reçues) et le **rapproche** d'un décompte
interne (côté criée : le bordereau mareyeur multi-vendeurs, F15 §4). C'est un flux **de sens opposé** à l'émission :
il n'émet rien, il **réconcilie** et **signale**. Le risque, si on l'anticipe mal, est de **polluer la machine
d'émission** (états, append-only) ou de **glisser vers une action financière** (rapprocher = déclencher un
paiement), ce qu'interdit la règle n°5. Cet ADR pose les garde-fous de frontière **avant** que quiconque ne code la
réception.

## Décision (esquisse — frontières seulement)

### 1. Contrat de réception **`IPaInboundClient` SÉPARÉ** d'`IPaClient`

La réception est exposée par un **contrat distinct** dans `Transmission.Contracts`, jamais fondue dans `IPaClient`
(émission). Un plug-in PA implémente l'un, l'autre, ou les deux ; la **disponibilité de la réception est déclarée
par `PaCapabilities`** (ex. `SupportsInbound`), **jamais** par un flag produit ni un `if (pa is …)` (CLAUDE.md n°8).
Un plug-in PA ne référence que `Transmission.Contracts` (blueprint §6).

### 2. Agrégat **`InboundDocument` distinct** (ne pas polluer l'émission)

La facture reçue vit dans un **agrégat distinct** `InboundDocument`, **séparé** de la machine d'émission
`DocumentState` et de son append-only. Tenant-scopé (`company_id`). Aucune transition d'émission n'est réutilisée
pour la réception.

### 3. Rapprochement par **`MatchConfidence`** — jamais de lien automatique en confiance non haute

Le rapprochement facture entrante ↔ décompte interne produit une **confiance** (`MatchConfidence`). Un lien
**automatique** n'est posé **qu'en confiance haute** ; en confiance **non haute**, le rapprochement est
**proposé** et exige une **validation humaine** (console). Jamais de réconciliation silencieuse.

### 4. Le conditionnement du règlement est un **SIGNAL**, jamais une écriture

Le résultat du rapprochement est un **feu vert/rouge** restitué en **console/export** (« cette facture reçue
correspond / ne correspond pas au décompte »). Ce n'est **jamais** une écriture dans la base source, **jamais** un
ordre de virement ni une action financière (règle n°5). La passerelle **informe**, elle n'**agit** pas sur la
trésorerie.

### 5. Portée : esquisse, phase 2 — **ne bloque pas la capacité A**

Le **modèle de données** d'`InboundDocument`, l'algorithme de `MatchConfidence`, le **statut fiscal** d'un bordereau
multi-vendeurs (F15 §6.2) et le **circuit d'enrôlement/impersonation PA** pour N vendeurs (F15 §6.3) sont **hors
périmètre** de cet ADR : ils relèveront d'une **F-spec dédiée** en phase 2. La capacité A (émission 389, ADR-0024/
0025) **n'en dépend pas**.

## Invariants

- **INV-RECV-1** — La réception est exposée par `IPaInboundClient` **distinct** d'`IPaClient` ; sa disponibilité est
  pilotée par `PaCapabilities`, **jamais** par un flag produit ni `if (pa is …)` (NetArchTest + test de capacité).
- **INV-RECV-2** — `InboundDocument` est un **agrégat distinct** ; **aucun** état/append-only de la machine
  d'émission n'est réutilisé pour la réception ; tenant-scopé (`company_id` NOT NULL).
- **INV-RECV-3** — Aucun lien de rapprochement **automatique** hors confiance **haute** ; sinon validation humaine.
- **INV-RECV-4** — La réception ne produit **qu'un signal** (console/export) : **aucune** écriture sur la base
  source, **aucun** ordre de virement (règle n°5) — test prouvant l'absence de chemin d'écriture/paiement.

## Conséquences

**Positif** : les frontières sont posées avant tout code, donc un futur lot réception ne pourra ni polluer
l'émission, ni glisser vers une action financière, ni coupler un plug-in PA à autre chose que `Transmission.Contracts`.
La capacité A avance **indépendamment**.

**À la charge de la phase 2** : F-spec dédiée (modèle `InboundDocument`, `MatchConfidence`, statut fiscal
multi-vendeurs, enrôlement PA), implémentation, tests.

**Limite** : esquisse — aucun modèle de données ni workflow figé ici ; points fiscaux F15 §6.2/§6.3 non tranchés.

## Alternatives rejetées

- **Fondre la réception dans `IPaClient`/`DocumentState`** : mêle deux flux de sens opposé, pollue la machine
  d'émission et son append-only. **Rejetée.**
- **Piloter la réception par un flag produit** (`ReceptionEnabled`) : doublonne une capacité PA, viole CLAUDE.md
  n°8. **Rejetée** au profit de `PaCapabilities`.
- **Rapprocher automatiquement en confiance moyenne** ou **déclencher un règlement** : viole la règle n°5 et le
  principe « bloquer/ signaler plutôt qu'agir ». **Rejetée.**

## Références

- `docs/conception/F15-Autofacturation-Mandat.md` §5 (esquisse capacité B), §6.2/§6.3 (NON TRANCHÉ) ;
  `blueprint.md` §6 (frontières plug-in PA), §13 (phase 2) ; `CLAUDE.md` n°5/n°8
- `docs/adr/ADR-0022-...md` §6 point (c) ; ADR sœurs **0024** (acceptation), **0025** (BT-1 hybride + re-clé F06)
- repo externe `C:\Source\Liakont-GoToMarket` : `Metiers/Encheres/Roadmap-Gestion-BV-Liakont.md` §2-B
