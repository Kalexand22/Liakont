# F12-A — Paramétrage par tenant (annexe de F12)
### Document de conception — modèle détaillé du niveau de configuration « Tenant »

> Statut : 🟨 conception interne (lot CFG, item CFG01 — 2026-06-04). À revoir ensemble.
> Annexe de [F12 §6](F12-Architecture-Plateforme-Agent.md) (« Configuration et déploiement »),
> qui définit les **trois niveaux** de configuration (Instance / Tenant / Agent). Ce document
> détaille **uniquement le niveau Tenant** : ce qu'il contient, qui l'édite, comment il est
> consommé, et comment il est semé (`deployments/<client>/`).
>
> **Règle de rédaction (CLAUDE.md n°2, blueprint §11) :** aucune règle fiscale n'est inventée
> ici. Chaque valeur trace vers [F09](F09-E-Reporting-Paiement.md), [F12](F12-Architecture-Plateforme-Agent.md),
> `blueprint.md` ou `tasks/decisions.md`. Tout point fiscal non tranché par une source est
> marqué **« À TRANCHER »** et renvoyé à l'expert-comptable du client — jamais deviné.
>
> Implémenté par **CFG02** (entités + handlers + chiffrement). Consommé par **PIP03** (pipeline
> e-reporting), **SUP01** (alertes de supervision), **WEB01** (affichage échéance), **OPS03**
> (provisioning / import de seed), **AGT03** (planification poussée à l'agent).

---

## 1. Positionnement : le niveau Tenant parmi les trois

Rappel de [F12 §6.1](F12-Architecture-Plateforme-Agent.md) — les trois niveaux :

| Niveau | Où | Qui modifie |
|---|---|---|
| **Instance** | `appsettings` / variables d'environnement (Docker) | Opérateur de l'instance (déploiement) |
| **Tenant** | **en base, par tenant, édité via la console** | Droit « paramétrage » (journalisé, revalidation) |
| **Agent** | Fichier local + DPAPI chez le client | Installateur / CLI agent |

Cette annexe détaille la colonne **Tenant**. Principes structurants (blueprint §7) :

1. **1 tenant = 1 client final**, identifié par son **SIREN** (clé fonctionnelle).
2. **Isolation physique par tenant** (database-per-tenant, héritée du socle Stratum). Tout le
   paramétrage tenant vit dans la base **du tenant**, jamais en cross-tenant.
3. **Le paramétrage est édité depuis la console** (droit « paramétrage »), **journalisé**
   (module Audit, append-only) et — pour la table TVA — **revalidé** par l'expert-comptable.
4. **Aucune donnée client dans le code** (CLAUDE.md n°7) : le paramétrage réel est en base
   (édité console) et/ou en **seed versionné** dans `deployments/<client>/` (§8). Le code et
   `config/exemples/` n'embarquent que des **exemples fictifs**.

---

## 2. Profil du tenant

Identité légale et administrative du client final.

| Champ | Type | Obligatoire | Notes / source |
|---|---|---|---|
| `siren` | string (9 chiffres) | **oui** | Clé fonctionnelle du tenant (blueprint §7.1). Validé **Luhn** (règle VAL02 ; CFG02 réutilise VAL02 si disponible, sinon duplication temporaire documentée). |
| `raisonSociale` | string | oui | Dénomination légale. |
| `adresse` | structure (rue, code postal, ville, pays ISO 3166-1 alpha-2) | oui | Émetteur des flux (cf. `PivotPartyDto`, F01-F02). |
| `contactEmailAlerte` | string (email) | **non** | Destinataire des alertes critiques côté tenant (F12 §5.3). Optionnel : si absent, seules les alertes vers l'opérateur d'instance partent. |
| `statut` | enum `{ Actif, Suspendu }` | oui, défaut `Actif` | `Suspendu` = paramétrage conservé, aucune extraction/transmission active (distinct de la fin de vie OPS06, qui est irréversible). |

> Le profil est édité via la console (droit `liakont.settings`) ; toute mutation est journalisée
> avec l'identité de l'opérateur (§7).

---

## 3. Paramétrage fiscal du tenant

**Les décisions de cette section appartiennent à l'expert-comptable du client** (blueprint §11,
décisions D2/D4 du 2026-06-03). Le produit ne tranche jamais à sa place.

