# Passerelle de conformité e-invoicing / e-reporting pour logiciels legacy
### Document de conception produit — base de travail

> Créé le 2026-06-01. Statut : **squelette à détailler** — chaque fonctionnalité (section 6) sera creusée une par une lors des prochaines sessions de travail.
> Documents liés : analyses de marché (ce répertoire), `RECAP-Test-B2Brouter-eReporting-DGFiP.md`, `Analyse-Donnees-V1-Mapping-TVA.md`, `Intégration de l'API B2Brouter eDocExchange....md`.

---

## 1. Positionnement produit

**Le produit n'est PAS un connecteur pour logiciels de ventes aux enchères.** C'est une **passerelle de conformité facturation électronique pour tout logiciel legacy non-API** : tout progiciel métier ancien (Magic XPA, AS400, Access, Windev, Delphi, vieux SQL Server…) incapable de dialoguer nativement avec une Plateforme Agréée.

- **Statut réglementaire** : Solution Compatible (SC) — non agréée, adossée à une PA partenaire (B2Brouter en premier) qui porte le routage, l'interopérabilité, l'archivage et la déclaration.
- **Principe directeur** : un **cœur générique** (~90 % du code, réutilisable) + un **adaptateur par logiciel source** (~10 %, spécifique).
- **Premier adaptateur** : EncheresV6 (Magic XPA / Pervasive) — financé par le projet CMP, sert de démo et d'étude de cas. Les ventes aux enchères sont le premier vertical, pas le marché.
- **Cible de déploiement** : on-premise uniquement, chez le client ou son hébergeur. Pas de SaaS, pas de multi-tenant, pas de web. Simple, adapté au monde legacy.

### Nom du produit — DÉCISION OUVERTE
"EncheresConnect" rejeté (trop spécifique au vertical enchères). Le nom doit refléter la généricité legacy. Candidats à discuter :
- (à proposer / brainstormer — critères : générique, sérieux, compréhensible par un DSI/expert-comptable, pas anglophone obligatoire)

Dans ce document, le produit est désigné par "**la Passerelle**" et les projets de code par le préfixe neutre `Gateway.*` (à renommer quand le nom sera choisi).

---

## 2. Contexte stratégique (état au 2026-06-01)

### Le projet CMP (client fondateur)
- **Client final** : Crédit Municipal de Paris, SVV, **Grande Entreprise** → obligation émission + e-reporting au **1er septembre 2026**, sans report possible.
- Flux majoritairement **B2C → e-reporting** (Flux 10.3). Risque pertinent : 500 €/transmission manquante (plafond 15 000 €/an, LF 2026).
- **Chiffrage V1 B2C** : forfait au résultat **~33 k€ HT** (fourchette 32-35 k€), sans TJM affiché. Charge marché ~35-45 j ; effort réel de dev < 10 j.
- **Proposition commerciale EN ATTENTE** : dépendance au retour d'ISATECH. On ne fait pas d'offre tant qu'ISATECH ne s'est pas manifesté.
- Conditions suspensives à inclure le moment venu : enregistrement SIRET CMP au QAS/annuaire, validation fiscale du régime des ventes de gages (CMP / expert-comptable).
- Faisabilité technique **prouvée en sandbox** : modèle 2 lignes marge validé, tax report DGFiP généré (cf. RECAP).

