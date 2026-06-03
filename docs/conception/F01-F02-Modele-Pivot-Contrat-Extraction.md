# F1 + F2 — Modèle pivot & Contrat d'extraction (IExtractor)
### Document de conception — Gateway.Core/Pivot + frontière adaptateurs

> Statut : 🟨 issu de la deep research DR1 (2026-06-02) + connaissances projet (RECAP, analyse données EncheresV6, API B2Brouter). À revoir ensemble.
> Légende de confiance : ✅ confirmé (source primaire vérifiée ou validé en staging) · 🔶 très probable (source citée, non re-vérifiée) · ❓ décision à prendre
>
> **✅ VEILLE RÉGLEMENTAIRE RÉSOLUE (2026-06-02)** : cette spec cite les spécifications externes
> DGFiP **v3.1 (31 octobre 2025)**. La **v3.2 (30/04/2026)** a été téléchargée et dépouillée —
> elle est dans `docs/references/dgfip-v3.2/` (avec note de lecture LECTURE-CONFORMAT.md).
> **Delta v3.1 → v3.2 minime, aucun impact sur le périmètre Conformat V1** : e-reporting =
> suppression d'un attribut xmlns (cosmétique), e-invoicing = BG-25 commenté dans le profil BASE
> (phase 2), annuaire = aucun changement (source : Changelog_XSD.md officiel). Cette spec reste
> valide. Reste à faire (humain) : lecture du Dossier général v3.2 (texte) et croisement de
> l'Annexe 7 (règles de gestion V1.9) avec F04 lors du lot VAL.

---

## 1. Ce que la recherche a établi (DR1)

### ✅ Confirmé sur sources primaires (AFNOR Norm'Info, impots.gouv.fr)

1. **Le triptyque normatif AFNOR est la référence** du contrat de données :
   - **XP Z12-012** : « Formats et Profils des messages Factures et Statuts de cycle de vie, constitutifs du socle minimal » — formats facture/avoir alignés **EN 16931 + profil EXTENDED-CTC-FR**, en syntaxes **UBL, CII et Factur-X**, + statuts de cycle de vie via message **CDAR**.
   - **XP Z12-013** : « API pour interfacer les SI des entreprises avec les PA » — le contrat API SI↔PA. Couvre **tous les flux** : factures (Flux 2), statuts (Flux 6), e-reporting (Flux 10), annuaire (Flux 11-12).
   - **XP Z12-014** : cas d'usage B2B.
2. **Les spécifications externes DGFiP v3.1 (31 octobre 2025)** sont la référence réglementaire en vigueur pour le déploiement du 1er sept. 2026.
3. Conséquence structurante : **un pivot conforme doit porter 4 familles d'objets** — la facture, le statut de cycle de vie, la donnée d'e-reporting, la référence annuaire — pas seulement la facture.

### 🔶 Très probable (cohérent avec nos propres docs projet, à confirmer sur les spec externes v3.1)

