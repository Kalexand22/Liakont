# Plan — Transmission e-reporting B2C SuperPDP, agrégée N→1, document-driven — **v2 (post-review)**

> Statut : **plan corrigé après review adversariale (6 lentilles, verdict GO_WITH_EDITS, 6 P1 réels).**
> Évolution de `tasks/plan-ereporting-b2c-10-3.md` (Lot 2). Branche `feat/ereporting-b2c`.
> **Cas = ENCHÈRES (ventes volontaires aux particuliers, régime marge), JAMAIS la criée (B2B).**
>
> ⚠️ **SÉPARATION CARDINALE (P1 review)** : la **FORME** de la marge (taux, conversion HT, role_code) est
> **GELÉE** par `GATE_B2C_SHAPE_SOURCING` (= `gate_pending` ; `B2C09c` = `pending`). Une **décision orale**
> (Karl : « en enchères c'est TTC ») **ne lève PAS** un gate humain et ne vaut PAS l'ancrage `F*.md` exigé
> par la gouvernance §0.1. → On code le **structurel hors-forme (Lot A)** ; la **forme marge (Lot B)** attend
> (a) l'établissement factuel HT/TTC sur la donnée, (b) l'ancrage F03 §2.5 + la levée de gate.

---

## 0. Ce qui est VERROUILLÉ (sourcé) vs GELÉ (gate) vs CORRIGÉ (review)

### 0.1 Verrouillé — sourcé, codable
- **D1** — E-reporting B2C = agrégé N→1 ; **SuperPDP re-agrège côté serveur + envoie au PPF selon
  `company.vat_regime`** (OpenAPI). La cadence n'est pas calculée par nous **mais le fork régime n'est pas
  supprimé : il est déplacé au provisioning de `vat_regime`** (voir A6). | OpenAPI ; principe Karl
- **D2** — Codelist **TT-81 fermée {TLB1,TPS1,TNT1,TMA1}**, fail-closed. | G1.68 (Annexe 7 repo) → F03 §2.6
- **D3** — Schéma de fil SuperPDP (OpenAPI v1.24.0.beta), §2. Montants decimal-string (n°1). | OpenAPI vendorée
- **D8(mécanisme)** — Le store `ReportingPieceLink` (append-only triggers, bidirectionnel, tenant, idempotent)
  est réutilisé **tel quel comme mécanisme**. | revue B2C-03

### 0.2 GELÉ — `GATE_B2C_SHAPE_SOURCING` (P1 #1) : aucun code tant que non levé
- **Le TAUX applicable à la marge** (même « = taux de l'honoraire via F03 ») — F03 §2.5 le défère (statut 🟧 PROPOSÉ).
- **La conversion TTC→HT** `marge_HT = marge_TTC/(1+taux)` — F03 §2.5 : « un implémenteur ne doit pas figer le
  taux ni la formule sans cette validation ».
- **`role_code` (BY/SE)** de la marge et le groupage par rôle — non sourcé (P2 review).
- **Question factuelle bloquante (P1 #3)** : **EncheresV6 `type_ligne 2` émet-il l'honoraire en HT ou TTC ?**
  Le contrat pivot dit **HT** (`PivotBuyerFeeDto.NetAmount` « Montant HT ») et `MarginCalculator` le somme **en
  HT**. Un défaut « TTC + /(1+taux) » **DÉFLATERAIT DEUX FOIS** → marge sous-estimée. **À établir sur la donnée
  réelle/fixture, jamais par un défaut produit deviné.**
- Pour lever : amender **F03 §2.5 décision 1** avec taux + formule + nature HT/TTC **ANCRÉS (source citée)**,
  puis passer la gate.

### 0.3 CORRIGÉ par la review (à appliquer dans le plan/code)
- **P1 #2 — bonne capacité** : la transmission du **montant de marge** se gate sur **`SupportsMarginAmountReporting`**
  (capacité dédiée B2C09a, `PaCapabilities.cs:66`, gate `RequireMarginAmountReporting()`) — **jamais** sur
  `SupportsB2cReporting`. `SuperPdp.SupportsMarginAmountReporting=false` **reste false** jusqu'à confirmation
  sandbox + gate. (D9 reformulé : capacité B2C « true-mais-non-câblée », pas « à rendre honnête ».)
- **P1 #4 — NE PAS supprimer `LotReference`** : c'est un **paramètre positionnel requis** + **1er champ
  sérialisé** du JSON canonique des frais (`CanonicalJson.cs:169-183`). Le retirer change le **hash de tout
  document portant des frais** → casse golden contrat-v1, idempotence, WORM, contrat agent. **Non hash-neutre.**
  → on **conserve `LotReference`** (clé de rattachement source/traçabilité). On ne change QUE la **logique
  d'agrégation** (grouper par taux/jour/devise, **pas par lot**). Karl « pas de lot » = pas de **regroupement
  e-reporting par lot**, ≠ retirer le champ du contrat.
- **P1 #5 — re-clé traçabilité casse l'export** : `FiscalControlExportService.BuildReportingLinkFileAsync`
  joint par `GetByDocumentAsync(companyId, documentId)` (document archivé). Re-cléer sur un `AggregateId`
  (absent du coffre WORM) fait **disparaître silencieusement** `liens-reporting-pieces.json`. → re-pointer le
  consommateur sur `GetBySourceReferenceAsync` (sens pièce→agrégat, existe déjà) **ou** projection
  agrégat→documents ; **test d'intégration export** prouvant la réversibilité ; **geler le lien APRÈS
  confirmation d'envoi**, `AggregateId` **déterministe** (idempotence ON CONFLICT). Lister les tests B2C04 à réécrire.
- **P1 #6 — contrat de transport** : `SendDocumentAsync(PivotDocumentDto)` POST un **XML invoice CII** — un
  `b2c_transaction[]` (JSON `{data:[…]}`, pas d'`external_id`, pas de CII) **ne peut pas** y transiter. →
  **nouvelle méthode `IPaClient` AGNOSTIQUE** (ex. `SendB2cTransactionsAsync(IReadOnlyList<…>)`) propagée à
  **tous** les plug-ins avec **résultat typé `NotSupported`** par défaut + impact `Transmission.Contracts`
  tracé + **NetArchTest**. Jamais détourner le chemin invoice.
- **P2 — anti-doublon = CONCEPTION, pas une probe** : `b2c_transaction` **n'a pas d'`external_id`** (confirmé
  OpenAPI), POST stocke + ré-agrège immédiatement → rejeu = **double e-reporting**. → **clé d'idempotence
  déterministe côté produit** (tenant×date×devise×role×catégorie×taux) + **état d'émission persistant** ;
  ne ré-émettre que le non-acquitté. **Prérequis bloquant de l'agrégateur**, pas une option.
- **P2 — agrégat à la volée par défaut** (R8 tranché) : SuperPDP re-agrège ; l'objet `b2c_transaction` est de
  **transport éphémère**. **Pas d'entité persistée** sauf besoin démontré d'un id stable (corrélation statut
  `GET /ereportings`) **après** la probe de grain. Traçabilité conservée au **grain document existant**.
- **P2 — `id` readOnly** : présent dans `required` mais `readOnly:true` → le builder **n'émet JAMAIS `id`**
  (ni `tax_due_date_type_code` sans source) ; **tester le payload sortant** (clé `id` absente). Tolérer la
  coquille `http_satus_code` en désérialisation d'erreur.

---

## 1. Contrat de fil SuperPDP (sourcé, vendoré) + provisioning + idempotence

`POST /v1.beta/b2c_transactions` — `{ "data": [ b2c_transaction ] }`. `b2c_transaction` :
`category_code`∈{TLB1,TPS1,TNT1,TMA1}* · `currency`(ISO4217)* · `date`(jour)* · `role_code`∈{BY,SE}* ·
`tax_exclusive_amount`(decimal)* · `tax_total`(decimal)* · `tax_subtotals[{tax_percent,taxable_amount,tax_total}]`* ·
`tax_due_date_type_code`(opt) · `id`(integer, **readOnly — jamais émis**). Statut : `GET /ereportings` →
`ppf_ereporting{kind,role_code,start_period,end_period,events[].status_code}`.
**Provisioning cadence** : `company.vat_regime ∈ {monthly,quarterly,simplified,vat_exemption}` via
`PATCH /v1.beta/companies` — **paramétrage tenant ancré ; `null` → suspendre, jamais un défaut deviné** (A6).
**Pas d'`external_id`** → idempotence produit obligatoire (clé déterministe + état d'émission).

---

## 2. Bricks — **Lot A (codable maintenant, hors-forme)** vs **Lot B (GELÉ, forme marge)**

### Lot A — structurel, sourcé, codable maintenant
- [x] **A1 — Gel sources/docs.** F03 **§2.6** (TT-81) *fait* ; OpenAPI **vendorée** *fait* ; reste : section **F14**
  contrat `b2c_transactions`/`ereportings`.
- [ ] **A2 — Domaine TT-81.** `EReportingTransactionCategory` enum fermé + parse/validation **fail-closed**
  (hors-liste → bloqué). Tests : 4 codes + rejet « TLS1 ».
- [ ] **A3 — Gate marge sur la BONNE capacité.** Câbler la garde du **montant de marge** sur
  `SupportsMarginAmountReporting` / `RequireMarginAmountReporting()` (résultat typé). **Ne pas toucher**
  `SupportsB2cReporting`. SuperPDP reste `false`. Test : marge vers PA `false` → bloquée, jamais émise.
- [ ] **A4 — Verbe de transport `IPaClient`.** `SendB2cTransactionsAsync(IReadOnlyList<B2cTransaction>)`
  **agnostique**, propagé à TOUS les plug-ins → `PaCapabilityNotSupportedResult` par défaut. **NetArchTest** +
  test « plug-in sans capacité → NotSupported ». Impact `Transmission.Contracts` tracé (INVARIANTS.md).
- [ ] **A5 — Squelette agrégateur (structure non-fiscale).** Job tenant-scopé qui **collecte** les documents
  B2C-marge et **construit la clé de groupement (jour × devise × catégorie)** + **clé d'idempotence
  déterministe** + état d'émission. **N'inclut PAS** le taux, la conversion HT, le role (Lot B). Calqué sur la
  *structure* de `PaymentAggregatorTenantJob` (**pas** sa base encaissement).
- [ ] **A6 — Provisioning `vat_regime`.** `PATCH /companies` depuis un paramétrage tenant ; `null` → suspendre.

### Lot B — forme marge, **GELÉ jusqu'à (gate levée + HT/TTC établi)**
- [ ] **B1 — Établir la nature HT/TTC** d'`EncheresV6 type_ligne 2` (donnée réelle/fixture) — **prérequis**.
- [ ] **B2 — Amender F03 §2.5** : taux (= taux mappé de l'honoraire, F03), conversion (selon B1), role_code,
  **source citée** ; puis lever `GATE_B2C_SHAPE_SOURCING`.
- [ ] **B3 — Marge : dimension taux + (conversion SI B1=TTC).** Étendre `MarginCalculator` :
  taux = `TvaMapper(SourceRegimeCode)` (fail-closed), **grouper par taux** ; **si B1=HT : aucune conversion**
  (somme HT directe, `TVA = base×taux`) ; **si B1=TTC : `marge_HT = marge_TTC/(1+taux)`**. decimal half-up ;
  garde 297 E conservée. **Conserver `LotReference`** (pas de retrait du contrat).
- [ ] **B4 — Agrégateur (forme).** Compléter A5 : `tax_subtotals` par taux, `tax_exclusive_amount`/`tax_total`,
  `role_code` (sourcé en B2), fail-closed (taux non mappé, catégorie/role non dérivables).
- [ ] **B5 — Payload + envoi SuperPDP.** `SuperPdpB2cPayloadBuilder` (decimal→string invariant, **n'émet pas
  `id`**) + `SendB2cTransactionsAsync` SuperPDP + statut `GET /ereportings`. Gaté `SupportsMarginAmountReporting`.
- [ ] **B6 — Traçabilité re-clé (sûre).** `AggregateId` **déterministe** ; lien gelé **après** confirmation ;
  re-pointer `FiscalControlExportService` (sens pièce→agrégat) ; **test export réversibilité N→1** ; réécrire
  les tests B2C04.
- [ ] **B7 — Tests + envoi RÉEL sandbox** (b2c_transaction comme la facture B2B 72272) + arrondi (n°1) +
  états limites (néant, taux non mappé, rejeu/doublon). + garde CHECK si possible.
- [ ] **B8 — Vérif** : verify-fast (2 sols) + run-tests + Release + codex-review en boucle.

---

## 3. Prérequis externes (probe sandbox — bloquant de B4/B5)
- **Grain** attendu d'un `b2c_transaction` (unitaire vs bucket pré-agrégé). | **Idempotence** (confirmer absence
  d'`external_id` et le comportement de rejeu). | Lecture statut `ppf_ereporting`.

## 4. Décisions qui appartiennent à Karl (avant Lot B)
1. **HT ou TTC** dans EncheresV6 (`type_ligne 2`) ? → détermine s'il y a conversion (sinon double déflation).
2. **Gate** `GATE_B2C_SHAPE_SOURCING` : on ancre dans F03 §2.5 + on lève, ou on tient la forme gelée ?

## 5. Séquencement
```
Lot A (maintenant) : A1 ─ A2 ─ A3 ─ A4 ─ A5 ─ A6   (indépendants, structurels, hors-forme)
Karl : (1) HT/TTC ? + (2) gate ?  ──►  B1 ─ B2 (F03 + gate) ──► B3 ─ B4 ─ B5 ─ B6 ─ B7 ─ B8
probe sandbox (grain/idempotence) ───────────────────────────────► (bloque B4/B5)
```
