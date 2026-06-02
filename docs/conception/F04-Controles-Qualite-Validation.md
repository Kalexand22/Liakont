# F4 — Contrôles de qualité / Validation
### Document de conception — Gateway.Core/Validation

> Statut : 🟨 issu de la deep research DR3 (2026-06-02) + connaissances projet. À revoir ensemble.
> Légende : ✅ confirmé source primaire · 🔶 probable · ❓ à confirmer/décider

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

### 3.4 TVA / mapping (lien avec F3)
| Contrôle | Niveau | Détail |
|---|---|---|
| Régime source mappé dans la table TVA | 🛑 | régime inconnu → blocage (jamais d'envoi à l'aveugle) |
| Si catégorie E (exonéré) et taux 0 → **code VATEX présent** | 🛑 | ✅ validé staging : absence = blocage silencieux côté PA |
| Catégorie TVA ∈ énumération valide (S/E/Z/AE/G/K/O…) | 🛑 | |
| Taux cohérent avec la catégorie (S ⇒ taux > 0 ; E ⇒ 0) | ⚠️ | 🔶 règles BR-S-*/BR-E-* |

### 3.5 Avoirs (lien avec F7)
| Contrôle | Niveau | Détail |
|---|---|---|
| Référence facture d'origine présente | 🛑 | sinon avoir orphelin |
| Facture d'origine connue du Tracking et déjà émise | 🛑 | |
| Montants positifs (jamais négatifs sur un 381) | 🛑 | ✅ règle EN 16931 |

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
