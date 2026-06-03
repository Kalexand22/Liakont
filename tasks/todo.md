# Plan de travail courant

_Ce fichier est le plan de la session interactive en cours. En mode orchestration,
le backlog autoritaire est `orchestration/manifest.yaml`._

## 2026-06-03 (suite) — Repo agent séparé + installateur paramétrable par intégrateur

Décisions actées avec Karl (session interactive) :
- **Agent = dépôt Git produit séparé** (`liakont-agent`), PAS un template forké par client.
  Cœur générique + adaptateurs en plug-ins (1 par logiciel source) ; `Liakont.Agent.Contracts`
  publié en NuGet versionné. Le « différent par client » = config/seed (`deployments/<client>/`),
  jamais le cœur (auto-update de flotte préservé). → ADR-0005.
- **Installateur GUI à écrans guidés** dans le repo agent (connexion BDD source + serveur central),
  **piloté par un profil intégrateur déclaratif** (3 états affiché/verrouillé/masqué + valeur par
  défaut ; jamais de `if(integrateur)`). 1 `.exe` par profil, même code. Générique par intégrateur.
  Liste des options masquables NON figée (data-driven, extensible). → F13.

Garde-fou : l'agent n'existe pas encore (SOL01 en cours ; lots AGT/OPS = priorité 5500+). On ACTE
la conception ; on ne réécrit PAS l'orchestration runtime (protocol.md multi-repo, manifest+state)
maintenant — bascule outillage planifiée aux lots AGT/SOL02, tracée dans l'ADR. Un item ajouté au
manifest sans entrée dans state.yaml est traité « done & purgé » (jamais exécuté) : l'intégration
des items proposés reste une opération opérateur atomique (manifest + OPS.yaml + state).

- [x] ADR-0005 : agent en repo séparé + contrat NuGet (`docs/adr/ADR-0005-agent-repo-separe-contrat-nuget.md`)
- [x] F13 : conception installateur agent + profils intégrateur (`docs/conception/F13-...md`)
- [x] blueprint.md : orientation repo séparé (→ ADR-0005) + composant `Liakont.Agent.Installer` + règle 7 « profil intégrateur »
- [x] README-Index-Conception : ligne F13
- [x] F12 : note d'amendement en tête (→ F13 + ADR-0005) + ligne Installer au tableau des composants
- [x] lessons.md : leçon (acter une décision ≠ réécrire l'orchestration ; piège manifest sans state)
- [x] Items backlog **intégrés** : OPS08 ajouté à `manifest.yaml` (v8) + `OPS.yaml` + `state.yaml`
      (pending) ; GATE_TOOLKIT dépend d'OPS08. OPS05 = note de renvoi (le packaging par profil vit
      dans OPS08, pour éviter le cycle OPS05↔OPS08).
- [x] verify-fast PASS ; codex-review 0 P1 / 2 P2 (les 2 hors-scope, sur ADR-0004 — voir Revue)

### Revue (fin de session — 2026-06-03)

**verify-fast** : PASS intégral (structure, manifest-sanity, plateforme build+tests, agent net48
build+tests x86). Aucun fichier de solution ni manifest/state touché — changements 100 % documentaires.

**codex-review** (round 1, working tree) : **0 P1, 2 P2**. Mes 7 fichiers (ADR-0005, F13, blueprint,
F12, README-Index, lessons, todo) : **aucun finding**. Les 2 P2 portent uniquement sur
`docs/adr/ADR-0004-perimetre-contrat-extraction-pivot-v1.md`, fichier **untracked pré-existant,
étranger à cette session** :
- P2-1 : affirmation inexacte dans ADR-0004 (« aucune classe C# n'existe » alors que
  `agent/src/.../IExtractor.cs` existe en squelette).
- P2-2 : ADR-0004 non tracé (ni dans ce plan, ni committé), risque d'être committé en passager.

