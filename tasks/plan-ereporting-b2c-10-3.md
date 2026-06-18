# Plan d'implémentation — E-reporting B2C marge (Chantier 1)

> Plan **interactif**, non seedé dans `orchestration/manifest.yaml` ; les renvois GATE_DEMO_ISATECH /
> PIP03b sont documentaires. Itéré sur redline 2026-06-18.

## Statut de sourcing (à jour)
**Sourcé (texte / code vérifié)** : art. 297 E « pas de TVA distincte sous régime de la marge »
(F03:41, F07-F08:36) · OVV intermédiaire opaque + deux jambes (F07-F08 §A.3:32-36) · VATEX-EU-F/I/J marge
(F03 §2.2) · capacités `SupportsB2cReporting` / `PaCapability.B2cReporting` existent
(`PaCapability.cs`, `PaCapabilityNotSupportedResult.cs`) · paramètres `VatOnDebits` / `OperationCategory` /
`FeeImputationMethod` existent avec `null→suspendre` (`FiscalSettings.cs`) · `ReportingFrequency` = chaîne
**opaque** non figée (INV-TENANTSETTINGS-008).
**NON sourcé (à établir avant tout code)** : la **composition arithmétique** de la marge (« frais
acheteur + frais vendeur ») · l'existence/définition d'éventuelles **méthodes de calcul de marge** ·
l'**énumération** `reportingFrequency` (régime vs fréquence ; trimestrielle vs bimestrielle) · la **forme**
du « montant de marge » transmis (cas DGFiP n°33).

---

## 0. Cadrage

### 0.1 Principe de gouvernance (décision Karl, 2026-06-18)
**Aucune ratification expert-comptable ne sera demandée.** Pour chaque fork fiscal :
- **2+ options légales ANCRÉES dans une `F*.md` → PARAMÉTRAGE tenant** (défaut non renseigné → **suspendre**).
  Une source 🔶 (non re-confirmée) reste un ancrage valide, mais le **figeage** d'un enum doit citer la `F*.md`.
- **Lecture unique / choix d'archi non-fiscal → on implémente.**
- **0 option ancrée → reste BLOQUÉ** (paramétrer y *serait* l'invention — règle n°2).
- Multi-PA : comportement piloté par les **capacités déclarées** (`PaCapabilities`), **jamais** `if (pa is X)`.
  L'état d'intégration d'**un** plug-in n'est jamais un blocage produit.

### 0.2 Deux questions distinctes à ne pas confondre
1. **Quels documents sont dans le périmètre produit** → la **2e jambe** (bordereaux acheteurs). La 1re
   jambe (commettant→OVV) est **hors périmètre produit** (F07-F08 §A.3:34-35).
2. **Comment se compose la marge** → l'OVV est intermédiaire opaque (art. 256 V ; régime de la marge
   art. 297 A/E ; BOI-TVA-SECT-90-50 §140-360 confirme les **deux jambes**, F07-F08 §A.3:32-36).
   **Cadrage opérateur (Karl)** : les **frais vendeur (BV) entrent dans le périmètre de la marge — ce n'est
   pas une option** ; les omettre = marge tronquée. **MAIS la composition arithmétique exacte est une
   HYPOTHÈSE NON SOURCÉE** : aucune `docs/conception/F*.md` ne porte « marge = frais acheteur + frais
   vendeur ». Elle doit être confirmée par **B2C-05** (prérequis bloquant) avant tout calcul (n°2).

### 0.3 Deux niveaux de produit
- **Essentiel — bordereau acheteur en document 10.3** : présentation régime de la marge (adjudication
  `E/0%/VATEX-EU-F/I/J` + frais acheteur `S/20%`), **sans montant de marge** (art. 297 E). Validé staging.
  Ne nécessite QUE la BA → démo-able tout de suite sur Fake ; la PA agrège au Ledger.
- **Complet — montant de marge (cas DGFiP n°33)** : nécessite la marge réelle. **SI B2C-05 confirme la
  formule, alors BV devient obligatoire.** Tant que B2C-05 n'a pas ancré la formule dans F03, l'obligation
  de BV reste une hypothèse — aucun calcul de marge (B2C-09) n'est codé.

---

## 1. Classification des forks (verdicts vérifiés)