### 3.1 Règle transverse : `null` = décision en attente = suspension

> **Tout paramètre fiscal `null` signifie « décision de l'expert-comptable en attente ».**
> Conséquence : **les transmissions concernées sont suspendues**, avec un **message opérateur
> explicite** (numéro de tenant + action corrective — CLAUDE.md n°12). **Jamais de valeur
> par défaut, jamais de cadence/règle devinée** (décision D4 ; même mécanique que `vatOnDebits`).

Cette section est consommée par **PIP03** (le pipeline e-reporting) : un paramètre fiscal
manquant suspend la transmission au lieu de l'envoyer avec une hypothèse.

### 3.2 Les champs

| Champ | Type | `null` ⇒ | Source |
|---|---|---|---|
| `vatOnDebits` | `bool?` | E-reporting de paiement **suspendu** | F09 amendement #2, F09 §6 #1 |
| `operationCategory` | enum? `{ LivraisonBiens, PrestationServices, Mixte }` | Transmissions dont l'exigibilité dépend de la nature **suspendues** | F01-F02 (`OperationCategory`), F09 §1 |
| `reportingFrequency` | enum? (cadence déclarative — voir §3.3) | **Pas de calcul d'échéance** (SUP01 J-3, WEB01) + e-reporting paiement **suspendu** | F09 §2, décision D4 |

**Détails :**

