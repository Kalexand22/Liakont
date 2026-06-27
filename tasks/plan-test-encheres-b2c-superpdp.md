# Plan de test E2E — e-reporting B2C enchères → SuperPDP sandbox (env Isatech)

Branche : `feat/ereporting-b2c` (HEAD `135ccb52` + merge aiguillage + fix factures + seed).
Env : plateforme Isatech relancée (base + realm vierges, Host rebuildé depuis la branche).
PA : **SuperPDP Sandbox** (`https://api.superpdp.tech`) — POST réels. ⚠️ chaque envoi = une **vraie ligne** sandbox.

## 0. Ce qu'on prouve (un parcours, 6 sorties)
La même base source EncheresV6 produit, via l'agent (lecture seule) → plateforme → SuperPDP :
| Flux | Doc source (exemple réel) | Marquage | Sortie SuperPDP |
|---|---|---|---|
| **Marge** | BA `100022` (régime 6, acheteur particulier) | TMA1 | `b2c_transactions` agrégé (TVA sur la marge) |
| **Prix total taxable** | BA régime `5`, acheteur particulier | TLB1 | `b2c_transactions` agrégé |
| **Export** | BA `code_export=1` (hors UE / CEE / FR) | TLB1/TNT1 | `b2c_transactions` **unitaire** taux 0 |
| **Facture client** | `00100004` (DIV 2000 @20 %, dossier 2) | **TPS1** (nature tenant) | `b2c_transactions` agrégé |
| **Note d'honoraires** | `100008` (hono 23 @20 %, dossier 2) | **TPS1** | `b2c_transactions` agrégé |
| **B2B (aiguillage)** | acheteur à **SIREN** | — (jamais e-reporté) | **voie document** (Factur-X / PDP B2B), pas e-reporting |
| *(contrôle fail-closed)* | note `100004` (frais 0 % + hono 20 % = mixte) | — | **bloquée** au CHECK (taux 0 non mappé) |

## 1. Pré-requis (à vérifier AVANT)
- [ ] **Plateforme à jour** : le Host a été rebuildé par le `reset` depuis `feat/ereporting-b2c` → l'aiguillage B2B + flux #7 + fix factures sont dedans. (Fait.)
- [ ] **Agent recompilé** : `dotnet build agent/Liakont.Agent.sln -c Release` — sinon l'extraction factures/notes n'est pas dans le binaire net48.
- [ ] **Credentials SuperPDP sandbox** disponibles (les tiens, OAuth2 : `accountId`, `clientId`, `clientSecret` — ceux du POST marge prouvé id 585/591).
- [ ] **Driver ODBC 17 for SQL Server** + base source montée.

## 2. Étapes (tu exécutes, je guide)

### A. Base source propre
```
powershell -ExecutionPolicy Bypass -File deployments\encheres-demo\demo.ps1 source
```
→ remonte la base `EncheresV6_Demo` (schéma `enc`) + le login lecture seule + l'injection SIREN démo (quelques acheteurs pro pour démontrer l'aiguillage B2B). Idempotent.

### B. Console — créer le tenant (commencer par UN seul : **volontaire / dossier 2**, le plus riche)
1. Login `sysadmin / Test@1234` (1er login : changement MDP + 2FA).
2. *Clients* → *Nouveau client* → tenant **volontaire** (provisioning : base + migrations + `company_id`).

### C. Console — paramétrer le tenant (source de vérité : `deployments/encheres-demo/tenant-seed/volontaire/`)
1. **Paramétrage fiscal** :
   - **Nature d'opération = `PrestationServices`** (pilote le TT-81 des documents ordinaires → TPS1 ; les bordereaux dérivent leur TT-81 de leur propre job, inchangés).
   - **Table de mapping TVA** — saisir les **6 règles** (`tenant-seed/volontaire/mapping-tva.json`) :
     `5`→S 20 %, `6`→E+VATEX-EU-J (bordereaux) ; `EXP_HORSUE`→G/0, `EXP_CEE`→K/0, `EXP_FR`→G/0 (export) ; `20`→S 20 %, `5.5`→S 5,5 % (factures/notes — clé = **taux**).
   - **⚠️ VALIDER la table** (action opérateur) : sans `validatedDate`, la garde **PIP01 refuse tout envoi réel** vers SuperPDP. C'est l'étape qui débloque les POST réels.
