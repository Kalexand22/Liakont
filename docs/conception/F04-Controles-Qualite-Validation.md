# F4 — Contrôles de qualité / Validation
### Document de conception — Gateway.Core/Validation

> Statut : 🟨 issu de la deep research DR3 (2026-06-02) + connaissances projet. À revoir ensemble.
> Légende : ✅ confirmé source primaire · 🔶 probable · ❓ à confirmer/décider
>
> **⚠️ AMENDEMENT (2026-06-02 — décision utilisateur, postérieure à la rédaction)** : le contrôle
> « Taux cohérent avec la catégorie (S ⇒ taux > 0 ; E ⇒ 0) » (§3.4) passe de ⚠️ Warning à 🛑 **BLOCKING**.
> Motif : une catégorie incohérente avec son taux est soit une erreur de mapping, soit une donnée
> source fausse — dans les deux cas, l'envoyer transmettrait un motif de taxation erroné
> (règle produit « bloquer plutôt qu'envoyer faux »). Le backlog (orchestration/items/VAL.yaml, VAL04) fait foi.
>
> **⚠️ AMENDEMENT (2026-06-04 — extension du contrôle §3.4 aux 9 catégories, item VAL04)** : la cohérence
> catégorie/taux 🛑 BLOCKING s'applique aux **9 catégories UNCL5305 du pivot** (liste F03 §2.1), pas
> seulement à S et E. Attente de taux par catégorie (la nature de chaque catégorie vient de F03 §2.1 ;
> aucune règle inventée, CLAUDE.md n°2) :
>
> | Catégorie | Attente de taux |
> |---|---|
> | S (normal), AA (réduit), AAA (super réduit) | taux **> 0** |
> | Z (taux zéro), E (exonéré), AE (autoliquidation), G (export hors UE), K (intra-UE), O (hors champ) | taux **= 0** (ou absent) |
>
> Implémenté par `CategoryRateConsistencyRule` (module Validation). **QUESTION OUVERTE** (expert-comptable) :
> existe-t-il un cas légitime de catégorie AA/AAA à taux 0 ? Si oui, cette table sera **amendée** —
> jamais assouplie silencieusement (CLAUDE.md n°2/3).
>
> **⚠️ QUESTION OUVERTE (2026-06-04 — périmètre du contrôle VATEX §3.4, item VAL04)** : la règle
> `VatexRequiredRule` exige aujourd'hui un code VATEX (BT-121) pour la **seule catégorie E** à taux 0 —
> c'est le cas confirmé en staging (absence de VATEX = blocage SILENCIEUX côté PA) et la lettre de §3.4.
> Or les Schematron EN 16931 (BR-AE-10 / BR-G-10 / BR-IC-10 / BR-O-10) imposent aussi BT-121 pour **AE**
> (autoliquidation), **G** (export hors UE), **K** (intra-UE) et **O** (hors champ). En V1, le produit
> N'EMBARQUE PAS le moteur Schematron (§2, décision 1) : c'est B2Brouter qui génère et valide le XML, donc
> ces BR-*-10 sont déjà appliqués côté PA. **À TRANCHER avec l'expert-comptable / au vu du taux de rejet
> réel** : faut-il pré-valider VATEX aussi sur AE/G/K/O (comme pour E) pour attraper le rejet AVANT envoi ?
> Si oui, élargir la condition de `VatexRequiredRule` — jamais en silence, toujours par cet amendement
> (CLAUDE.md n°2). Tant que ce n'est pas tranché, le périmètre reste « E uniquement ».

---

## 1. Principe

**Tout ce que la passerelle peut détecter AVANT l'envoi doit être détecté avant l'envoi.** Un rejet par la PA après envoi coûte un aller-retour, une intervention humaine, et — pour le CMP — une transmission e-reporting potentiellement manquante (500 €). La Validation est la barrière qui transforme « rejet PA » en « blocage local explicite et corrigeable ».

Position dans le pipeline : `Extract → **CHECK** → Send`. Un document qui échoue à un contrôle bloquant n'est jamais envoyé.

## 2. Ce que la recherche a établi (DR3)

### ✅ Confirmé (sources primaires)
1. **La PA et le PPF appliquent les contrôles EN 16931 et rejettent les non-conformes** → on doit pré-valider contre les mêmes règles (impots.gouv.fr/specifications-externes-b2b).
2. **Référence de validation faisant autorité** : artefacts **Schematron officiels CEN/TC 434** (dépôt `ConnectingEurope/eInvoicing-EN16931`, v1.3.16 avril 2026), qui implémentent les règles **BR-*** comme assertions exécutables. Notamment **BR-CO-15 : TTC (BT-112) = HT (BT-109) + total TVA (BT-110)** en sévérité **FATALE**.
3. **EN 16931 n'offre AUCUNE tolérance d'arrondi sur la cohérence arithmétique** : les totaux doivent réconcilier exactement. (BT-114 « Rounding Amount » sert uniquement à l'arrondi du montant à *payer*, ex. 5 centimes — à ne pas confondre.)
4. **Listes de codes contraintes** (genericode v17.0, en vigueur 15/05/2026) : pays (ISO 3166-1), devise (ISO 4217), etc. → validation contre ces listes.
5. **Motifs de rejet DGFiP** (🔶 source vendeur, alignée sur le cadre) : format invalide, mentions obligatoires manquantes (TVA, SIREN, n° facture), Factur-X corrompu, **destinataire introuvable dans l'annuaire PPF**.

### ⚠️ Lacune importante confirmée
**Les extensions nationales françaises (BR-FR-* / CIUS France / EXTENDED-CTC-FR) ne sont PAS dans le dépôt CEN/TC 434** — elles sont maintenues séparément par **FNFE-MPE / Factur-X**. La validation EN 16931 est *nécessaire mais non suffisante* pour la conformité française. → **Action** : récupérer les artefacts FNFE-MPE.

> **Note importante pour notre cas** : nous envoyons du **JSON à B2Brouter**, qui génère le XML et applique lui-même les Schematron. Nous n'avons donc PAS besoin d'embarquer le moteur Schematron en V1. Notre Validation reproduit les **règles métier essentielles** (arithmétique, identifiants, présence) pour éviter les rejets — mais la validation XML formelle reste chez B2Brouter. C'est un choix de coût/risque : on couvre 95 % des rejets avec 5 % de l'effort. (Décision §6.)

## 3. Catalogue des contrôles

Classés en **🛑 Bloquant** (pas d'envoi) / **⚠️ Alerte** (envoi possible, signalé).

### 3.1 Identité émetteur
| Contrôle | Niveau | Détail |
|---|---|---|
| SIREN émetteur présent | 🛑 | Vient de la config (cf. EncheresV6) |
| SIREN émetteur valide (clé de Luhn, 9 chiffres) | 🛑 | cf. algo §4 |
| SIRET émetteur valide si fourni (14 chiffres, Luhn) | 🛑 | |
| Tax_report_setting actif côté PA pour ce SIREN | ⚠️ | Diagnostic au démarrage (déjà vu : si non publié → "Transport not available") |

### 3.2 Identité acheteur
| Contrôle | Niveau | Détail |
|---|---|---|
| Acheteur professionnel détecté en B2C (cf. F8) | 🛑 | Garde-fou : ne pas déclarer en B2C une vente B2B |
| SIREN acheteur valide SI présent (B2B, phase 2) | 🛑 | |
| Pays acheteur = code ISO 3166-1 alpha-2 valide | 🛑 | Détermine B2C (10.3) vs international (10.1) |

### 3.3 Cohérence du document
| Contrôle | Niveau | Détail |
|---|---|---|
| **Σ lignes HT = Total HT** (BT-109) | 🛑 | tolérance 0 (arrondi 2 déc. appliqué partout) |
| **Σ lignes TVA = Total TVA** (BT-110) | 🛑 | |
| **HT + TVA = TTC** (BR-CO-15, BT-112) | 🛑 | ✅ règle FATALE EN 16931 |
| Total passerelle = Total source (`total_bordereau`) | ⚠️ | écart = bug d'extraction probable → signaler |
| Au moins 1 ligne | 🛑 | |
| Numéro de document présent et non déjà émis | 🛑 | anti-doublon via Tracking (F6) |
| Date présente et plausible (pas dans le futur, pas absurde) | 🛑 / ⚠️ | futur = 🛑 ; très ancienne = ⚠️ |
| Devise = ISO 4217 valide | 🛑 | défaut EUR |

> **Précision (2026-06-04 — VAL03)** : le seuil « date invraisemblablement ancienne » (⚠️ alerte de la ligne ci-dessus) est fixé à **antérieure au 1er janvier 2000**, conformément au backlog `orchestration/items/VAL.yaml` (VAL03, « pas avant 2000 »). C'est une borne d'invraisemblance **technique** (date manifestement erronée ou non initialisée), distincte du cas « rattrapage légitime » de la décision #4 (§6) — qui reste une simple alerte sans seuil chiffré. Aucune incidence fiscale (seuil non chiffré au sens TVA/CA). La détection « date dans le futur » (🛑) tolère un jour de marge pour absorber l'écart de fuseau horaire (dates civiles locales d'un ERP français vs date UTC) — voir `StructureRule`.

> **Précision (2026-06-04 — VAL03, réconciliation des totaux)** : la réconciliation HT suit **BR-CO-13** — Total HT (BT-109) = Σ lignes HT (BT-131) − Σ remises document (BG-20) + Σ charges document (BG-21) ; les remises/charges de niveau document (`PivotDocumentChargeDto`, HT) sont donc intégrées. La réconciliation **TVA** (Σ TVA lignes = Total TVA) n'est exécutée **que lorsque le document ne porte aucune charge/remise de niveau document** : leur TVA n'est pas encore résolue en VAL03 (mapping des codes régime source = TVA04), donc l'exécuter produirait un faux positif bloquant sur un document conforme. Le contrôle TVA complet (charges incluses) reprendra avec le mapping (TVA04).

### 3.4 TVA / mapping (lien avec F3)
| Contrôle | Niveau | Détail |
|---|---|---|
| Régime source mappé dans la table TVA | 🛑 | régime inconnu → blocage (jamais d'envoi à l'aveugle) |
| Si catégorie E (exonéré) et taux 0 → **code VATEX présent** | 🛑 | ✅ validé staging : absence = blocage silencieux côté PA |
| Catégorie TVA ∈ énumération valide (S/E/Z/AE/G/K/O…) | 🛑 | |
| Taux cohérent avec la catégorie, 9 catégories UNCL5305 (S/AA/AAA ⇒ taux > 0 ; Z/E/AE/G/K/O ⇒ 0) | 🛑 | BLOCKING — voir amendements 2026-06-02 / 2026-06-04 en tête ; règles BR-S-*/BR-E-*, liste F03 §2.1 |

### 3.5 Avoirs (lien avec F7)
| Contrôle | Niveau | Détail |
|---|---|---|
| Référence facture d'origine présente | 🛑 | sinon avoir orphelin |
| Facture d'origine connue du Tracking et déjà émise | 🛑 | |
| Montants positifs (jamais négatifs sur un 381) | 🛑 | ✅ règle EN 16931 |

### 3.5bis Classification du type source → facture/avoir — table tenant (item RD405, ADR-0004 D3-3)

> **⚠️ AJOUT (2026-06-20 — item RD405, redline ADR-0004 finding RD4-16)** : spécification de la
> classification du *type de document source* (`PivotDocumentDto.SourceDocumentKind`, brut) vers le type
> canonique facture (UNTDID 1001 « 380 ») / avoir (« 381 »). Le mécanisme de validation est livré par
> RD405 ; la PERSISTANCE de la table tenant (provisioning par seed) est l'item de suivi nommé en fin de
> section.

**Problème (finding RD4-16).** L'ADR-0004 D3-3 décide que « la classification facture/avoir vit dans
Validation » : l'adaptateur ne classe PAS (le type source est souvent un signe ou un champ ambigu, propre
à chaque logiciel), il transmet la valeur BRUTE dans `SourceDocumentKind`. Aujourd'hui, un avoir n'est
détecté QUE par son signal STRUCTUREL EN 16931 — la présence d'une référence de facture d'origine
(`CreditNoteRefs`, BG-3 ; `CreditNoteRule`, §3.5). **Trou** : une source qui ne porte la nature « avoir »
que par un drapeau dans son type (`SourceDocumentKind`), SANS aucune référence d'origine (ex. avoir
EncheresV6 sans `no_ba_lettrage`), serait traitée comme une **facture** → signe/sens inversé, envoi faux.

**Règle produit (CLAUDE.md n°2/n°7).** La correspondance « valeur de type source → facture/avoir » N'EST
PAS une règle fiscale universelle : elle **varie par logiciel source** et n'est connue que du déploiement.
Elle est donc du **paramétrage de tenant**, JAMAIS devinée ni codée en dur :

| Élément | Spécification |
|---|---|
| Table | Correspondance tenant `SourceDocumentKind` (chaîne brute de la source) → type canonique ∈ { facture (380), avoir (381) }. 0..n lignes par tenant. |
| Validation métier | La table est **validée par l'expert-comptable du tenant** (comme la table de mapping TVA, F03) avant tout envoi. Aucune correspondance par défaut produit. |
| Valeur NON cartographiée | « non classée » (`Unmapped`) : on ne devine pas. Le repli reste la détection STRUCTURELLE (`CreditNoteRefs`) — comportement inchangé, rétro-compatible. |
| Provisioning | Canal de seed sanctionné `deployments/<client>/` (CLAUDE.md n°7), comme `mapping-tva.json`. Aucune donnée client dans le code ; les exemples du Core sont fictifs. |

**Contrôle ajouté (BLOQUANT, jamais deviné).** Quand la table classe un document en **avoir** mais qu'il
ne porte AUCUNE référence d'origine résoluble (`CreditNoteRefs` vide) :

| Contrôle | Niveau | Détail |
|---|---|---|
| Avoir (classé par type source) sans aucune référence d'origine | 🛑 | `CREDIT_NOTE_KIND_WITHOUT_ORIGIN` — l'avoir est reconnu par sa nature mais son origine n'est pas résoluble : bloqué (aucune référence fabriquée, F07-F08 §B.4). L'opérateur rattache l'origine ou traite manuellement. |

Un document classé **facture** (ou non cartographié) ne produit aucune anomalie de ce contrôle. Une fois
l'origine renseignée (`CreditNoteRefs` non vide), les contrôles d'avoir nominaux de §3.5 (`CreditNoteRule`)
prennent le relais (orphelin, original non émis, montants positifs).

**Périmètre RD405 vs suivi.** RD405 livre le **mécanisme de validation** : l'abstraction tenant-scopée
`ISourceDocumentKindClassifier` (Validation.Contracts), la règle `SourceDocumentKindCreditNoteRule`
(Validation.Domain) qui la consomme, et son enregistrement. Le classificateur par défaut
(`UnconfiguredSourceDocumentKindClassifier`) répond « non classé » partout — état honnête « aucune
correspondance tenant provisionnée », sans invention. **Item de suivi (à planifier, NON couvert par
RD405)** : persistance de la table tenant + import depuis `deployments/<client>/` (extension de
`ImportTenantSeedCommand`, précédent FIX01b `mapping-tva.json`) + implémentation `ISourceDocumentKindClassifier`
adossée à cette table (substituée au défaut par `services.Replace`, précédent SUP03), et — si l'EC doit la
saisir sans IT — un écran de console (avec ses tests bUnit, règle review n°19). Tant que ce suivi n'est pas
livré, le contrôle reste dormant en production (repli structurel `CreditNoteRefs`) ; le mécanisme est
néanmoins **câblé et testé** (la règle tourne sur chaque document, le chemin bloquant est couvert).

## 4. Algorithmes (❓ à confirmer que les PA les appliquent, mais sans risque à les implémenter)

### 4.1 SIREN / SIRET — clé de Luhn
> ⚠️ La recherche n'a **pas pu confirmer sur source primaire** que les PA appliquent Luhn (vs simple présence à l'annuaire). Mais l'implémenter est trivial et sans risque — un SIREN qui échoue Luhn est forcément absent de l'annuaire.