### ISATECH (éditeur d'EncheresV6)
- ISATECH ne livrera pas de module de conformité pour EncheresV6 avant 2027 (ses autres clients ne sont concernés qu'à cette date).
- **La majorité du parc EncheresV6 ne peut PAS être migrée vers la solution récente d'ISATECH d'ici 2027** (matériellement impossible) → ISATECH aura besoin d'une solution pour sa base legacy. La Passerelle est cette solution.
- **Stratégie** : développer le produit MAINTENANT pour disposer d'une **démo complète de bout en bout** quand ISATECH se réveillera. Position visée : seule solution fonctionnelle existante pour le parc EncheresV6 → partenariat / marque blanche en position de force.
- Le serveur Zen `azmut-enbase01` héberge une base par étude (ADER, BATTIN, CHAUSSON, CORTOT…) : grappe de déploiement à coût marginal quasi nul, même schéma.

### Calendrier de référence (réforme)
| Échéance | Obligation | Qui |
|---|---|---|
| 1er sept. 2026 | Réception | Toutes les entreprises |
| 1er sept. 2026 | Émission + e-reporting | GE / ETI (dont CMP) |
| 1er sept. 2027 | Émission + e-reporting | PME / TPE / micro (= le reste du parc EncheresV6 et le marché de masse) |

---

## 3. Décisions techniques actées

| Décision | Choix | Justification |
|---|---|---|
| **Framework** | **.NET Framework 4.8** (pas 4.7, pas .NET 8/10) | Compatibilité extrême : tourne sur Windows 7 SP1 / Server 2008 R2 SP1 et tout ce qui suit. .NET 8/10 ne démarre pas sur Server 2008 R2 / 2012 non-R2, même en self-contained. 4.8 = même portée OS que 4.7 mais version maintenue. |
| **Builds** | x86 **et** x64 | Les drivers ODBC legacy (Pervasive notamment) sont souvent 32 bits. Pattern déjà appliqué sur EncheresExtract. |
| **UI console admin** | **WPF desktop** | Pas de web, pas de multi-tenant. Déploiement on-premise simple, adapté au monde legacy. |
| **Mode automatique** | EXE CLI + tâche planifiée Windows | Pattern SynchroAxelor / SynchroAppliMobile, éprouvé en production chez le même client. |
| **Persistance état** | SQLite local | Anti-doublons, statuts, journal — aucune écriture dans la base source. |
| **PA partenaire** | B2Brouter (eDocExchange API) | Compte staging validé, payloads validés, modèle 2 lignes marge accepté. Multi-PA = plus tard. |
| **Données de dev** | Fixtures JSON locales (issues d'`extraction-result.json`) | Le poste de dev n'a pas de licence Zen valide. L'adaptateur réel (ODBC) n'est testé que sur le serveur licencié. |
| **Accès base source** | Lecture seule stricte, zéro modification du logiciel source | Supprime la dépendance à l'autorisation de l'éditeur. Décision structurante du projet CMP, généralisée au produit. |

---

## 4. Architecture

```
Gateway.sln                                    (.NET Framework 4.8)
│
├── Gateway.Core\                ★ LE PRODUIT — générique, aucune référence à un logiciel source
│   ├── Pivot\                   modèle pivot document (bordereau/facture/ticket : lignes, taxes, avoirs, paiements)
│   ├── B2Brouter\               client API : invoices, tax_reports, transports, retry, errors[]
│   ├── TvaMapping\              moteur de mapping régime → {catégorie TVA, %, VATEX} — table paramétrable
│   ├── Validation\              SIREN/SIRET (Luhn), équilibre TVA/arrondis, données manquantes, détection B2B
│   ├── Tracking\                SQLite : anti-doublons (hash payload), statuts, journal, reprise
│   └── Pipeline\                orchestration : PipelineRunner (extraction → contrôles → envoi → suivi), host-agnostique
│
├── Gateway.Adapters.EncheresV6\ premier adaptateur (implémente IExtractor)
│   ├── PervasiveExtractor       ODBC réel — testé uniquement sur serveur licencié
│   └── FixtureExtractor         rejoue les fixtures JSON — dev et démo hors site
│
├── Gateway.App\                 WPF — console admin (support de la démo)
│
├── Gateway.Cli\                 EXE ligne de commande — appelle PipelineRunner, lancé par le Planificateur Windows
│
└── Gateway.Service\             service Windows résident (ServiceBase) — ordonnanceur interne + battement de cœur
```

> **Deux hôtes, un seul moteur** : `Gateway.Cli` et `Gateway.Service` sont des coquilles minces autour de `Gateway.Core.Pipeline.PipelineRunner`. On active l'un OU l'autre par déploiement (cf. F11). Tâche planifiée = défaut simple ; service résident = auto-surveillance (« montre morte ») et sites sans accès au Planificateur.

**Frontière produit/adaptateur** : tout ce qui dépend du schéma de données du logiciel source vit dans l'adaptateur. Tout ce qui dépend de la réforme (formats, TVA, API PA, statuts) vit dans le Core. Argument commercial direct : « l'adaptateur pour votre logiciel, c'est X jours » (ISATECH, autres éditeurs verticaux).

---

## 5. Périmètre de la démo (cible ISATECH)

Scénario de démonstration visé :
1. La console pointe sur la base DEMO (ou les fixtures) → liste des bordereaux, contrôles visibles
2. Envoi → B2Brouter staging → tax reports DGFiP réels générés → statuts en retour
3. Un avoir → note de crédit liée au document d'origine
4. Un acheteur professionnel → blocage "circuit B2B non configuré"
5. Un SIREN invalide → blocage avec motif
6. Encaissements → e-reporting de paiement (sous réserve API B2Brouter, cf. F9)

Le multi-étude n'est pas un écran de démo mais vient gratuitement (1 fichier de config = 1 étude/1 client).

---

## 6. Fonctionnalités — À DÉTAILLER UNE PAR UNE

> **MÀJ 2026-06-02** : chaque fonctionnalité majeure a fait l'objet d'une deep research + d'un document de conception dédié dans `..\Conception\`. Voir l'index : `Conception\README-Index-Conception.md`. Statuts ci-dessous mis à jour.
>
> | Fonctionnalité | Document de conception | Statut |
> |---|---|---|
> | F1 + F2 | `Conception\F01-F02-Modele-Pivot-Contrat-Extraction.md` | 🟨 spécifié, à revoir |
> | F3 | `Conception\F03-Mapping-TVA.md` | 🟨 spécifié, à revoir |
> | F4 | `Conception\F04-Controles-Qualite-Validation.md` | 🟨 spécifié, à revoir |
> | F5 | `Conception\F05-Client-API-B2Brouter.md` | 🟨 spécifié, à revoir |
> | F6 | `Conception\F06-Tracking-Piste-Audit.md` | 🟨 spécifié, à revoir |
> | F7 + F8 | `Conception\F07-F08-Avoirs-Frontiere-B2B-B2C.md` | 🟨 spécifié, à revoir |
> | F9 | `Conception\F09-E-Reporting-Paiement.md` | 🟨 spécifié, à revoir |
> | F10 | `Conception\F10-Console-Admin-WPF.md` | 🟨 spécifié, à revoir |
> | F11 | `Conception\F11-CLI-Mode-Automatique.md` | 🟨 spécifié, à revoir |
> | F12 | — | ⬜ reste à faire |
>
> Les sections détaillées ci-dessous restent comme cadrage initial de référence.

> Chaque section ci-dessous est un chantier de spécification à creuser ensemble. Statuts : ⬜ à détailler / 🟨 en cours / ✅ spécifié.

### F1 — Contrat d'extraction (`IExtractor`) ⬜
L'interface que tout adaptateur doit implémenter. C'est LE contrat qui définit le produit.
- Ce qu'on sait : extraction par période, lecture seule, documents + lignes + tiers + régimes TVA + règlements.
- À détailler : signature exacte, granularité (document complet vs flux), gestion des données manquantes, pagination/volumes, mode incrémental (qu'est-ce qui dit "nouveau bordereau" ?), erreurs.

### F2 — Modèle pivot ⬜
La représentation interne neutre d'un document à déclarer/facturer.
- Ce qu'on sait : aligné sur les besoins EN 16931 / e-reporting ; doit porter : type (facture/avoir/ticket B2C), émetteur, destinataire (optionnel en B2C), lignes (libellé, montants HT/TVA/TTC, catégorie, VATEX), totaux de contrôle, références d'origine, paiements.
- À détailler : champs exacts, types, ce qui est obligatoire selon le flux (B2C vs B2B vs avoir), arrondis (qui arrondit, quand, comment).

### F3 — Mapping TVA (régime → catégorie/VATEX) ⬜
Le cœur de valeur fiscale. Table paramétrable par déploiement.
- Ce qu'on sait : cf. `Analyse-Donnees-V1-Mapping-TVA.md` §5 — régimes assujetti/non-assujetti/marge, VATEX-EU-F/I/J, cas "non assujetti ≠ marge" à trancher juridiquement par le client.
- À détailler : format de la table de config, valeurs par défaut, comportement quand un régime n'est pas mappé (bloquer ? alerter ?), traçabilité du mapping appliqué (piste d'audit).

### F4 — Validations / contrôles qualité ⬜
Tout ce qui bloque ou alerte AVANT envoi.
- Ce qu'on sait : SIREN/SIRET (clé Luhn + format), équilibre HT+TVA=TTC par ligne et par document, montants flottants à arrondir (2 décimales), données obligatoires manquantes, détection acheteur professionnel.
- À détailler : liste exhaustive des contrôles, niveaux (bloquant/alerte), messages, où les résultats sont stockés/affichés.

### F5 — Client API B2Brouter ⬜
- Ce qu'on sait : tous les appels validés en staging (cf. RECAP partie B) — auth X-B2B-API-Key, POST invoices, GET tax_reports, gestion errors[], pas d'endpoint resend (recréer), tax_report_setting, directory lookup non fiable en sandbox.
- À détailler : surface exacte du client, gestion des erreurs/retry/timeouts, idempotence, gestion des comptes (un par client final), version d'API (X-B2B-API-Version).

### F6 — Tracking / anti-doublons / reprise ⬜
- Ce qu'on sait : SQLite local, hash du payload pour l'anti-doublon, état par document (à envoyer / envoyé / erreur / rejeté), aucune écriture dans la base source.
- À détailler : schéma SQLite, cycle de vie des états, politique de reprise après crash, purge/rétention, lien document source ↔ id B2Brouter ↔ tax report.

### F7 — Avoirs B2C ⬜
- Ce qu'on sait : côté API = `is_credit_note: true` + `is_amend` + `amended_number`/`amended_date` ; côté EncheresV6 = avoir stocké en POSITIF, lié par `no_ba_lettrage`, `bordereau_ou_avoir='A'`.
- À détailler : règle de transformation (signe, lien), que faire d'un avoir dont l'original n'a pas été envoyé par la passerelle, avoirs partiels.

### F8 — Garde-fou acheteurs professionnels (B2B) ⬜
- Ce qu'on sait : en V1 le circuit B2B (e-invoicing Flux 1) n'est pas développé ; un acheteur pro détecté → mise en erreur "à traiter manuellement / circuit B2B non configuré". Détection difficile : pas de SIREN acheteur dans EncheresV6 (champ `societe` rempli = indice).
- À détailler : heuristique de détection, workflow de l'erreur (qui débloque, comment), préparation de la phase 2 (e-invoicing complet).

### F9 — E-reporting de paiement (Flux 10.2/10.4) ⬜ ⚠️ POINT OUVERT B2BROUTER
- Ce qu'on sait : obligation réelle (TVA sur encaissements des prestations de services/frais) ; les données existent dans EncheresV6 (`type_ligne=3`, `date_reglement`, `no_remise`) ; B2Brouter génère des ledgers `xml.ledger.dgfip.payments` ; MAIS le statut "Encaissée" (CDAR 212) est annoncé "planned for a future release".
- **Première action : vérifier/demander à B2Brouter comment transmettre les encaissements aujourd'hui via l'API.** Si bloqué chez eux → afficher "préparé, en attente PA" dans la démo.
- À détailler : modèle de données paiement dans le pivot, agrégation (par jour ? par taux ?), rattachement paiement ↔ document.

### F10 — Console admin WPF ⬜
- Ce qu'on sait : desktop, simple, on-premise. Fonctions : lister, voir contrôles, envoyer, voir statuts, renvoyer, voir erreurs/blocages.
- À détailler : écrans (maquettes), filtres, workflow opérateur type, gestion de la config (édition de la table TVA ? lecture seule ?), logs visibles.

### F11 — CLI / mode automatique ⬜
- Ce qu'on sait : EXE + tâche planifiée, pattern SynchroAxelor (App.config, CommandLineParser, logs fichiers).
- À détailler : arguments, codes retour, fréquence recommandée, notifications d'erreur (mail ?), cohabitation avec la console (verrous).

### F12 — Configuration / déploiement ⬜
- Ce qu'on sait : 1 config = 1 client/étude (chaîne de connexion, SIREN émetteur, clé API B2Brouter, table TVA, planification). Packaging zip + doc.
- À détailler : format (ini vs json), gestion des secrets (clé API), procédure d'installation type, prérequis serveur, multi-config sur un même serveur (cas azmut-enbase01).

---

## 7. Plan de construction (après spécification)

| Étape | Contenu | Critère de done |
|---|---|---|
| **1. Socle** | Solution + Core (pivot, client B2Brouter, mapping TVA, validation, tracking) + FixtureExtractor + CLI | Un bordereau des fixtures part sur le staging et revient avec son tax report |
| **2. Console WPF** | Liste, contrôles, envoi, statuts, erreurs | Démo du flux nominal en clic-bouton |
| **3. Cas non-nominaux** | Avoirs, garde-fou B2B, SIREN invalide, paiements 10.4 (selon B2Brouter) | Chaque cas démontrable |
| **4. Réel** | PervasiveExtractor sur le serveur, packaging x86/x64, doc | Tourne sur azmut-enbase01 contre la base DEMO |

---

## 8. Points ouverts (hors fonctionnalités)

| # | Point | Propriétaire | Échéance souhaitable |
|---|---|---|---|
| 1 | **Nom du produit** | nous | avant la démo ISATECH |
| 2 | **Flux 10.4 / encaissements via API B2Brouter** | B2Brouter (ticket support à ouvrir) | avant l'étape 3 |
| 3 | Validation fiscale ventes de gages (mapping VATEX définitif) | CMP / expert-comptable | avant la prod CMP |
| 4 | Enregistrement SIRET CMP au QAS / annuaire | CMP / B2Brouter | condition suspensive de l'offre |
| 5 | Retour ISATECH (déclencheur de l'offre CMP) | ISATECH | — |
| 6 | Programme reseller / partenariat B2Brouter | nous | parallèle, non bloquant |
| 7 | Mise à jour du RECAP (chiffrage 33 k€, périmètre, architecture sans modif Magic) | nous (Claude peut le faire) | quand voulu |

---

## 9. Historique des décisions

| Date | Décision |
|---|---|
| 2026-06-01 | Architecture V1 CMP : zéro impact Magic, lecture SQL Pervasive seule |
| 2026-06-01 | Chiffrage CMP : ~33 k€ forfait au résultat, en attente ISATECH |
| 2026-06-01 | Stratégie : développer le produit/démo en attendant ISATECH |
| 2026-06-01 | Produit générique "tout logiciel legacy", pas spécifique enchères → nom "EncheresConnect" rejeté |
| 2026-06-01 | Stack : .NET Framework 4.8, WPF, SQLite, on-premise only, fixtures JSON pour le dev |
