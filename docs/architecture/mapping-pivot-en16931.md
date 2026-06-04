# Mapping du pivot Liakont → EN 16931 → Flux 10.x (e-reporting DGFiP)

> **Objet (PIV02, acceptance §3).** Vérifier que les champs du modèle pivot
> (`Liakont.Agent.Contracts.Pivot`) couvrent les *Business Terms* EN 16931 nécessaires aux **Flux
> 10.x** (e-reporting) de la réforme, et tracer chaque champ vers son code sémantique.
>
> **Sources (rien n'est inventé ici — CLAUDE.md n°2).**
> - `docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md` §3 (colonne « Réf. BT » par champ).
> - DGFiP v3.2, **Annexe 6 — Format sémantique FE e-reporting V1.10**
>   (`docs/references/dgfip-v3.2/2- Annexes_v3.2/`) et les XSD officiels
>   `docs/references/dgfip-v3.2/3- XSD_v3.2/1 - E-reporting/` (`transaction.xsd`, `payment.xsd`) —
>   d'où sont tirés les identifiants **TG-/TT-** cités.
> - Delta v3.1 → v3.2 sans impact e-reporting (`docs/references/dgfip-v3.2/LECTURE-LIAKONT.md`).
>
> **Rappel d'architecture.** Liakont ne produit PAS le XML DGFiP : il transmet le pivot à une
> Plateforme Agréée qui génère le Flux 10.x. Les codes TG-/TT- servent de **référence de validation
> amont** (s'assurer que le pivot porte de quoi produire un flux valide), pas de format de sortie.
> Le **mapping de la catégorie TVA** (régime source brut → UNCL5305) et les **agrégats**
> (ventilation TVA par taux, paiements par jour × taux) sont calculés sur la PLATEFORME (lots TVA,
> PIP), pas par l'agent : le pivot porte la donnée par ligne / par paiement, brute.

---

## 1. Périmètre V1

| Flux | Intitulé | Granularité pivot | Statut V1 |
|---|---|---|---|
| **10.3** | e-reporting de transactions B2C | `PivotDocumentDto` (au document) | ✅ V1 (validé staging) |
| **10.4** | e-reporting de paiements B2C | `PivotPaymentDto` (paiement brut, agrégé par le Pipeline) | ✅ V1 |
| 10.1 / 10.2 | B2B international / paiements internationaux | — | ⏳ Phase 2 (décision D2 : V1 = domestique) |

---

## 2. Document — `PivotDocumentDto` (EN 16931 niveau facture → e-reporting TG-8 « Invoice »)

| Champ pivot | Type | Réf. EN 16931 | e-reporting (Annexe 6) | Notes |
|---|---|---|---|---|
| `SourceDocumentKind` | string | BT-3 | TT-21 `TypeCode` | Valeur source BRUTE ; la classification 380/381 vit dans Validation (ADR-0004 D3-3) |
| `Number` | string | BT-1 | TT-19 `Invoice/ID`, TT-91 `InvoiceID` | Clé d'idempotence |
| `IssueDate` | DateTime | BT-2 | TT-20 `IssueDate`, TT-102 | Format pivot `yyyy-MM-dd` (ADR-0007) |
| `SourceReference` | string | — | — | Réconciliation + piste d'audit, non transmis |
| `Supplier` | PivotPartyDto | BG-4 | TG-12 `Seller` | cf. §3 |
| `Customer` | PivotPartyDto? | BG-7 | TG-14 `Buyer` | Optionnel en B2C |
| `Totals` | PivotTotalsDto | BG-22 | TG-22 `MonetaryTotal` | cf. §5 |
| `Lines` | List\<PivotLineDto\> | BG-25 | TG-24 `Line` | cf. §6 |
| `CreditNoteRefs` | List\<PivotDocumentRefDto\> | BT-25 | TG-11 `ReferencedDocument` | cf. §8 |
| `Payments` | List\<PivotPaymentDto\> | — (F09) | `payment.xsd` TG-35 | cf. §9 |
| `DocumentCharges` | List\<PivotDocumentChargeDto\> | BG-20 / BG-21 | TG-20 / TG-21 `AllowanceCharge` | cf. §7 |
| `OperationCategory` | enum | mention FR | TG-31 `Transactions/CategoryCode` (TT-81) 🔶 | Conditionne le reporting de paiement (part services) |
| `CurrencyCode` | string | BT-5 | TT-22 `CurrencyCode` | ISO 4217 |
| `Invoicer` | PivotPartyDto? | (auto-facturation, ADR-0004 D3-6) | — | Informatif V1 ; e-invoicing phase 2 |
| `Payee` | PivotPartyDto? | BG-10 | — | Affacturage ; phase 2 |
| `IsSelfBilled` | bool | (brut, ADR-0004 D3-6) | — | Indice brut ; interprété par Validation |
| `PrepaidAmount` | decimal? | BT-113 | — | Acompte ; chaînage acompte→solde (phase 2) |
| `SourceData` | string? | — | — | Traçabilité (JSON brut), non transmis |

---

## 3. Tiers — `PivotPartyDto` (BG-4 / BG-7 / BG-10 → TG-12 `Seller` / TG-14 `Buyer`)

| Champ pivot | Réf. EN 16931 | e-reporting | Notes |
|---|---|---|---|
| `Name` | BT-27 / BT-44 | — (B2C : non nominatif côté DGFiP) | |
| `Siren` | BT-30 (scheme 0002) | TT-33 `Seller/CompanyId` / TT-36 `Buyer/CompanyId` | |
| `Siret` | BT-30 (scheme 0009) | TT-33 / TT-36 (schemeId) | |
| `VatNumber` | BT-31 | TT-34 `Seller/TaxRegistrationId` / TT-38 `Buyer` | |
| `Address` | BG-5 / BG-8 | TG-13 / TG-15 `PostalAddress` | cf. §4 |
| `Email` | — | — | Notifications uniquement |
| `IsCompanyHint` | — | — | Indice brut ; bascule B2B décidée par Validation (VAL05) |

## 4. Adresse — `PivotAddressDto` (BG-5 / BG-8)

| Champ pivot | Réf. EN 16931 | e-reporting | Notes |
|---|---|---|---|
| `CountryCode` | BT-40 / BT-55 | TT-35 / TT-39 `CountryId` | ISO 3166-1 alpha-2 ; **seul champ d'adresse requis par le Flux 10.x** |
| `Line1` / `Line2` / `PostalCode` / `City` | BG-5 / BG-8 | — | Portés par le pivot, non requis par l'e-reporting B2C |

## 5. Totaux — `PivotTotalsDto` (BG-22 → TG-22 `MonetaryTotal`)

| Champ pivot | Réf. EN 16931 | e-reporting | Notes |
|---|---|---|---|
| `TotalNet` | BT-109 | TT-51 `TaxExclusiveAmount` | |
| `TotalTax` | BT-110 | TT-52 `TaxAmount` | |
| `TotalGross` | BT-112 | — | Contrôle BR-CO-15 (= BT-109 + BT-110), vérifié par Validation (F04) |
| `SourceTotalGross` | — | — | Contrôle de cohérence d'extraction (optionnel, ADR-0004 D3-5) |

## 6. Ligne — `PivotLineDto` (BG-25 → TG-24 `Line`) et taxe de ligne `PivotLineTaxDto` (BG-30 → TG-23 `TaxSubTotal`)

| Champ pivot | Réf. EN 16931 | e-reporting | Notes |
|---|---|---|---|
| `Line.Description` | BT-153 | TT-76 `Product/Name` | |
| `Line.Quantity` | BT-129 | TT-62 `BilledQuantity` | |
| `Line.UnitPriceNet` | BT-146 | TT-69 `Price/PriceAmount` | |
| `Line.NetAmount` | BT-131 | base de TT-54 `TaxableAmount` (agrégé plateforme) | |
| `Line.SourceRegimeCodes` | — (source brut) | — | Entrée du mapping TVA plateforme (F3) → catégorie/taux |
| `Tax.CategoryCode` | BT-151 | TT-56 `TaxCategory/Code` | **Résultat** du mapping plateforme (nul tant que non mappé) |
| `Tax.Rate` | BT-152 | TT-57 `Percent` | |
| `Tax.TaxAmount` | — | base de TT-55 `TaxAmount` (agrégé plateforme) | |
| `Tax.VatexCode` | BT-121 | TT-59 `TaxExemptionReasonCode` | Obligatoire si catégorie E / taux 0 (contrôlé par Validation) |

> La **ventilation TVA du document** (TG-23 : `TaxableAmount`/`TaxAmount`/`Category` par taux) est
> AGRÉGÉE par la plateforme à partir des taxes de ligne — le pivot porte le détail par ligne.

## 7. Charges / remises document — `PivotDocumentChargeDto` (BG-20 / BG-21 → TG-20 / TG-21)

| Champ pivot | Réf. EN 16931 | e-reporting | Notes |
|---|---|---|---|
| `IsCharge` | (indicateur) | attribut `ChargeIndicator` | `true` = charge (BG-21), `false` = remise (BG-20) |
| `Amount` | — | TT-45 (remise) / TT-48 (charge) | HT |
| `SourceRegimeCodes` | — (source brut) | TT-46/49 `TaxCategoryCode`, TT-47/50 `TaxPercent` (via mapping plateforme) | |
| `Reason` / `ReasonCode` | — | — | Motif (optionnel) |

## 8. Référence d'origine — `PivotDocumentRefDto` (BT-25 → TG-11 `ReferencedDocument`)

| Champ pivot | e-reporting | Notes |
|---|---|---|
| `Number` | TT-30 `ID` | Multi-références (avoirs groupés) |
| `IssueDate` | TT-31 `IssueDate` | **Date OBLIGATOIRE** (F07-F08 §B.3 ; B2Brouter exige `amended_date`) |
| `SourceReference` | — | Traçabilité |

## 9. Paiement — `PivotPaymentDto` (F09 → `payment.xsd` TG-34/35/36)

| Champ pivot | e-reporting (Annexe 6 `payment.xsd`) | Notes |
|---|---|---|
| `RelatedDocumentNumber` | TT-91 `InvoiceID` | Lettrage ; rattachement au document |
| `PaymentDate` | TT-92 `Payment/Date` | |
| `Amount` | TT-95 `SubTotals/Amount` | **Réparti par taux (TT-93) par le Pipeline** (agrégat jour × taux) |
| `Method` | — | Informatif, non transmis |
| `SourceReference` | — | Traçabilité |

---

## 10. Conclusion de couverture (acceptance PIV02 §3)

**Le pivot V1 couvre tous les Business Terms requis par les Flux 10.3 (transactions B2C) et 10.4
(paiements B2C).** Aucun champ obligatoire de l'Annexe 6 v3.2 nécessaire à ces flux n'est manquant —
**aucun champ n'est à ajouter** au pivot pour la V1.

Les éléments de l'e-reporting calculés par AGRÉGATION (ventilation TVA par taux du document, sous-
totaux de paiement par taux) ne sont **pas** des champs du pivot : ils sont dérivés sur la plateforme
(mapping TVA F3 + agrégation Pipeline) à partir des données par ligne / par paiement que le pivot
porte. C'est conforme au principe « l'agent transmet le brut, la plateforme calcule » (F01-F02 §3.7).

Champs présents dans les XSD e-reporting mais **hors périmètre V1 B2C** — donc volontairement non
portés (et non « manquants ») : `DueDate` (TT-201), `TaxDueDateTypeCode` (TT-24/80), `Delivery`
(TG-17/19), `InvoicePeriod` (TG-18/25), notes de facture/ligne (TG-9/61), `SellerTaxRepresentative`
(TG-16). Ils relèvent du e-invoicing B2B (phase 2) ou d'options non requises pour le B2C ; les
ajouter relèverait d'une montée de version sourcée (CLAUDE.md n°2), pas de la V1.