- **SIREN** : 9 chiffres, algorithme de Luhn standard.
- **SIRET** : 14 chiffres, Luhn standard.
- **Exception connue à coder** : **La Poste** (SIREN 356000000) ne respecte pas Luhn → cas particulier documenté à autoriser.

### 4.2 N° TVA intracommunautaire français
- Format `FR` + clé (2 car.) + SIREN (9 chiffres).
- Clé = `(12 + 3 × (SIREN mod 97)) mod 97`.
- Contrôle : cohérence clé + le SIREN intégré = SIREN émetteur.

### 4.3 Arrondi
- **2 décimales partout**, arrondi commercial (half-up). Appliqué à l'extraction (montants flottants sales) ET vérifié à la validation.
- **Pas de tolérance** sur la réconciliation des totaux (EN 16931). Si la source produit une incohérence d'arrondi → c'est un blocage, pas un rattrapage silencieux.

## 5. Résultat d'un contrôle (structure)

```csharp
public sealed class ValidationResult
{
    public bool IsBlocking { get; }              // au moins un contrôle 🛑 échoué
    public IReadOnlyList<ValidationIssue> Issues { get; }
}
public sealed class ValidationIssue
{
    public string Code { get; }                  // ex. "DOC_TOTAL_MISMATCH", "VATEX_MISSING"
    public ValidationSeverity Severity { get; }  // Blocking | Warning
    public string OperatorMessage { get; }       // message en français, actionnable
    public string TechnicalDetail { get; }       // pour le journal/audit
    public string FieldRef { get; }              // BT-xxx ou champ pivot concerné
}
```