- EN 16931 ≈ 160-165 Business Terms (BT) organisés en Business Groups (BG) avec cardinalités ; BT obligatoires : BT-1 (n°), BT-2 (date), BT-3 (type 380/381), BT-5 (devise), BT-27 (nom vendeur), BT-40 (pays vendeur), BT-44 (nom acheteur), BT-55 (pays acheteur), BT-106→115 (totaux).
- E-reporting en 4 sous-flux : **10.1** (B2B international, par facture), **10.2** (paiements internationaux), **10.3** (B2C, agrégeable par jour/taux), **10.4** (paiements B2C).
- B2C : transmission **agrégée par jour et par taux de TVA** permise (c'est la PA qui agrège — vérifié avec B2Brouter : on envoie au document, leur Ledger agrège).

### 📌 Action prioritaire issue de la recherche

> **Télécharger et dépouiller les sources primaires** (gratuites) :
> 1. Spécifications externes DGFiP v3.1 — impots.gouv.fr/specifications-externes-b2b
> 2. Normes AFNOR XP Z12-012 / -013 / -014 (mises à disposition gratuitement dans le cadre de la réforme)
>
> C'est le seul moyen d'avoir la **liste exacte et opposable** des champs obligatoires. La conception ci-dessous est structurée pour être complétée champ par champ depuis ces documents (colonne "Réf. BT").

---

## 2. Décision d'architecture : sur quoi aligner le pivot ?

| Option | Pour | Contre | Verdict |
|---|---|---|---|
| **(a) Aligner sémantiquement sur EN 16931** (chaque champ du pivot = un BT identifié) | Référence stable, européenne, indépendante de la PA ; vocabulaire opposable | Plus riche que le besoin V1 | ✅ **RETENU** |
| (b) Copier les structures de l'API B2Brouter | Mapping trivial vers la PA actuelle | Couplage à un fournisseur ; vocabulaire propriétaire | ❌ rejeté (mais la sérialisation B2Brouter reste triviale depuis (a)) |
| (c) Adopter les structures XP Z12-013 comme modèle interne | Futur-proof multi-PA "à la norme" | Norme expérimentale, payload pas encore figé, sur-ingénierie V1 | ❌ rejeté en V1 — **la sérialisation XP Z12-013 sera une sortie supplémentaire** le jour où une PA l'expose |

**Principe retenu : le pivot est un modèle C# aligné sémantiquement sur EN 16931 (chaque propriété documente son BT), et les formats de sortie (JSON B2Brouter aujourd'hui, XP Z12-013 ou Factur-X demain) sont des sérialiseurs à la frontière.**

---

## 3. Le modèle pivot (spécification)

### 3.1 `PivotDocument` — le document à transmettre

| Propriété | Type | Réf. EN 16931 | Obligatoire | Notes |
|---|---|---|---|---|
| `DocumentType` | enum | BT-3 | ✅ | `InvoiceB2C` (380, e-reporting), `CreditNoteB2C` (381), `InvoiceB2B` (380, e-invoicing — phase 2), `CreditNoteB2B` (381 — phase 2) |
| `Number` | string | BT-1 | ✅ | Unique par émetteur. Clé d'idempotence vers la PA |
| `IssueDate` | DateTime | BT-2 | ✅ | |
| `CurrencyCode` | string | BT-5 | ✅ | ISO 4217, défaut "EUR" |
| `SourceReference` | string | — | ✅ | Identifiant dans le système source (ex. `no_ba`) — réconciliation + piste d'audit |
| `Supplier` | PivotParty | BG-4 | ✅ | L'émetteur (la SVV / l'entreprise cliente) |
| `Customer` | PivotParty | BG-7 | selon flux | Obligatoire en B2B ; en B2C : nom/adresse si dispo (non transmis à la DGFiP, mais utile au document) |
| `Lines` | List\<PivotLine\> | BG-25 | ✅ ≥ 1 | |
| `Totals` | PivotTotals | BG-22 | ✅ | Totaux de contrôle — comparés à la somme des lignes par F4 |
| `CreditNoteRef` | PivotDocumentRef? | BT-25 | si avoir | N° + date du document d'origine |
| `OperationCategory` | enum | mention FR | ✅ | `LivraisonDeBiens` / `PrestationDeServices` / `Mixte` — mention obligatoire réforme, conditionne l'e-reporting de paiement |
| `SourceData` | dictionnaire | — | optionnel | Données brutes utiles à la traçabilité (régimes source, montants originaux non arrondis) |

### 3.2 `PivotParty` — un tiers