2. **Plateforme Agréée** → ajouter un compte **SuperPDP** :
   - environnement **Sandbox**, `accountId` + `clientId` + `clientSecret` (tes creds OAuth2 — chiffrés au coffre du tenant).
   - vérifier la capacité affichée : **e-reporting B2C ✅** (`SupportsB2cReporting=true`).
3. **Publier le SIREN** (action d'onboarding) → rend l'envoi exerçable.
4. **Enrôler un agent** → noter sa **clé API**.

### D. Agent — configurer + lancer (dossier 2)
```
powershell -ExecutionPolicy Bypass -File deployments\encheres-demo\demo.ps1 agent-config
```
- chiffrer (sur le poste) la chaîne ODBC + la clé API (`Liakont.Agent.Cli.exe encrypt`), les coller dans `deployments/encheres-demo/agent/volontaire/agent.json`.
- vérifier `adapterConfig.EncheresV6` : `schema = "enc"`, `dossier = "2"`.
- **⚠️ période d'extraction LARGE** (les factures/notes vont de 2002 à 2024) — sinon rien ne sort.
- lancer le service agent → il extrait BA + BV + **factures + notes**, pousse vers `/api/agent/v1/documents/batch`.

### E. Déclencher les jobs (console → admin des planifications du socle)
Déclencher (ou attendre la planification) les jobs système d'e-reporting B2C :
- `AggregateB2cMarginAll` (TMA1), `AggregateB2cTaxableAll` (TLB1), `AggregateB2cExportAll` (export), **`AggregateB2cPlainTaxableAll` (factures/notes TPS1)**.
- + le `SendAll` pour la voie document (B2B Factur-X).

## 3. Observation / résultats attendus
- **Console `/emissions-marge-b2c`** (titre « e-reporting B2C ») : lignes Pending→Émis, **colonne Catégorie** = TMA1 / TLB1 / **TPS1**, id PA SuperPDP retourné, nb de pièces.
- **`/traitements`** + **`/documents`** : les bordereaux marge/taxable/export différés de la voie document (D1) ; les factures/notes marquées 10.3 ; le doc à SIREN parti en voie document.
- **SuperPDP sandbox** : nouvelles lignes `b2c_transactions` (réelles). Vérifier le lien **doc ↔ reporting** (un e-reporting → retrouver ses pièces).
- **Fail-closed** : la note mixte `100004` et les régimes non mappés → **bloqués** au CHECK avec verdict opérateur (pas d'envoi faux).
- **Idempotence** : relancer un job → **aucun** nouveau POST (attempt-once par document, journal `b2c_margin_emissions`).

## 4. ⚠️ Garde-fous envoi réel
- Anti-doublon **produit** obligatoire (SuperPDP ne dédoublonne PAS côté serveur : 2 POST = 2 lignes). La garde attempt-once (Pending écrit AVANT le POST) le couvre.
- 1ʳᵉ exécution = **vraies lignes** sandbox. Pour limiter le bruit : tester d'abord **1 tenant / 1 période courte** ciblant `00100004` + `100008` + `100022`, observer, puis élargir.
- La voie document B2B exige un **canal** sur la PA (SuperPDP `SupportsB2bInvoicing=true` ✅) — sinon le doc à SIREN est **maintenu** (jamais dégradé en B2C anonyme — garde aiguillage).

## 5. Puis tenant **judiciaire** (dossier 1)
Répéter B→E avec `dossier = "1"` et `tenant-seed/judiciaire/` (notes d'inventaire judiciaires plus nombreuses → TPS1).

---
**Points où ta connaissance console comble** : libellé exact de l'action « valider la table », champs du compte SuperPDP, écran d'admin des planifications. Je guide commande par commande sur les étapes shell (A, D) ; toi sur la console (B, C, E).