**Exemples de messages opérateur (pas de jargon) :**
- `DOC_TOTAL_MISMATCH` → « Le total du bordereau (1 162,80 €) ne correspond pas à la somme des lignes (1 160,00 €). Vérifiez le bordereau n° 2019 dans Enchères SVV. »
- `VATEX_MISSING` → « Une ligne est exonérée de TVA mais le motif d'exonération n'est pas déterminé (régime 12 non paramétré). Complétez la table des régimes de TVA. »
- `BUYER_LOOKS_PROFESSIONAL` → « L'acheteur "MARTIN SAS" semble être un professionnel. Une facture électronique B2B est requise (non gérée automatiquement). Traitez ce bordereau manuellement ou confirmez qu'il s'agit d'un particulier. »

## 6. Décisions à valider ensemble

| # | Décision | Options | Recommandation |
|---|---|---|---|
| 1 | Embarquer un moteur Schematron EN 16931 en V1 ? | oui (validation XML complète) / non (règles métier + on laisse B2Brouter valider le XML) | **Non en V1** — B2Brouter génère et valide le XML ; on couvre les règles métier essentielles. Réévaluer si taux de rejet élevé |
| 2 | Récupérer les artefacts FNFE-MPE (BR-FR-*) ? | maintenant / phase 2 | **Documenter la source maintenant**, intégration phase 2 (B2B) où le risque XML français est réel |
| 3 | Niveau du contrôle "total passerelle ≠ total source" | bloquant / alerte | **alerte** (peut être légitime : arrondi source), mais journalisé |
| 4 | Date très ancienne (ex. > 1 an) | bloquant / alerte | alerte (cas de rattrapage légitime) |
| 5 | Où trouver le jeu codifié complet des motifs de rejet DGFiP (AFNOR XP Z12-012 v1.3, ~45 codes) | — | à extraire de la norme primaire pour mapper nos codes internes sur les leurs (phase 2) |