| Fork | Verdict | Paramètre / action | Déjà codé ? |
|---|---|---|---|
| **Exigibilité part frais** (10.4) | **PARAMÉTRAGE** | `vatOnDebits` : `true`→pas de 10.4 ; `false`→dû ; `null`→suspendre | **oui** |
| **Régime 6 → marge vs hors-champ** | **PARAMÉTRAGE** | table mapping TVA tenant ; `defaultBehavior:block` ; ne pas seeder EU-J en prod | **oui** |
| **VATEX de la marge** | **PARAMÉTRAGE** | `vatexCode` (codelist EN16931 : EU-F/I/J) ; défaut block | **oui** |
| **Catégorie TVA (AA/AAA…)** | **PARAMÉTRAGE** | enum fermé `VatCategory` ; ne pas retirer AA/AAA ; acceptation PA validée à l'envoi | **oui** |
| **OperationCategory** (LB/PS/Mixte) | **PARAMÉTRAGE** | paramétrage **tenant** → projeté sur `PivotDocumentDto.OperationCategory` à l'ingestion (`PivotEmitterEnricher`, ADR-0023) ; `null`→suspendre | **oui** |
| **Méthode d'imputation frais** (mono-cat.) | **PARAMÉTRAGE** | `FeeImputationMethod` {Prorata, AgregationJourTaux} ; `null`→Suspended | **oui** |
| **Modélisation du 10.3** (archi) | **IMPLÉMENTER** | router via `SendDocumentAsync` ; ne PAS greffer sur `SendPaymentReportAsync` ; gate **uniquement** sur un envoi de **déclaration 10.3** (voir B2C-01) | gate à câbler |
| **Cadence** `reportingFrequency` | **À TRANCHER puis figer** | modélisation (régime vs fréquence) + réconciliation D4↔F09 NON tranchées (INV-TENANTSETTINGS-008) ; chaîne opaque `null`→suspendre en attendant | champ opaque **oui** ; enum **non figeable encore** |
| **Composition de la marge** (frais acheteur + frais vendeur) | **À SOURCER** *(B2C-05, bloquant)* | BV **dans le périmètre** (cadrage Karl) ; **formule arithmétique non sourcée** → pas de calcul tant que B2C-05 ne l'a pas ancrée (n°2) | **non** |
| **Méthode de calcul de marge** | **À SOURCER avant de nommer** *(B2C-05)* | NE PAS pré-câbler d'enum ; si le texte primaire distingue des options → paramétrage ; sinon → §0.1 « 0 option → bloqué » | **non** |
| **« Montant de marge »** (transmission, cas n°33) | **CAPACITÉ PA** (générique) + **shape gelée** | gate par capacité déclarée, validé à l'envoi (résultat typé) ; **forme du payload (champ/VATEX) gelée tant que non sourcée** (ticket fournisseur ouvert, F03:117) | **non** |
| **Extraction frais vendeur (BV)** | **REQUIS si B2C-05 confirme** — prérequis **schéma source réel** | structure inconnue (pas de `no_lot` ; jointure courante = `no_ba`) → schéma ISATECH réel (prod) / fixture fictive (démo) | **non** |
| **Déclaration néant** | **DÉFAUT SÛR** *(0 source)* | défaut = ne rien transmettre + **alerte de supervision visible (SUP01)** ; revisable si DGFiP l'impose | à câbler |

> `null → suspendre/bloquer` est déjà encodé partout. Le paramétrage est surtout du câblage + un seed démo.

---

## 2. Lot 1 — Essentiel : document 10.3 + fondations (CODABLE MAINTENANT, Fake)

