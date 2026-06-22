# F7 + F8 — Avoirs & Frontière B2B/B2C
### Document de conception — Gateway.Core (Pipeline, Validation, Pivot)

> Statut : 🟨 issu de la deep research DR4 (2026-06-02) — **recherche la plus solide du lot : 25/25 affirmations confirmées sur sources primaires (BOFiP, impots.gouv.fr, OpenPEPPOL)**. À revoir ensemble.
> Légende : ✅ confirmé source primaire · 🔶 probable · ❓ décision/zone d'ombre réglementaire

---

## PARTIE A — La frontière B2B / B2C (F8)

### A.1 Le critère de qualification (✅ confirmé, art. 289 bis CGI)

**L'e-invoicing obligatoire via PA s'applique UNIQUEMENT quand l'émetteur ET le destinataire sont des assujettis à la TVA établis en France.** Tout le reste relève de l'e-reporting.

| Situation | Régime | Flux | Granularité |
|---|---|---|---|
| Vendeur FR assujetti → **acheteur FR assujetti** | e-invoicing | Flux 1/2 | par facture, via PA, routage annuaire |
| Vendeur FR → **particulier / non-assujetti** (B2C) | e-reporting | Flux 10.3 | **agrégé** jour/devise/type/taux |
| Vendeur FR → **assujetti étranger** (non établi FR) | e-reporting | **Flux 10.1** | **par facture** (sémantiquement comme l'e-invoicing, sans le SIREN routé) |

Points critiques confirmés :
- **Le critère est le STATUT d'assujetti, pas le fait que la TVA soit due.** Un client en franchise en base est assujetti → relève de l'e-invoicing B2B. (Piège : un petit professionnel sans TVA apparente est quand même B2B.)
- **Le routage e-invoicing exige le SIREN/SIRET de l'acheteur** (interrogation de l'annuaire central). Sans SIREN, le routage est techniquement impossible.
- L'acheteur étranger (Flux 10.1) est « par facture » — donc proche de l'e-invoicing dans la structure, mais sans routage. Important pour une SVV qui vend à des marchands étrangers.

### A.2 Le risque central (✅ confirmé, BOI-TVA-SECT-90-50)

> **Déclarer en e-reporting B2C une vente qui relève de l'e-invoicing B2B prive l'acheteur professionnel de la facture électronique régulière dont il a besoin pour déduire la TVA, et constitue un manquement à l'obligation d'e-invoicing.**

C'est exactement le risque du projet CMP : une SVV qui traite *tous* les bordereaux en B2C (parce que la base ne porte pas le SIREN acheteur) se trompe pour les acheteurs marchands/professionnels. D'où le garde-fou F8.

### A.3 Cas spécifique ventes aux enchères (✅ confirmé, BOI-TVA-SECT-90-50 §140-§360)

- Le commissaire-priseur / OVV **agissant en son nom propre** est fiscalement un **acheteur-revendeur (intermédiaire opaque)** : il est réputé acquérir auprès du commettant puis revendre à l'acquéreur. Il **délivre lui-même** la facture/bordereau à l'acquéreur (art. 256 V, 256 bis III, 289 I-1-d CGI).
- Donc deux « jambes » fiscales : commettant→OVV (bordereau vendeur) et OVV→acquéreur (bordereau acheteur). **Le périmètre CMP ne couvre que la 2e jambe** (bordereaux acheteurs) — cohérent.
- **Sous régime de la marge (art. 297 E) : le bordereau ne peut JAMAIS faire apparaître la TVA distinctement** (mention « Régime particulier – Biens d'occasion »). Sous régime au prix total : la TVA doit apparaître. → Ces deux régimes se modélisent différemment (déjà traité par le modèle 2 lignes + F3).

### A.4 Heuristique de détection d'un acheteur professionnel (❓ zone d'ombre — aucune méthode officielle)

La recherche confirme le **besoin** du SIREN mais **aucune source primaire ne donne de méthode de détection** quand il est absent. C'est donc à nous de définir une heuristique prudente. Proposition pour `IsCompanyHint` :

| Indice (dans EncheresV6) | Force |
|---|---|
| `entete_ba.societe` renseigné (raison sociale) | fort |
| n° TVA intracommunautaire présent | fort |
| Forme du nom (SARL, SAS, SA, EURL, EI…) détectée par regex | moyen |
| Montant élevé (seuil ?) | faible (à ne pas utiliser seul) |

**Comportement V1 (garde-fou, pas traitement) :**
1. Si `IsCompanyHint = true` → le document est **bloqué** avec le motif « Acheteur potentiellement professionnel — circuit B2B (e-invoicing) non disponible en V1, traitement manuel requis ».
2. L'opérateur tranche dans la console : soit « confirmer B2C » (acheteur particulier malgré l'indice → débloque en B2C, décision journalisée), soit « traiter manuellement en B2B hors passerelle » (en attendant la phase 2).