**Décision** : les 2 P2 sont **acceptés hors-scope** — je ne modifie pas ADR-0004 (WIP d'une autre
tâche ; principe « surgical / ne pas toucher un orphelin non créé par mon changement »). Pas de
round 2 : il rebouclerait sur un fichier que je ne dois pas modifier. **À remonter à l'humain**
(cf. compte-rendu de session).

**Non fait volontairement** (différé / hors autonomie) :
- ~~Intégration des items au backlog~~ **FAIT (2026-06-03)** : OPS08 ajouté à `manifest.yaml` (v8),
  `OPS.yaml` et `$ORCH_REPO/state.yaml` (pending) — atomique, aucune session active. GATE_TOOLKIT
  dépend désormais d'OPS08. Packaging par profil placé dans OPS08 (évite le cycle OPS05↔OPS08).
- Réécriture de l'orchestration multi-repo (`protocol.md`), de `verify-fast` Step 4, du champ
  `repo:` des segments : différée au démarrage du segment `agent` (ADR-0005 §Conséquences).
- Commit demandé : sur `feat/socle-v6-SOL01` (l'utilisateur a explicitement levé la contrainte de
  branche — « le projet vient de débuter »). `state.yaml` committé séparément dans `$ORCH_REPO`.

---

## ⚠️ PIVOT D'ARCHITECTURE EN COURS (2026-06-03)

Décision actée : plateforme web centralisée (socle Stratum) + agent léger remplace
l'architecture on-premise. Voir `tasks/analyse-impact-pivot-plateforme.md` et `tasks/decisions.md`.

**L'orchestration est SUSPENDUE jusqu'au manifest v6.** Ne pas lancer de session.

### Séquence de préparation (sessions interactives)

- [x] ~~Fermer la PR #1 (GATE_SOCLE) sans la merger~~ **FAIT (2026-06-03)**
- [x] ~~Trancher : où vit le code plateforme ?~~ **TRANCHÉ** : repo Liakont, socle Stratum vendored
- [x] ~~Trancher : nommage des projets~~ **TRANCHÉ** : Liakont.* (Liakont.Host, Liakont.Modules.*, Liakont.Agent)
- [x] ~~Réécrire blueprint.md~~ **FAIT (2026-06-03)** : blueprint v2 (plateforme + agent, 3 topologies,
      multi-tenancy, structure du dépôt, stack double, frontières de modules, stratégie de test)
- [x] ~~Réécrire les règles métier du CLAUDE.md~~ **FAIT (2026-06-03)** : règles 5-12 + checklist +
      règles de review 14-20 adaptées (tenant-scoping, frontières modules, Blazor/bUnit/Playwright,
      socle vendored). AGENTS.md synchronisé