- [ ] **B2C-01 — Router le 10.3 au document + gate de capacité ciblée (M).**
  - **Préalable d'actionnabilité** : aucun marqueur de flux 10.3 n'existe sur `PivotDocumentDto`. B2C-01
    doit **d'abord définir comment un document 10.3 est identifié** (champ additif pattern EXT01, ou
    dérivation documentée) — **pas** via `BuyerLooksProfessionalRule` (détection-seule/bloquante, ne route pas).
  - La garde `SupportsB2cReporting` s'applique **UNIQUEMENT à un envoi de DÉCLARATION 10.3**, **jamais** au
    transport d'une facture/Factur-X B2C ni aux avoirs/B2B. `SendDocumentAsync` est la **voie unique de tous
    les documents** : une garde générale y casserait l'Essentiel générique (`GeneriqueCapabilities` :
    `SupportsFacturXTransmission=true` **et** `SupportsB2cReporting=false`) et les avoirs/B2B.
  - Câbler côté **SEND** (`SendTenantJob`), résultat typé **`PaCapabilityNotSupportedResult.Create(paName,
    PaCapability.B2cReporting)`** (PAS `PendingCapability`, qui est un statut Pipeline paiement/rectification).
    **Ne PAS** introduire de nouvelle capacité (`SupportsB2cReporting` existe déjà ; aujourd'hui consommée
    en lecture console — `TenantSettingsConsoleQueries.cs:100`, `ComptesPaView.razor:380` — mais pas comme
    garde d'envoi).
- [ ] **B2C-02 — Paramétrage fiscal tenant du chemin 10.3 + seed démo (S-M).** Honorer `defaultBehavior:block`
  et `OperationCategory null → suspendre`. Seed démo (régimes décidés) ; **régime 6 non mappé → BLOCAGE
  démontré** (comportement correct).
- [ ] **B2C-03 — Traçabilité reporting↔pièces (append-only) + export autoportant (M).** Lien figé à la
  transmission, APPEND-ONLY (n°4), deux sens, tenant-scopé (n°9). Réutilise chaînage/horodatage/WORM/
  `FiscalControlExport`.
- [ ] **B2C-04 — Démo bout-en-bout sur PA Fake (S-M).** Extraction BA → pivot → mapping → 10.3 au document
  via Fake → lien B2C-03 → export. Déclenchement manuel + « transmis/accusé » + blocage régime 6.
  bUnit/Playwright (page Blazor sans test = P1, review n°19). Dépend de B2C-01/02/03.

---

## 3. Lot 2 — Marge complète : montant de marge (BV intégral au périmètre)

> **B2C-05 est le prérequis fiscal BLOQUANT de tout le Lot 2** : aucun calcul/transmission de marge n'est
> codé tant qu'il n'a pas ancré la formule (et l'éventuelle méthode) sur texte primaire.

- [ ] **B2C-05 — Sourcer la marge sur texte primaire et l'ancrer dans F03 (S, sourcing, sans EC, BLOQUANT).**
  - **Identifier ET citer la source primaire** de la composition de la marge (BOI/CGI) — l'item doit
    *établir* la source, pas la présupposer. Tant qu'elle n'est pas ancrée dans F03, « marge = frais
    acheteur + frais vendeur » reste une hypothèse → **pas de calcul** (n°2).
  - **Sourcer l'existence et la définition d'éventuelles méthodes de calcul AVANT de nommer un enum.**
    **NE PAS** pré-câbler `{Globalisation, CoupParCoup}`. Si le sourcing établit plusieurs options →
    PARAMÉTRAGE ; s'il n'en distingue aucune → « 0 option → BLOQUÉ » (§0.1), pas d'enum inventé.
- [ ] **B2C-06 — Obtenir le schéma source réel du bordereau vendeur (S, prérequis ISATECH).** Quelle
  table/vue, quelles colonnes (montant/régime), **quelle clé de rattachement au lot** (le `no_lot` présumé
  n'existe pas ; jointure courante = `no_ba`). → demande ISATECH / GATE_DEMO_ISATECH. Démo : modéliser un
  bordereau vendeur **fictif** (fixture/`config/exemples`, donnée fictive n°7).
- [ ] **B2C-07 — Étendre l'adaptateur EncheresV6 pour lire BV (M, dépend BLOQUANT de B2C-06).** Lire BV
  **selon la structure tranchée par B2C-06** : soit (a) nouveau `type_ligne` du **MÊME** bordereau
  (rattachement `no_ba`, aucune jointure), soit (b) document distinct à joindre par une clé à identifier.
  **Ne pas écrire de SQL de jointure tant que B2C-06 n'a pas livré table/colonnes/clé.** SELECT-only (n°5),
  constante SQL dans `EncheresV6Schema` (point unique) ; extraction pure, aucune logique fiscale (n°6).
- [ ] **B2C-08 — Porter BV comme DONNÉE SOURCE de calcul dans le pivot (S-M, dépend de B2C-07).** BV est
  une **donnée de calcul de marge**, pas une ligne facturée à l'acheteur : la porter via
  `PivotLineDto.SourceData` ou un **champ additif hash-neutre au grain lot** (pattern EXT01 — seul un
  marqueur additif **absent** est hash-neutre ; **ajouter une `PivotLineDto` n'est PAS hash-neutre** :
  `Lines` est toujours sérialisée + impacte `Totals`). **Jamais** via une ventilation `Taxes/Rate/TaxAmount`,
  **jamais** via une valeur de part `frais_vendeur` (`TvaMappingPart` est figé `{Adjudication, Frais, Autre}`
  — l'inventer violerait n°2 ; et l'injecter en ligne taxable du document acheteur risquerait une TVA
  distincte interdite, art. 297 E). Ce champ alimente B2C-09, pas la base imposable du document 10.3.
- [ ] **B2C-08bis — Persister/éditer le paramètre « méthode de marge » en console (S, conditionné à B2C-05).**
  Seulement **si** B2C-05 établit des options. Champ `FiscalSettings.MargeMethod?` (liste fermée),
  command/handler, exposition console + **test bUnit** (n°19), doc F12-A §3.2. `null`→suspendre.
- [ ] **B2C-09a — Capacité PA « montant de marge » + gate typé (S-M).** Décider si le montant de marge
  relève d'une capacité distincte ou de `B2cReporting` ; propager la capacité à **tous** les plug-ins +
  gate typé (`PaCapabilityNotSupportedResult`). **La forme du payload (champ/code VATEX) reste gelée tant
  que non sourcée** (cas n°33) — le gate et la plomberie sont codables, pas le shape.
- [ ] **B2C-09b — Calcul de la marge + transmission (M-L, dépend de B2C-05/08/09a (+ B2C-08bis si méthodes établies) ET B2C-03).** Appliquer la
  formule (B2C-05) selon la méthode paramétrée. **Calcul en `decimal`, arrondi half-up 2 décimales via
  `PivotRounding.RoundAmount` ; test d'arrondi obligatoire (n°1).** **Critère bloquant (art. 297 E)** : le
  payload du montant de marge **ne porte AUCUNE ligne ni total de TVA distincte** (`TotalTax=0`, aucune
  ventilation > 0) ; un cas qui ferait apparaître une TVA distincte **échoue** (adossé à F03:41 /
  F07-F08:36, aucun seuil inventé). Lien reporting↔pièces (B2C-03) au grain lot. Si la méthode
  « globalisation » est retenue, son **agrégation par période** est un mécanisme neuf (≠
  `PaymentAggregationCalculator`, base encaissement) → sizing L.

---

## 4. Lot 3 — Piste 10.4 (part frais, paiement)

> **B2C-10/11/12 mettent en œuvre l'item EXISTANT `PIP03b` (runtime = blocked, `PIP.yaml:330-371`).**
> Ne PAS créer d'items concurrents — débloquer/re-découper PIP03b dans le manifest (geste opérateur).

- [ ] **B2C-10 — Trancher la modélisation de `reportingFrequency`, puis figer l'enum (S, conditionné).**
  Point à trancher (F12-A §3.3 / INV-TENANTSETTINGS-008) : (a) stocker le **RÉGIME** {RéelNormalMensuel,
  RéelNormalTrimestriel, RéelSimplifié, FranchiseEnBase} d'où dériver fréquence+délai via F09 §2, **OU** la
  **FRÉQUENCE** {Décadaire, Mensuelle, Bimestrielle} ? ; (b) réconcilier D4↔F09 §2 (retirer « trimestrielle »,
  statuer « bimestrielle »). Figer l'enum est légitime **une fois (a)+(b) tranchés** ; jusque-là chaîne
  opaque nullable, `null`→suspendre. Table de dérivation 🔶 = paramétrage/seed **révisable**, jamais calcul
  codé en dur.
- [ ] **B2C-11 — PIP03b : fenêtrage déclaratif + envoi 10.4 (L, BLOQUÉ jusqu'à l'arbitrage B2C-10).** Params
  déjà câblés (`vatOnDebits`, `FeeImputationMethod`, `SupportsDomesticPaymentReporting` déjà consommée,
  `payment_aggregate_events` V004 append-only, RE livré PIP04). Neuf réel = groupage en périodes + envoi +
  marqueur RE + néant (B2C-12). Gate capacité **générique** `SupportsDomesticPaymentReporting` (Fake=true
  pour la démo). Mixte reste suspendu (découpage frais/adjudication non sourcé).
- [ ] **B2C-12 — Déclaration néant = ne rien transmettre + alerte SUP01 visible (S).** Défaut sûr (0 source) ;
  jamais d'agrégat vide envoyé ; **alerte de supervision visible (SUP01), jamais un log silencieux** ;
  revisable si DGFiP l'impose. Partie de B2C-11.

---

## 5. Séquencement

```
Lot 1 (Essentiel, démo Fake — aucune dépendance) :
   B2C-01 / B2C-02 / B2C-03  (parallélisables)  ──►  B2C-04   (ordre 01/02/03 non bloquant)

Lot 2 (Marge complète — BV intégral) :
   B2C-05 (SOURCER formule+méthode — BLOQUANT de tout le Lot 2)
        ├─► B2C-08bis (méthode, si options établies) ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┐
        └─► B2C-06 (schéma BV : ISATECH / fixture) ─► B2C-07 (extraction) ─► B2C-08 (donnée source pivot) ─┤
                                                                                                            ├─► B2C-09b
   B2C-09a (capacité+gate, shape gelée) ────────────────────────────────────────────────────────────────┤   (calcul+transmission)
   B2C-03 (lien reporting↔pièces) ──────────────────────────────────────────────────────────────────────┘

Lot 3 (Paiement 10.4 = PIP03b) :
   B2C-10 (trancher modélisation cadence)  ┄conditionne┄►  B2C-11 (fenêtrage+envoi, BLOQUÉ jusqu'à B2C-10)  ◄─ B2C-12 (néant)
                                                            └── gate capacité « Encaissée » (générique ; démo = Fake)
```

**Démo immédiate (Essentiel)** : B2C-01/02/03 → B2C-04 (Fake). **Démo marge complète** : B2C-05 (sourcing)
→ B2C-06 (fixture fictive) → B2C-07/08/09a/09b. Le **schéma BV réel** (B2C-06 prod) est le seul prérequis
externe — technique (ISATECH), pas fiscal. **B2C-11** reste BLOQUÉ jusqu'à l'arbitrage B2C-10 (cohérent
avec PIP03b gelé).

---

## 6. Pièges d'invention fiscale à surveiller en review (chacun = P1, règle n°2)

1. Greffer le 10.3 sur le contrat de paiement (`PaymentReportFlux`/`SendPaymentReportAsync`).
2. Réutiliser `PaymentAggregationCalculator` (base encaissement) pour le 10.3 (base transaction).
3. Faire apparaître une TVA distincte sous régime de la marge (art. 297 E l'interdit — F03:41/F07-F08:36).
4. Coder un défaut `vatOnDebits` / une cadence par défaut / un prorata d'imputation au lieu de suspendre.
5. Seeder en prod le mapping régime 6 → VATEX-EU-J (exemple fictif ; défaut = bloquer).
6. **Calculer/transmettre un montant de marge sur une formule non sourcée** — tant que B2C-05 n'a pas
   confirmé la composition sur texte primaire, tout calcul est bloqué (n°2). Cadrage acquis : BV est dans le
   périmètre (l'omettre = marge tronquée), mais la formule arithmétique **n'est pas un fait sourcé** tant que
   B2C-05 ne l'a pas établie.
7. **Inventer le schéma source BV** (table/colonnes/clé) au lieu de le sourcer (ISATECH réel / fixture
   fictive explicite) — règles n°2/n°7.
8. **Pré-nommer un enum de méthode de marge** (`Globalisation/CoupParCoup`) sans l'avoir sourcé (B2C-05).
9. Fabriquer une déclaration néant (agrégat vide envoyé) ou la dégrader en log silencieux (→ alerte SUP01).
10. Porter BV en **ligne taxable** du document acheteur (gonfle la base, risque TVA distincte) ou en part
    `frais_vendeur` inventée (`TvaMappingPart` figé) — BV = **donnée source de calcul**, pas ligne facturée.
11. `if (pa is X)` ou retirer AA/AAA : multi-PA piloté par capacités, acceptation validée à l'envoi.
```
