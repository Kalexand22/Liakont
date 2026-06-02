# Plan de travail courant

_Ce fichier est le plan de la session interactive en cours. En mode orchestration,
le backlog autoritaire est `orchestration/manifest.yaml`._

## ⚠️ PIVOT D'ARCHITECTURE EN COURS (2026-06-03)

Décision actée : plateforme web centralisée (socle Stratum) + agent léger remplace
l'architecture on-premise. Voir `tasks/analyse-impact-pivot-plateforme.md` et `tasks/decisions.md`.

**L'orchestration est SUSPENDUE jusqu'au manifest v6.** Ne pas lancer de session.

### Séquence de préparation (sessions interactives)

- [x] ~~Fermer la PR #1 (GATE_SOCLE) sans la merger~~ **FAIT (2026-06-03)**
- [x] ~~Trancher : où vit le code plateforme ?~~ **TRANCHÉ** : repo Conformat, socle Stratum vendored
- [x] ~~Trancher : nommage des projets~~ **TRANCHÉ** : Conformat.* (Conformat.Host, Conformat.Modules.*, Conformat.Agent)
- [x] ~~Réécrire blueprint.md~~ **FAIT (2026-06-03)** : blueprint v2 (plateforme + agent, 3 topologies,
      multi-tenancy, structure du dépôt, stack double, frontières de modules, stratégie de test)
- [x] ~~Réécrire les règles métier du CLAUDE.md~~ **FAIT (2026-06-03)** : règles 5-12 + checklist +
      règles de review 14-20 adaptées (tenant-scoping, frontières modules, Blazor/bUnit/Playwright,
      socle vendored). AGENTS.md synchronisé
- [ ] Amender F10/F11 + créer F12 (architecture plateforme/agent + contrat d'API)
- [ ] Réécrire manifest v6 + items + blueprints d'orchestration + outillage (verify-fast, run-tests)
- [ ] Réinitialiser state.yaml (v6) dans conformat-orchestration
- [ ] Relancer l'orchestration

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