- [x] ~~Amender F10/F11 + créer F12~~ **FAIT (2026-06-03)** : F12 créé (agent, contrat d'ingestion,
      supervision, configuration/déploiement — absorbe l'ancien placeholder F12), F10 amendée
      (console web, contenu fonctionnel conservé), F11 amendée (exécution répartie agent/plateforme),
      F06 amendée (PostgreSQL remplace SQLite), index mis à jour
- [x] ~~Réécrire manifest v6 + items + blueprints + outillage~~ **FAIT (2026-06-03)** :
      manifest v6 (79 items + 12 gates = 91 entrées, 11 segments, 19 lots), 19 fichiers d'items
      (nouveaux : AGT/SUP/OPS/BRD/WEB ; supprimés : SVC/CLI/WPF/PKG), blueprint blazor-page-item
      (remplace wpf-screen-item), verify-fast/run-tests adaptés au double build (plateforme .NET 10 +
      agent net48 x86), codex-review mis à jour (frontières modules, tenant-scoping)
- [x] ~~Réinitialiser state.yaml (v6)~~ **FAIT (2026-06-03)** : 91 entrées pending, segments v6,
      branche feat/socle (v5) supprimée. verify-fast PASS (manifest-sanity + gardes bootstrap)
- [x] **Traiter les reviews indépendantes v6 & v8** ~~(session en cours, 2026-06-03)~~ **FAIT (2026-06-03)** :
  - [x] Consigner les décisions D1-D8 dans tasks/decisions.md
  - [x] manifest.yaml : dépendances manquantes (VAL02, TRK01, TRK03, OPS03, OPS06), inversion
        PIV04↔PIV05, nouveaux items (SOL05, SOL06, API05, WEB09, OPS07, DOC02, DOC03), gates,
        meta.version 6 → 7
  - [x] Items YAML : SOL, PIV, CFG, VAL, TVA, TRK, PIP, PAA, AGT, API, WEB, SUP, OPS, DOC, CMP
  - [x] Blueprints + conventions + prompt.md + protocol.md + README.md
  - [x] Scripts : verify-fast.ps1, run-tests.ps1, codex-review.ps1, orch-state.ps1,
        build-agent-context.ps1 (gardes anti-faux-vert testées : ORCH_REPO invalide → exit 1)
  - [x] Docs : definition-of-done.md, orchestration-protocol.md, F01-F02, F05, F09, F12, F06,
        Cadrage, analyse-impact-pivot-plateforme.md
  - [x] state.yaml (dépôt d'état) : 7 nouveaux items en pending (98 entrées alignées)
  - [x] Vérification : cohérence croisée (98=98, priorités OK) + verify-fast PASS + codex-review
  - [x] Table de correspondance finding→correction : tasks/review-independante-v6-v8-reponses.md
- [x] **Ajustements de validation Karl (2026-06-03, soir)** : D9 (coffre WORM = 3ᵉ axe
      enfichable à capacités `IArchiveStore` ; V1 = FileSystem + S3-compatible couvrant
      Amazon/MinIO/OVH/Scaleway ; Azure Blob + GCS fast-follow) + D10 (auth derrière une
      abstraction d'IdP dès SOL01 + spike empreinte Keycloak vs OpenIddict — ne pas figer
      l'auth). Branding self-hosted déjà couvert par BRD01 (confirmé, inchangé). Gravé dans
      decisions.md (D9/D10), TRK05, SOL01, OPS01, blueprint §2/5/6/12, F12 §7, CLAUDE/AGENTS.
      **Manifest inchangé** (98 entrées, aucun nouvel item, meta.version reste 7).
- [ ] **Relancer l'orchestration** : `Lis orchestration/prompt.md et exécute-le.` dans une nouvelle
      fenêtre Claude Code → l'agent prendra SOL01 (vendoring du socle Stratum + Liakont.Host)

## Actions humaines à mener en parallèle du développement (hors orchestration)

### Nouvelles actions dues au pivot (2026-06-03)

- [ ] **Question ISATECH/CMP : tenant mutualisé, instance dédiée ou appliance on-premise ?**
      → dimensionne le lot CMP et l'urgence de l'appliance Docker
- [ ] Choisir l'hébergeur des instances hébergées (OVH / Scaleway / autre — France/UE obligatoire)
- [ ] RC Pro : faire évoluer le contrat pour couvrir l'hébergement de données fiscales de tiers
- [ ] Préparer le DPA (sous-traitant RGPD) et le registre des traitements
- [ ] Se renseigner sur le séquestre de code source (APP) — argument commercial pour les éditeurs self-hosted
- [ ] Décliner le pivot dans l'offre commerciale (supervision proactive incluse, marque grise = instance
      par éditeur, réversibilité)

Ces points appartiennent à des humains. Grâce à l'architecture générique (plug-ins + paramétrage),
ils ne bloquent PAS le développement du produit — ils bloquent les gates de déploiement concernées.

### Bloquant pour GATE_PROD_CMP (production CMP)
- [ ] Expert-comptable CMP : régime 6 = marge EU-J ou hors champ ? → consigner dans deployments/cmp/DECISIONS-FISCALES.md
- [ ] Expert-comptable CMP : TVA sur les débits optée ? (conditionne l'e-reporting paiement pour CE déploiement)
- [ ] Expert-comptable CMP : OperationCategory = Mixte ?
- [ ] Expert-comptable CMP : volume d'acheteurs professionnels ?
- [ ] Ticket support B2Brouter : montant marge cas n°33
- [ ] Ticket support B2Brouter : transmission Flux 10.2/10.4 (calendrier) → mettra à jour les capacités du plug-in
- [ ] Ticket/vérification staging B2Brouter : endpoint de téléchargement de la facture Factur-X générée (→ capacité SupportsDocumentRetrieval, archivage des factures légales)
- [ ] Compte B2Brouter production + tax_report_setting

### Bloquant pour GATE_PA_SUPERPDP (plug-in Super PDP)
- [ ] Ouvrir une sandbox Super PDP (action DR17-A4, ~1-2 jours)
- [ ] Questions support Super PDP : flux paiement 10.2/10.4, archivage NF Z42-013, sort des archives en cas de résiliation

### Veille réglementaire
- [x] ~~Télécharger la DERNIÈRE version des spécifications externes DGFiP~~ **FAIT (2026-06-02)** :
      v3.2 (30/04/2026) téléchargée et dépouillée → `docs/references/dgfip-v3.2/` + note de lecture.
      **Delta v3.1→v3.2 minime, aucun impact V1** (changelog officiel). F01-F02 reste valide.
- [ ] Lire le Dossier général v3.2 (PDF) pour vérifier les évolutions de TEXTE (le changelog ne
      couvre que les XSD) + croiser l'Annexe 7 (règles de gestion V1.9) avec F04 lors du lot VAL
- [ ] Télécharger les normes AFNOR XP Z12-012/-013/-014 (payantes — boutique AFNOR)
- [ ] Vérifier l'impact de la recodification des textes fiscaux applicable au 1er septembre 2026
      sur les références juridiques des specs (art. 289 CGI, etc.)

### Questions techniques ouvertes (à trancher par ADR pendant les items concernés)
- [x] ~~TRK07 : OpenTimestamps en net48~~ **RÉSOLU PAR LE PIVOT (2026-06-03)** : la plateforme est en
      .NET 10, bibliothèques modernes disponibles
- [x] ~~TRK07 : RFC 3161 en net48~~ **RÉSOLU PAR LE PIVOT** : API .NET modernes natives (Rfc3161TimestampRequest)
- [x] ~~TRK08 : extraction de texte PDF en net48 (licences)~~ **RÉSOLU PAR LE PIVOT** : bibliothèques
      modernes côté plateforme (le choix précis reste un ADR, la contrainte net48 a disparu)
- [x] ~~WPF08 : aperçu PDF dans la console~~ **RÉSOLU PAR LE PIVOT** : affichage PDF natif dans le navigateur
- [x] ~~API01 : prérequis réseau Windows (urlacl, SPN, firewall)~~ **RÉSOLU PAR LE PIVOT** : plus d'API
      self-hosted ; la console est web, l'agent fait du HTTPS sortant uniquement
- [ ] Auth des instances : Keycloak par instance, Keycloak mutualisé (un realm par instance), ou
      alternative allégée ? (ADR au début du dev plateforme — empreinte mémoire en jeu)
- [ ] AGT02 : versionnement du contrat d'API agent ↔ plateforme (la plateforme supporte N et N-1)
- [ ] AGT04 : mécanisme d'auto-update de l'agent (flotte d'agents chez les clients finaux)

### Commercial (hors backlog technique)
- [ ] Relancer ISATECH (dossier CMP + période d'observation RJ jusqu'au 7 juillet 2026)
- [ ] Décliner la correction de l'offre (périmètre V1 : pas de réception native, pas de Flux 10.1,
      génération Factur-X par la PA) dans tout support commercial déjà diffusé