| Propriété | Type | Réf. | Notes |
|---|---|---|---|
| `Name` | string | BT-27/BT-44 | ✅ |
| `Siren` | string? | BT-30 (scheme 0002) | Émetteur : ✅ obligatoire (vient de la config, pas de la base — cf. EncheresV6). Acheteur : si présent → **bascule B2B** |
| `Siret` | string? | BT-30 (scheme 0009) | optionnel |
| `VatNumber` | string? | BT-31 | n° TVA intracommunautaire |
| `Address` | PivotAddress | BG-5/BG-8 | rue, CP, ville, **pays ISO 3166-1 alpha-2** (BT-40/BT-55) |
| `Email` | string? | — | utile aux notifications, pas transmis |
| `IsCompanyHint` | bool | — | **[Amendé 2026-06-03]** Transcription BRUTE d'un champ source (ex. champ `societe` non vide) — AUCUNE heuristique côté agent/adaptateur. TOUTE l'heuristique (formes juridiques, n° TVA, décision de blocage) vit dans le module Validation de la plateforme (VAL05) → garde-fou F8 |

### 3.3 `PivotLine` — une ligne

| Propriété | Type | Réf. | Notes |
|---|---|---|---|
| `Description` | string | BT-153 | ✅ |
| `Quantity` | decimal | BT-129 | défaut 1 |
| `UnitPriceNet` | decimal | BT-146 | |
| `NetAmount` | decimal | BT-131 | montant HT de la ligne |
| `Tax` | PivotLineTax | BG-30 | ✅ |
| `SourceRegimeCode` | string | — | **Le code régime TVA du système source, brut.** C'est le moteur de mapping (F3) qui le transforme — jamais l'adaptateur |
| `SourceLineRef` | string | — | référence ligne source (traçabilité) |

### 3.4 `PivotLineTax` — la TVA d'une ligne (résultat du mapping F3)

| Propriété | Type | Réf. | Notes |
|---|---|---|---|
| `CategoryCode` | string | BT-151 | S / E / Z / AE / G / K / O… (UNCL5305) |
| `Rate` | decimal | BT-152 | % |
| `TaxAmount` | decimal | — | montant de TVA de la ligne |
| `ExemptionReasonCode` | string? | BT-121 | code VATEX — **obligatoire si Rate = 0 et catégorie E** (validé en staging : son absence bloque silencieusement) |
| `MappingTrace` | string | — | "régime source 6 → E/0%/VATEX-EU-J par règle X de la table v3" — piste d'audit du mapping |

### 3.5 `PivotTotals`

| Propriété | Réf. | Notes |
|---|---|---|
| `TotalNet` | BT-109 | somme HT |
| `TotalTax` | BT-110 | somme TVA |
| `TotalGross` | BT-112 | TTC. Règle BR-CO-15 : BT-112 = BT-109 + BT-110 (contrôle F4 bloquant) |
| `SourceTotalGross` | — | le total tel que stocké dans le système source (ex. `entete_ba.total_bordereau`) → contrôle de cohérence extraction |

### 3.6 `PivotPayment` — un encaissement (pour F9 / Flux 10.4)

| Propriété | Notes |
|---|---|
| `PaymentDate` | date d'encaissement |
| `Amount` | montant encaissé |
| `Method` | CB / chèque / espèces / virement (info) |
| `RelatedDocumentNumber` | n° du document d'origine si rattachable (lettrage) |
| `SourceReference` | référence source (ex. ligne type 3) |

> La structure exacte de transmission (agrégation jour/taux) dépend de DR5/F9 — ce modèle porte la donnée brute, l'agrégation est faite par le Pipeline.

### 3.7 Règles transverses du pivot

1. **Montants en `decimal`** (jamais float/double). L'adaptateur arrondit à 2 décimales **au plus tard** à la sortie de l'extraction et conserve l'original dans `SourceData` (les bases legacy stockent des flottants sales : `8.329999999999998`).
2. **Le pivot ne calcule rien** : il porte des montants déjà calculés par le système source (principe CMP : "lire les montants calculés par Magic, ne pas recalculer").
3. **Tout champ absent est `null`**, jamais une valeur devinée. C'est la Validation (F4) qui décide si l'absence est bloquante, jamais l'adaptateur.
4. Chaque objet pivot est **sérialisable en JSON** tel quel (Newtonsoft) → c'est ce JSON qui est hashé pour l'anti-doublon (F6) et archivé pour la piste d'audit (DR6).

---

## 4. Le contrat d'extraction : `IExtractor`

### 4.1 Interface