- **`vatOnDebits`** — Option pour la TVA sur les débits. Si `true`, l'exigibilité est à la
  **facturation** : il n'y a **pas d'e-reporting de paiement** (F09 §2, §6 #1). Si `false`,
  l'exigibilité des prestations de services est à l'**encaissement** (e-reporting paiement dû).
  Si `null` : suspension (la question « le client a-t-il opté pour la TVA sur les débits ? » est
  une **question fiscale prioritaire**, F09 §6 #1 — A3 de l'index conception).

- **`operationCategory`** — Nature de l'opération du flux. `Mixte` (cas du bordereau d'enchères :
  adjudication = livraison de biens + frais = prestation de services) déclenche l'e-reporting
  de paiement **sur la part frais** (F09 §1). Valeurs issues de F01-F02 ; ne jamais en inventer
  d'autre.

- **`reportingFrequency`** — Cadence déclarative pilotant l'**échéance** de transmission.
  Consommée par **SUP01** (alerte « échéance à J-3 ») et **WEB01** (affichage de la prochaine
  échéance). Tant qu'elle est `null`, **aucune échéance n'est calculée** (ni alerte, ni
  affichage — jamais de cadence devinée). **L'énumération exacte est un point à trancher : voir §3.3.**

### 3.3 `reportingFrequency` — ⚠️ POINT À TRANCHER (énumération et modélisation)

La décision **D4 (2026-06-03)** a acté le **champ** `reportingFrequency` (nullable, sémantique
de suspension ci-dessus) en citant comme source **F09 §2**. Mais l'énumération exacte n'est pas
cohérente entre la décision et sa source citée, et doit être confirmée **avant que CFG02 ne fige
le type** :

- **D4 (texte de la décision)** propose les valeurs **`{ décadaire, mensuelle, trimestrielle }`**.
- **F09 §2** (la source citée — décret n° 2022-1299, section C) établit en réalité un mapping
  **régime → fréquence de transmission**, dont la colonne *fréquence* ne contient **pas**
  « trimestrielle » et contient « bimestrielle » :

  | Régime TVA du client | **Fréquence de transmission** | Délai |
  |---|---|---|
  | Réel normal mensuel | **décadaire** (3×/mois) | J+10 après chaque décade |
  | Réel normal trimestriel | **mensuelle** | avant le 10 du mois suivant |
  | Réel simplifié | **mensuelle** | entre le 25 et le 30 du mois suivant |
  | Franchise en base | **bimestrielle** | entre le 25 et le 30 du mois suivant la période |

  > Source : F09 §2 (🔶 fiche DGFiP / décret n° 2022-1299, non re-confirmée sur source primaire —
  > voir l'avertissement de méthode de F09 et A1/A3 de l'index conception).

**Écart constaté :** « trimestriel » dans D4 correspond à un **régime** (« réel normal
trimestriel ») dont la **fréquence de transmission est mensuelle** — ce n'est pas une fréquence.
La fréquence **bimestrielle** (franchise en base) est, elle, absente de D4.

**À trancher avec l'expert-comptable (ne pas deviner) :**

1. **Le champ stocke-t-il le RÉGIME du client** (`{ RéelNormalMensuel, RéelNormalTrimestriel,
   RéelSimplifié, FranchiseEnBase }`) — d'où le produit **dérive** fréquence + délai via la table
   F09 §2 — **ou directement la FRÉQUENCE** (`{ Décadaire, Mensuelle, Bimestrielle }`) ?
   La première option est la plus robuste (le régime est la caractéristique légale ; le délai en
   découle) et évite la perte d'information sur le délai dont SUP01 a besoin pour J-3.
2. **Réconcilier D4 et F09 §2** : retirer « trimestrielle » (régime, pas fréquence) et statuer
   sur « bimestrielle ». **Tant que ce point n'est pas tranché, CFG02 ne fige pas l'énumération**
   (champ présent, valeurs candidates documentées, type final confirmé en début de CFG02).
3. **Régime effectif du client concerné** : question fiscale ouverte (F09 §6 #2, A3 de l'index).

> Ce point est volontairement laissé ouvert : choisir une valeur ici **serait inventer une règle
> fiscale** (CLAUDE.md n°2). La sémantique `null = suspension` garantit qu'aucune transmission ne
> part sur une cadence supposée tant que la décision n'est pas prise.

### 3.4 Mentions de facturation (facture B2B)

> **Note d'amendement (2026-06-27, BUG-26).** Ces mentions sont des **données de l'entreprise**
> (conditions générales de vente), pas des règles fiscales : le produit **n'en invente jamais le
> contenu** (CLAUDE.md n°2/7). Elles sont **saisies par le client / son expert-comptable** depuis la
> console, **surchargeables par document** (la valeur portée par le pivot prime sur le défaut tenant).

Le converter **CTC-FR** d'une PA (Super PDP, profil franco-français) **exige** sur une **facture B2B**
trois mentions légales obligatoires entre professionnels (C. com. art. L441-9 / L441-10) et, pour un
montant dû positif, des **termes de paiement** (EN 16931 BR-CO-25). Faute de quoi le document est
**rejeté** (recette BUG-26). Ces mentions sont consommées par la **génération Factur-X**
([F16 §3.5](F16-FacturX-Generation.md)) et par `SuperPdpPayloadBuilder` (schéma `en_invoice` :
`payment_terms`, `notes[]`), injectées dans le pivot au *read-time* par `PivotEmitterEnricher`.

| Champ | Type | Élément cible | `null` ⇒ |
|---|---|---|---|
| `paymentTerms` | `string?` | BT-20 (`SpecifiedTradePaymentTerms/Description`) | satisfait BR-CO-25 quand le montant dû est positif (sinon échéance BT-9 du pivot) |
| `latePenaltyTerms` | `string?` | note BT-22, `SubjectCode` **PMD** (pénalités de retard) | mention BR-FR-05 manquante |
| `recoveryFeeTerms` | `string?` | note BT-22, `SubjectCode` **PMT** (indemnité forfaitaire de recouvrement) | mention BR-FR-05 manquante |
| `discountTerms` | `string?` | note BT-22, `SubjectCode` **AAB** (escompte ou son absence) | mention BR-FR-05 manquante |

**Mécanique de sûreté (même esprit que §3.1).** Sur une **facture B2B FR** (voie document, acheteur
identifié SIREN), une mention requise absente ⇒ **document bloqué** au CHECK avec **message opérateur**
(numéro de document + action corrective : « Renseignez les mentions de facturation dans Paramètres » —
CLAUDE.md n°12), **jamais un envoi voué au rejet** (CLAUDE.md n°3). Une fois saisies, le re-CHECK
débloque (même mécanique que le mapping TVA / le SIREN). Le **B2C** (e-reporting agrégé, pas de Factur-X
par document) n'est **pas** concerné. Codes UNTDID 4451 et obligation **non inventés** : imposés par le
verdict du converter CTC-FR.

> ⚠️ Ces mentions sont du **paramétrage client**, pas une décision fiscale ouverte : elles ne figurent
> donc pas au récapitulatif §9 (points à trancher par l'expert-comptable). Un déploiement les fournit
> via le seed (`tenant-profile.json`, §8) ou la console.

---

## 4. Comptes Plateforme Agréée (PA) du tenant

Un tenant possède **un ou plusieurs** comptes PA (ex. staging **et** production, ou multi-PA).

| Champ | Type | Notes / source |
|---|---|---|
| `pluginType` | string (identifiant de plug-in PA) | Le **type de plug-in** (ex. `B2Brouter`, `SuperPdp`, `Fake`), pas un flag produit. Le comportement reste piloté par les **capacités déclarées** du plug-in (`PaCapabilities`), jamais par `if (pa is …)` (CLAUDE.md n°8). |
| `environment` | enum `{ Staging, Production }` | Permet staging + production pour le même PA. |
| `accountIdentifiers` | structure (selon le plug-in) | Identifiants de compte côté PA (non secrets). |
| `apiKey` | secret **CHIFFRÉ** | **Jamais en clair** en base, dans les logs, ni dans les réponses d'API (CLAUDE.md n°10, blueprint §7). Chiffrement via **ASP.NET Core Data Protection** (clés de protection persistées par instance — CFG02 ; stockage des clés DP documenté à l'appliance OPS01). |
| `actif` | bool | Un compte désactivé n'est plus utilisé pour l'envoi. |

> **Frontière de généricité (CLAUDE.md n°6/8) :** le paramétrage porte le *type* de plug-in et
> ses identifiants ; aucune fonctionnalité produit n'est conditionnée à un PA concret. Le module
> `Transmission` ne référence jamais un plug-in PA précis.

---

## 5. Planification d'extraction du tenant

| Champ | Type | Notes / source |
|---|---|---|
| `schedule` | liste d'heures (ex. `["03:00"]`) | Heures de déclenchement des runs d'extraction de l'agent. |
| `catchUpOnStart` | bool | Rattrapage au démarrage de l'agent (F12 §2.2/§2.4). |

**Pilotage :** la planification **effective** est **poussée vers l'agent via le heartbeat**
(`AgentConfigurationDto`, F12 §2.4/§3.2, implémentée côté plateforme par **AGT03**). Décision
**D3 (F12 §7 #3)** : **la plateforme est prioritaire** (pilotage centralisé) ; le fichier local
de l'agent n'est qu'un défaut/secours.

---

## 6. Seuils d'alerte du tenant (supervision)

Consommés par **SUP01** (supervision proactive — dead-man's switch, F12 §5). **Paramétrables par
tenant**, avec des **valeurs par défaut produit** (F12 §5.2) :

| Règle | Champ | Seuil par défaut | Gravité (F12 §5.2) |
|---|---|---|---|
| Agent muet (aucun heartbeat) | `agentSilentHours` | 24 h | 🔴 Critique |
| Run d'extraction manqué | `missedRunHours` | 36 h | 🔴 Critique |
| File de push qui grossit | `pushQueueMaxItems` / `pushQueueMaxAgeHours` | 50 éléments / 6 h | 🟠 Avertissement |
| Documents bloqués non traités | `blockedDocumentsDays` | 5 jours | 🟠 Avertissement |
| Rejets PA non traités | `paRejectionsDays` | 2 jours | 🔴 Critique |
| Échéance déclarative proche, documents non transmis | (dérivé de `reportingFrequency` — §3.3) | J-3 | 🔴 Critique |
| Version d'agent obsolète (< N-1) | (politique d'instance) | — | 🟠 Avertissement |

| Champ | Type | Notes |
|---|---|---|
| `alertTenantContact` | bool | Active l'envoi des **alertes critiques** au `contactEmailAlerte` du profil (§2). Les alertes vers l'opérateur d'instance ne dépendent pas de ce flag (F12 §5.3). |

> L'alerte « échéance à J-3 » nécessite `reportingFrequency` (§3.3) : tant qu'elle est `null`,
> cette alerte **n'est pas calculée** (pas d'échéance ⇒ pas de J-3).

---

## 7. Qui édite quoi : droits par section

Le modèle d'autorisation est défini dans
[`docs/architecture/identity-permissions-liakont.md`](../architecture/identity-permissions-liakont.md).
Le paramétrage tenant relève de la permission **`liakont.settings`** (rôle `parametrage`), sauf
la table TVA qui ajoute un **workflow de validation** propre.

| Section de paramétrage | Permission requise | Workflow particulier |
|---|---|---|
| Profil du tenant (§2) | `liakont.settings` | Journalisé (identité opérateur). |
| Paramétrage fiscal (§3) | `liakont.settings` | Journalisé. `null` = suspension (§3.1). |
| Comptes PA (§4) | `liakont.settings` | Journalisé ; secret jamais exposé en lecture. |
| Planification (§5) | `liakont.settings` | Journalisé ; poussée à l'agent (AGT03). |
| Seuils d'alerte (§6) | `liakont.settings` | Journalisé. |
| **Table TVA** (lot TVA — paramétrage par tenant) | `liakont.settings` | **+ validation expert-comptable** : toute modification **invalide la validation** ⇒ **revalidation requise** (workflow TVA05, blueprint §7.5, décision 2026-06-02). C'est un niveau de contrôle **distinct** du profil/seuils. |

**Journalisation (CLAUDE.md n°4) :** toute mutation du paramétrage est consignée dans le module
**Audit** (append-only) avec l'**identité de l'opérateur**, l'horodatage et l'avant/après. Aucun
chemin d'update/delete sur le journal.

> Consultation seule = `liakont.read` (rôle `lecture`). Les vues cross-tenant restent réservées
> au module **Supervision** en lecture seule (`liakont.supervision`) — CLAUDE.md n°9, blueprint §7.3.

---

## 8. Seed de déploiement : `deployments/<client>/`

Le paramétrage d'un tenant peut être **versionné en seed** lorsqu'il doit être reproductible
(rejouable à l'identique sur une nouvelle instance, audité, réversible par tenant) — cf.
[`deployments/README.md`](../../deployments/README.md).

### 8.1 Structure cible

```
deployments/<client>/
├─ tenant-profile.json     # Profil (§2) + paramétrage fiscal (§3) + planification (§5) + seuils (§6)
├─ tva-table.csv           # Table TVA du tenant (codes régime source → catégorie/taux/VATEX) — lot TVA
├─ pa-accounts.json        # Comptes PA (§4) avec secrets en PLACEHOLDER (jamais en clair)
└─ fixtures/               # Jeux de données fictifs de démo/recette (facultatif)
```

- **Aucun secret en clair** dans un fichier versionné (CLAUDE.md n°10) : `pa-accounts.json` ne
  porte que des **placeholders** ; la clé API réelle est saisie depuis la console (chiffrée en
  base, §4) ou injectée au déploiement. Un SIREN réel, une table TVA réelle, une chaîne ODBC ou
  un compte PA réel **hors de `deployments/`** est un **P1** (CLAUDE.md n°15).
- Le seed d'un client concret (ex. `deployments/cmp/`) est produit par le **lot CMP**, pas par
  le socle. Les exemples **fictifs** vivent dans
  [`config/exemples/`](../../config/exemples/README.md).

### 8.2 Sémantique de l'import

- **Opération** : `ImportTenantSeed(deployments/<client>/)` (handler CFG02), consommée par le
  **provisioning OPS03** (« Créer un tenant », F12 §6.3).
- **Idempotence** : l'import **crée ou met à jour** le profil et son paramétrage (rejouable).
- **Secrets** : l'import **n'écrit jamais** un secret en clair ; les placeholders restent à
  compléter via la console (saisie chiffrée). La table TVA importée **n'est pas pré-validée** :
  elle suit le workflow de validation expert-comptable (§7).
- **Réversibilité** : l'export console (tracking + archive + paramétrage) produit le dossier
  remis au client (F12 §6.3) — symétrique de l'import.

---

## 9. Récapitulatif des points à trancher (expert-comptable — ne jamais deviner)

| # | Point ouvert | Effet tant que non tranché | Source |
|---|---|---|---|
| 1 | `reportingFrequency` : régime vs fréquence, « trimestrielle » (D4) vs « bimestrielle » (F09 §2) | Champ présent, énumération **non figée** ; `null` ⇒ pas d'échéance + e-reporting paiement suspendu | §3.3, F09 §2/§6, D4, A3 |
| 2 | Régime TVA effectif du client | `reportingFrequency = null` ⇒ suspension | F09 §6 #2, A3 |
| 3 | Option TVA sur les débits du client | `vatOnDebits = null` ⇒ e-reporting paiement suspendu | F09 §6 #1, A3 |
| 4 | `operationCategory` (Mixte ?) du flux | `null` ⇒ transmissions dépendantes suspendues | F09 §6 #3, F01-F02 |
| 5 | Méthode d'imputation des frais (lettrage) | Paramètre de tenant validé par l'expert-comptable ; non renseigné ⇒ e-reporting paiement suspendu (PIP03) | F09 §5.2 amendement, décision D4 |

> Tous ces points suivent la **même mécanique de sûreté** : `null`/non validé ⇒ **suspension +
> message opérateur**, jamais d'hypothèse codée en dur (CLAUDE.md n°2/3).

---

## 10. Traçabilité des sources

| Élément du paramétrage | Source |
|---|---|
| Trois niveaux Instance/Tenant/Agent | F12 §6.1 |
| 1 tenant = 1 SIREN, isolation, journalisation, revalidation | blueprint §7 ; décision 2026-06-02 (paramétrage console) |
| `vatOnDebits`, `operationCategory`, `reportingFrequency` (nullable, suspension) | F09 §1/§2/§6 + amendements ; décisions D2, D4 |
| Mentions de facturation (§3.4 : BT-20, notes PMD/PMT/AAB) | F16 §3.5 ; converter CTC-FR (BR-CO-25 / BR-FR-05) ; C. com. L441-9/L441-10 ; BUG-26 (2026-06-27) |
| Table régime → fréquence → délai | F09 §2 (décret n° 2022-1299) |
| Comptes PA, chiffrement des secrets | F12 §6.1 ; blueprint §7 ; CLAUDE.md n°10 ; décision D9 (capacités PA) |
| Généricité PA (pas de `if (pa is …)`) | CLAUDE.md n°6/8 ; décision 2026-06-02 |
| Planification poussée à l'agent, plateforme prioritaire | F12 §2.4/§3.2/§7 #3 (D3) ; AGT03 |
| Seuils d'alerte + valeurs par défaut | F12 §5.2/§5.3 |
| Droits par section (`liakont.settings`, etc.) | identity-permissions-liakont.md ; blueprint §6 |
| Table TVA = workflow de validation distinct | blueprint §7.5 ; décision 2026-06-02 ; TVA05 |
| `deployments/<client>/`, import, réversibilité | deployments/README.md ; config/exemples/README.md ; F12 §6.3 ; OPS03 |