> ⚠️ **Décision réglementaire à valider avec l'expert-comptable / le CMP** : quel volume réel d'acheteurs professionnels dans les ventes du CMP ? Si > quelques %, la phase 2 B2B devient prioritaire et non optionnelle. Si quasi nul (ventes de gages → particuliers), le garde-fou suffit largement.

### A.5 Workflow de régularisation a posteriori (❓ procédure officielle non documentée)

Aucune source primaire ne décrit la procédure officielle quand un SIREN est récupéré après coup. Position prudente V1 : la passerelle **ne tente pas** de régulariser automatiquement un B2B mal classé. Elle **bloque en amont** (A.4) pour éviter la mauvaise déclaration. La régularisation reste un acte manuel/comptable hors passerelle en V1. À réétudier en phase 2.

---

## PARTIE B — Les avoirs (F7)

> **⚠️ AMENDEMENT (2026-06-04 — frontière de couverture VAL04, item VAL04)** : la règle des avoirs du
> module Validation (`CreditNoteRule`) DÉTECTE un avoir par sa **référence de facture d'origine** (EN 16931
> BG-3 / BT-25 = `CreditNoteRefs`) et bloque alors : montant(s) négatif(s) (§B.2), facture d'origine
> inconnue (orphelin, §B.4) ou connue mais non émise (§B.5). **Cas NON couvert par VAL04, tracé ici pour
> qu'il ne tombe pas entre deux items** : un avoir dont la source porte le *type* « avoir » mais SANS
> aucune référence d'origine (ex. avoir EncheresV6 sans `no_ba_lettrage`). Le détecter exige la
> **classification facture/avoir** à partir du type source brut (`SourceDocumentKind`) — concern PLATEFORME
> non encore bâti, dont la correspondance type→avoir n'est PAS spécifiée (elle varie par logiciel source) ;
> VAL04 ne l'invente pas (CLAUDE.md n°2). Cette classification relève — par ADR-0004 D3-3 — du **module
> Validation**, via une **table tenant** `SourceDocumentKind` → facture/avoir (validée par l'EC, jamais
> devinée). Voir **F04 §3.5bis** pour la spécification complète.
>
> **✅ MISE À JOUR (2026-06-20 — item RD405)** : le **mécanisme de validation est livré**. La règle
> `SourceDocumentKindCreditNoteRule` (Validation.Domain) consomme l'abstraction tenant-scopée
> `ISourceDocumentKindClassifier` : un document classé « avoir » par son type source mais SANS aucune
> référence d'origine (`CreditNoteRefs` vide) est désormais **bloqué** (`CREDIT_NOTE_KIND_WITHOUT_ORIGIN`),
> jamais silencieusement laissé passer. Le trou fonctionnel ci-dessus est donc **fermé au niveau du
> mécanisme**. **Reste en suivi (NON couvert par RD405, voir F04 §3.5bis)** : la PERSISTANCE de la table
> tenant et son provisioning par seed `deployments/<client>/` — tant qu'elle n'est pas provisionnée, le
> classificateur par défaut répond « non classé » et le repli reste la détection structurelle
> (`CreditNoteRefs`). La classification n'est donc PLUS un concern du pipeline d'envoi : elle vit dans
> Validation (le rapprochement de l'original avec le Tracking, lui, reste au pipeline, §B.5).

### B.1 Deux logiques de correction, selon le flux (✅ confirmé, cahier des charges v3.0)

| Contexte | Mécanisme de correction |
|---|---|
| **e-invoicing B2B** | Soit (a) un **avoir** (CreditNote 381) qui annule, puis une nouvelle facture ; soit (b) une **facture rectificative** annule-et-remplace (la facture d'origine n'est alors pas comptabilisée par l'admin). |
| **e-reporting (B2C / paiements)** | Correction par **flux rectificatif type RE** : annule et remplace **l'ensemble des données agrégées de la période** (par SIREN + période). Il existe aussi un type MO (modificatif, ciblé) et la voie de la transmission complémentaire. |

**Conséquence majeure pour le B2C (cas CMP) :** un avoir B2C ne s'envoie pas comme un « document avoir » isolé qui se soustrait — il corrige le **CA agrégé de la période**. En pratique avec B2Brouter : on envoie l'avoir comme `IssuedSimplifiedInvoice` avec `is_credit_note: true`, et c'est B2Brouter qui recalcule l'agrégat du Ledger de la période. **À confirmer avec B2Brouter** : comment leur API matérialise la rectification d'un agrégat déjà transmis (re-soumission RE automatique ? fenêtre de correction ?).

### B.2 Modélisation technique d'un avoir (✅ confirmé, OpenPEPPOL / Peppol BIS 3.0)

Même si B2Brouter prend du JSON et génère le XML, ces règles dictent ce que notre pivot doit porter (et ce que la PA validera) :

1. **Type de document** : `CreditNoteTypeCode = 381` (UNTDID 1001). Les codes 81/83/396/532 sont synonymes.
2. **Montants POSITIFS** : la fonction « crédit » est portée par le *type de document*, **jamais par le signe**. Mélanger 381 + montants négatifs est **prohibé**. ✅ Cohérent avec EncheresV6 où l'avoir est « copie en positif » (`bordereau_ou_avoir='A'`).
3. **Lien vers la facture d'origine** : `cac:BillingReference / cac:InvoiceDocumentReference / cbc:ID` (BT-25, groupe BG-3).
   - ⚠️ **CRITIQUE** : référencer une **facture** (`InvoiceDocumentReference`), **JAMAIS** un `CreditNoteDocumentReference` — c'est un bug sémantique connu.
   - Côté API B2Brouter : `amended_number` + `amended_date`.
4. **Cardinalité 0..n** : un avoir peut référencer **plusieurs** factures d'origine → couvre les avoirs groupés/partiels.
5. **Date d'origine obligatoire** si la référence n'est pas unique.

### B.3 Modèle pivot pour l'avoir

Réutilise `PivotDocument` (cf. F1/F2) avec :
- `DocumentType = CreditNoteB2C` (ou `CreditNoteB2B` en phase 2)
- `CreditNoteRef` = liste de `{ Number, IssueDate }` des factures d'origine (cardinalité 0..n)
- montants positifs, mêmes lignes/taxes que la facture corrigée (ou la partie corrigée)

### B.4 Cas limites

| Cas | Traitement |
|---|---|
| **Avoir partiel** (une partie du bordereau) | lignes correspondant à la part annulée, lien vers la facture d'origine. Pas de difficulté technique. |
| **Avoir groupé** (plusieurs factures) | `CreditNoteRef` multi-références (0..n le permet). |
| **Avoir dont la facture d'origine n'a PAS été émise électroniquement** (facture pré-réforme, ou émise hors passerelle) | ❓ **zone d'ombre — aucune source primaire.** Position V1 : si l'original n'est pas dans le Tracking de la passerelle, l'avoir est **bloqué** avec motif « facture d'origine inconnue de la passerelle — vérifier le rattachement / traiter manuellement ». On ne fabrique pas une référence à une facture qu'on n'a pas émise. |
| **Avoir EncheresV6 sans lien `no_ba_lettrage`** | bloqué, même logique. |

### B.5 Comportement de la passerelle face à un avoir (Pipeline)

```
1. L'adaptateur extrait l'avoir (EncheresV6 : bordereau_ou_avoir='A', lien no_ba_lettrage)
2. Le Pipeline cherche la facture d'origine dans le Tracking (via no_ba_lettrage → SourceReference)
   - trouvée + déjà émise → CreditNoteRef renseigné → envoi
   - trouvée mais PAS encore émise → traiter l'original d'abord (ordre chronologique)
   - introuvable → BLOQUÉ (B.4)
3. Mapping TVA identique à l'original (F3), montants positifs
4. Envoi via PA (is_credit_note + amended_number/date)
```

---

## C — Décisions à valider ensemble

| # | Décision | Recommandation |
|---|---|---|
| 1 | Volume réel d'acheteurs pros au CMP → garde-fou suffisant ou phase 2 B2B prioritaire ? | **À chiffrer avec le CMP/expert-comptable** — détermine si F8 reste un garde-fou ou devient un vrai chantier |
| 2 | Heuristique `IsCompanyHint` : quels indices retenir ? | `societe` rempli OU n° TVA présent (les 2 indices forts). Pas le montant seul |
| 3 | Comportement sur avoir orphelin (origine hors passerelle) | bloquer + motif, pas de référence fabriquée |
| 4 | Question à poser à B2Brouter | Comment leur API matérialise la rectification d'un agrégat B2C déjà transmis (flux RE) ? Fenêtre de correction ? |
| 5 | `OperationCategory` Mixte (biens + services) au CMP | confirmer avec expert-comptable — impacte l'e-reporting de paiement (F9) |