```csharp
public interface IExtractor
{
    /// Identité de l'adaptateur (nom, version, système cible) — affichée en console et journalisée.
    ExtractorInfo GetInfo();

    /// Vérifie l'accès à la source (connexion, droits, schéma attendu). Ne lit aucune donnée métier.
    HealthCheckResult CheckHealth();

    /// Extrait les documents (bordereaux/factures/avoirs) d'une période. LECTURE SEULE. Idempotent.
    /// Retourne les documents dans l'ordre chronologique. Streaming (yield) pour les gros volumes.
    IEnumerable<PivotDocument> ExtractDocuments(DateTime fromInclusive, DateTime toExclusive);

    /// Extrait les encaissements d'une période (pour le e-reporting de paiement). LECTURE SEULE.
    IEnumerable<PivotPayment> ExtractPayments(DateTime fromInclusive, DateTime toExclusive);

    /// Liste les régimes de TVA du système source (code + libellé + indicateurs),
    /// pour alimenter le paramétrage de la table de mapping (F3) et détecter les régimes non mappés.
    IReadOnlyList<SourceTaxRegime> ListSourceTaxRegimes();
}
```

### 4.2 Règles du contrat (ce que tout adaptateur DOIT respecter)

| # | Règle | Justification |
|---|---|---|
| R1 | **Lecture seule absolue** — aucune écriture, aucun verrou explicite, aucune transaction modifiante sur la source | Décision structurante produit (zéro autorisation éditeur nécessaire) |
| R2 | **Idempotence** — deux extractions de la même période retournent les mêmes documents (mêmes `SourceReference`) | L'anti-doublon (F6) repose dessus |
| R3 | **L'adaptateur ne mappe pas la TVA** — il fournit `SourceRegimeCode` brut + les montants calculés par la source | Le mapping est central, paramétrable et audité (F3) |
| R4 | **L'adaptateur ne valide pas** — il extrait ce qui existe, met `null` sur ce qui manque | La validation est centrale et homogène (F4) |
| R5 | **L'adaptateur n'appelle jamais la PA** | Frontières strictes |
| R6 | **Pas d'état interne** — l'adaptateur ne sait pas ce qui a déjà été envoyé (c'est le Tracking F6) | Adaptateur = fonction pure de la source |
| R7 | **Erreurs typées** : `SourceUnavailableException` (réessayable) vs `SourceSchemaException` (config/version incompatible, non réessayable) | Le Pipeline doit savoir quoi faire |
| R8 | Performance : extraction par streaming (`yield return`), jamais tout en mémoire | Volumes : 6-8 000 docs/an au CMP, potentiellement plus ailleurs |

### 4.3 Ce que ça donne pour l'adaptateur EncheresV6 (validation du contrat sur le cas réel)

| Élément du contrat | Implémentation EncheresV6 |
|---|---|
| `ExtractDocuments` | `SELECT entete_ba WHERE bordereau_ou_avoir='B' AND date_vente ∈ [from, to[` + lignes `type_ligne IN ('4','2')` + jointure `Regime_tva` |
| Avoirs | `bordereau_ou_avoir='A'`, lien `no_ba_lettrage` → `CreditNoteRef` |
| `ExtractPayments` | lignes `type_ligne='3'` (`montant_ligne`, `date_reglement`, mode) |
| `ListSourceTaxRegimes` | `SELECT * FROM Regime_tva` → code, libellé, `assujetti_tva`, `vente_ht`, `RegimeMarge`, taux |
| `Supplier.Siren` | **PAS dans la base** (texte libre) → vient de la config, l'adaptateur le reçoit dans son constructeur |
| `Customer.IsCompanyHint` | `entete_ba.societe` non vide |
| `SourceTotalGross` | `entete_ba.total_bordereau` |
| Montants | arrondi 2 déc. (flottants Pervasive sales) ; original conservé dans `SourceData` |

✅ **Le contrat passe le test du cas réel** — chaque méthode a une implémentation directe et naturelle.

### 4.4 Le `FixtureExtractor` (dev + démo)

Implémentation d'`IExtractor` qui rejoue des fichiers JSON (`fixtures/*.json`) :
- Données initiales : générées depuis `Tools\EncheresExtract\extraction-result.json` (vraies données DEMO anonymisées si besoin).
- Sert : au dev sans licence Zen, aux tests unitaires du Pipeline, et à la **démo hors site** (présentation chez ISATECH sans accès au serveur).
- Doit inclure des cas pathologiques fabriqués : SIREN invalide, régime non mappé, acheteur pro, avoir orphelin, montants incohérents → pour démontrer F4/F8 en démo.

---

## 5. Granularité et flux : ce que le pivot doit savoir représenter

| Flux | Granularité | Représentation pivot | Statut V1 |
|---|---|---|---|
| Flux 10.3 — e-reporting B2C | Au document (la PA agrège en Ledger quotidien) | `PivotDocument` type `InvoiceB2C` | ✅ V1 (validé staging) |
| Flux 10.4 — paiements B2C | Agrégat jour × taux (à confirmer DR5) | `PivotPayment` (agrégation par le Pipeline) | 🟨 V1 si B2Brouter le permet |
| Flux 1/2 — e-invoicing B2B | Au document, obligatoirement | `PivotDocument` type `InvoiceB2B` (+ champs paiement obligatoires B2Brouter : PMD/PMT/AAB) | Phase 2 (garde-fou F8 en V1) |
| Flux 10.1 — B2B international | Au document | `InvoiceB2B` + pays ≠ FR | Phase 2 |
| Statuts cycle de vie (Flux 6) | Par document | `PaDocumentStatus` (côté Tracking, pas dans le pivot) | ✅ V1 (lecture seule) |

---

## 6. Données manquantes / dégradées — politique

| Situation | Politique |
|---|---|
| Champ obligatoire absent (ex. date) | Document **bloqué** par F4, motif explicite, jamais de valeur inventée |
| SIREN émetteur absent de la source | Normal (cas EncheresV6) → vient de la **config**, contrôlé par `check-config` |
| Régime TVA source non mappé | Document **bloqué** par F3/F4 ("régime 12 inconnu de la table de mapping") |
| Acheteur sans nom (B2C) | Accepté — le e-reporting B2C ne transmet pas de données nominatives à la DGFiP 🔶 (à confirmer DR5/spec) |
| Montants incohérents (lignes ≠ total source) | **Bloqué** — c'est un signe de bug d'extraction ou de donnée source corrompue |
| Document modifié dans la source APRÈS envoi | Détecté par le hash (F6) → **alerte**, jamais de ré-envoi automatique (un document fiscal émis ne se modifie pas, il s'avoir) |

---

## 7. Décisions à valider ensemble

| # | Décision | Options | Recommandation |
|---|---|---|---|
| 1 | Faut-il dépouiller les spec DGFiP v3.1 + normes AFNOR avant de coder le pivot, ou coder maintenant et ajuster ? | dépouiller d'abord (1-2 j) / coder + ajuster | **Coder maintenant** (le périmètre V1 = B2C est validé en staging, faible risque) et dépouiller en parallèle pour la phase 2 B2B (où le risque est réel) |
| 2 | Le pivot doit-il porter les champs B2B (contact, paiement PMD/PMT/AAB) dès la V1 ? | oui (vide) / non (ajout phase 2) | **Oui, présents et nullables** — éviter une migration de schéma SQLite |
| 3 | `OperationCategory` (LB/PS/mixte) : comment le déterminer pour un bordereau d'enchères ? | toujours "LivraisonDeBiens" / paramétrable / par ligne | ❓ à trancher avec l'expert-comptable — l'adjudication est une livraison de biens mais les frais sont une prestation de services → probablement **Mixte**, ce qui déclenche l'e-reporting de paiement sur la part frais (impact F9 !) |
| 4 | Anonymisation des fixtures de démo | oui / non | oui (noms/adresses remplacés) — la démo circulera |
