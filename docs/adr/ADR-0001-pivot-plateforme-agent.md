# ADR-0001 — Pivot d'architecture : plateforme centralisée multi-tenant + agent léger

**Date :** 2026-06-03

**Statut :** Accepté (2026-06-03)

---

## Contexte

La réforme française de la facturation électronique (e-invoicing / e-reporting,
échéances septembre 2026 / 2027) impose aux entreprises de transmettre leurs factures
et leurs données fiscales via des **Plateformes Agréées (PA)**. Liakont est une
**passerelle de conformité GÉNÉRIQUE** entre des logiciels métier *legacy* (base de
données accessible, **aucune API**) et ces PA.

Trois contraintes structurent la décision :

1. **Logiciels source sans API.** L'accès aux données se fait par lecture seule de la
   base (ODBC). Les logiciels cibles tournent sur de vieux environnements Windows avec
   des drivers ODBC 32 bits (ex. Pervasive pour EncheresV6).
2. **Besoin d'un PRODUIT générique, pas d'un développement spécifique.** Liakont doit
   servir N éditeurs et leurs clients finaux, sans coder de règle propre à un client
   (toute donnée client = paramétrage de tenant).
3. **Trois topologies de déploiement du même produit** (même code) :
   - **Self-hosted éditeur** : l'éditeur opère l'appliance Docker sur sa propre infra ;
     IT Innovations ne voit rien de son parc client.
   - **Instance dédiée hébergée** : IT Innovations opère une instance cloisonnée à la
     marque de l'éditeur, réversible (dump / restore / DNS → self-hosted).
   - **Mutualisée** : IT Innovations opère l'instance « maison » pour ses clients directs.

L'architecture **on-premise** initiale (service Windows + console WPF + tracking SQLite
chez chaque client) ne tient pas face à ces contraintes : le modèle marque grise
(1 éditeur = N clients finaux) est ineconomique en on-premise ; la **supervision
proactive** (détection des pannes silencieuses, dead-man's switch) y est structurellement
impossible alors que l'e-reporting a des échéances légales ; et plusieurs points durs
techniques (RFC 3161, OpenTimestamps, parsing PDF sous contrainte de licence) ne se
résolvent qu'avec des runtimes modernes.

Analyse complète : `tasks/analyse-impact-pivot-plateforme.md`. Décision de pilotage :
`tasks/decisions.md` (2026-06-03).

---

## Décision

Abandon de l'architecture on-premise (service Windows + console WPF + SQLite) au profit
d'une **plateforme web centralisée multi-tenant** complétée par un **agent léger** installé
chez le client final.

| | **Plateforme** | **Agent** |
|---|---|---|
| Où | Hébergée (self-hosted éditeur / dédiée / mutualisée) | Serveur du client final, près de la base legacy |
| Stack | **.NET 10 LTS**, ASP.NET Core, **Blazor Server + Radzen**, **PostgreSQL** (Dapper + DbUp), **Keycloak / OIDC** | **.NET Framework 4.8**, x86 **et** x64 |
| Rôle | TOUT le métier : ingestion, TVA, validation, machine à états, envoi PA, archive WORM, console, supervision | Extraction ODBC (lecture seule) + pool PDF + buffer SQLite + push HTTPS + heartbeat |
| Multi-tenant | Oui (1 tenant = 1 client final) | Non (1 agent = 1 tenant, clé API scopée) |

### Où vit le code : option C (repo Liakont, socle Stratum vendored)

Quatre options ont été pesées (voir `analyse-impact-pivot-plateforme.md` §6 ;
*Alternatives rejetées* ci-dessous). **L'option C est retenue** : le code de la
plateforme vit dans le **repo Liakont**, et le **socle Stratum y est COPIÉ (vendored)** —
`Common/*` + les modules autonomes `Identity`, `Job`, `Notification`, `Audit` — avec une
**note de provenance** (`docs/architecture/provenance-socle-stratum.md` : commit source,
date, fichiers copiés, écarts). La re-convergence future vers des packages NuGet
(option D) reste possible et explicitement visée à terme.

### Nommage des projets

- **`Liakont.*`** pour tous les projets **produit** : `Liakont.Host`,
  `Liakont.Modules.*`, `Liakont.PaClients.*`, `Liakont.Agent.*`, `Liakont.Agent.Contracts`.
- **`Stratum.*`** pour tout le code **vendored** (`Stratum.Common.*`, modules socle).
  Cette frontière de nommage matérialise la limite « ne pas modifier silencieusement le
  socle » : toute modification d'un fichier `Stratum.*` est consignée dans la provenance.

### Agent net48 et contrat d'API versionné

- L'agent cible **.NET Framework 4.8** (jamais 4.7, jamais .NET moderne) et est buildé en
  **x86 et x64**. ⚠️ **Deux justifications distinctes, à ne pas confondre** (détail :
  avenant 2026-06-20) : (a) le choix **net48** tient à la **compatibilité de l'OS hôte legacy**
  du client + **zéro runtime à déployer** (net48 est présent en standard sur les Windows visés) ;
  (b) le double build **x86/x64** tient à la **bitness du process** imposée par les **drivers ODBC
  32 bits** (ex. Pervasive pour EncheresV6) — c'est la bitness, pas net48, que le 32-bit impose.
  L'agent **n'a AUCUNE logique métier** (extraction + transport uniquement). Il **n'est plus le
  seul composant déployé chez le client final** : `clients/OnSiteSignature` (client soft de
  signature sur place **Wacom**, net48, **ADR-0030**) est un **second composant client** — voir
  l'avenant 2026-06-20.
- Le contrat d'API **agent ↔ plateforme** est **versionné** (`v1`, `v2`…) : la plateforme
  supporte la version N **et** N-1 (les agents se mettent à jour moins vite). Les DTOs
  partagés vivent dans **`Liakont.Agent.Contracts`** (**netstandard2.0**, aucune logique),
  référencé par l'agent (net48) **et** par le module Ingestion (net10). Le payload est le
  document pivot EN 16931 (JSON) — specs F01-F02, contrat détaillé F12.

---

## Conséquences

1. **Divergence du socle à tracer.** La copie vendored ne reçoit pas automatiquement les
   correctifs de Stratum (et inversement). Toute modification locale de `Stratum.*` doit
   être consignée dans `docs/architecture/provenance-socle-stratum.md` — c'est le prix
   assumé de l'option C, à rembourser par la re-convergence (option D).
2. **Re-convergence NuGet future (option D).** Quand les besoins socle de Liakont seront
   stabilisés et reversés dans Stratum, la copie pourra être remplacée par des packages
   NuGet. Le nommage `Liakont.*` / `Stratum.*` et la provenance préparent cette bascule.
3. **net48 confiné à l'agent.** La contrainte « .NET Framework 4.8 » ne vaut plus que pour
   l'agent. SQLite survit comme **buffer local de l'agent** (reprise sur coupure réseau) ;
   DPAPI uniquement pour les **secrets de l'agent** (clé API chiffrée). Tout le reste passe
   en .NET 10 / PostgreSQL.
4. **Gains.** Tests **E2E Playwright** des écrans web (impossible en WPF — gain majeur) ;
   réutilisation de l'infrastructure Stratum (multi-tenancy, Identity/RBAC, Job, Notification,
   Audit, UI shell, NetArchTest) ; **disparition de WPF** et des points durs net48
   (RFC 3161, OpenTimestamps, parsing PDF) côté plateforme ; mises à jour réglementaires
   centralisées (toutes les instances suivent un déploiement) ; supervision proactive
   rendue possible (dead-man's switch côté plateforme).
5. **Perte sèche limitée.** Le scaffold net48 (SOL01-03) et la PR #1 (GATE_SOCLE) sont
   invalidés (~2-3 sessions). Le pivot intervient **avant** le démarrage du métier — le
   meilleur moment possible.

---

## Alternatives rejetées

Résumé de `analyse-impact-pivot-plateforme.md` §6 :

- **Option A — Modules Liakont dans `Stratum.Host`.** La passerelle deviendrait des
  modules de l'ERP Stratum. **Rejetée** : le déploiement Liakont embarquerait les modules
  ERP (Sales, Reservation, Tourisme…) — poids mort et surface d'attaque inutiles —, les
  releases seraient couplées et le positionnement commercial confus.
- **Option B — `Liakont.Host` séparé DANS le repo Stratum.** Deux Hosts, un seul repo.
  **Rejetée** : l'orchestration, les docs et les specs Liakont vivent dans le repo
  Liakont ; une orchestration multi-agents cross-repo n'est pas gérée par le protocole, et
  les cycles de vie des deux produits resteraient couplés.
- **Option D — Socle Stratum en packages NuGet (maintenant).** Propre, pas de divergence
  silencieuse. **Rejetée pour la V1** : il faudrait monter dès maintenant l'infrastructure
  de packaging et subir la friction de version à chaque évolution du socle — or Liakont
  *va* demander des évolutions (branding, clés API, provisioning). D reste la **cible à
  terme**, pas le point de départ.

L'**option C** garde tout au même endroit pour l'orchestration multi-agents (critique sous
contrainte de délai) et laisse la liberté d'adapter (branding, provisioning) sans risquer
l'ERP, au prix d'une divergence tracée.

---

## Avenant 2026-06-20 — Second composant client (Wacom) + justification net48/x86 séparée (RDF12)

- **Statut** : Accepté — **Date** : 2026-06-20 — **Items** : RDF12 (redline ADR fondateurs, findings
  RL-AGT-5 + recadrage du « seul composant client », DEC-5)

### 1. L'agent n'est plus le seul composant client

Le pivot décrivait l'**agent** comme « le **seul** composant chez le client final ». Ce n'est plus exact :
`clients/OnSiteSignature` — le **client soft de signature sur place Wacom** (`Liakont.OnSiteSignature.Client`,
**net48**, hors `agent/`, tranché par **[ADR-0030](ADR-0030-client-soft-wacom-signature-sur-place.md)**) —
est un **second binaire net48 installé chez le client**. Comme l'agent, c'est un **pur capteur sans logique
métier** (capture de signature + transport tenant-scopé) ; il **ne porte ni TVA, ni validation, ni machine à
états** (frontière inchangée).

**DEC-5 — modèle de confiance / patch du composant Wacom (recommandation, décision = Karl).** Ce composant
embarque un **SDK natif Wacom** susceptible de porter des CVE et installé hors de la flotte d'agents.
**Recommandation** : lui faire **hériter du modèle de confiance et d'auto-update de
[ADR-0013](ADR-0013-modele-confiance-auto-update-agent.md)** (manifeste de version **signé** par une clé de
release hors plateforme, anti-downgrade, rollback fail-closed) plutôt qu'un patch manuel documenté — un
composant client non patché contredirait l'argument de sécurité du pivot. **Le modèle de patch effectif reste
une décision opérateur** (DEC-5) ; cet avenant **recommande** et **n'exécute pas** (aucun code, le client soft
relève de SIG08).

### 2. Les deux contraintes de runtime de l'agent sont **distinctes**

La formulation initiale accolait deux justifications de natures différentes (finding RL-AGT-5). À séparer :

- **net48** (et non .NET moderne) ⇐ **compatibilité de l'OS hôte legacy** du client + **zéro runtime à
  déployer** (net48 est livré en standard sur les Windows visés). Ce **n'est pas** la conséquence du 32-bit.
- **double build x86/x64** ⇐ **bitness du process** imposée par les **drivers ODBC 32 bits** (Pervasive
  EncheresV6…) : un process devant charger un driver 32-bit doit être 32-bit. C'est la **bitness**, pas le
  TFM, que le 32-bit contraint.

**Condition de sortie (par contrainte, indépendamment) :**

- *net48* : remplaçable par un **.NET moderne** le jour où les **OS hôtes ciblés** garantissent le runtime
  requis (ou où l'on accepte de **déployer le runtime**) — décision par parc client, pas globale.
- *x86* : le build **32-bit** disparaît dès qu'**aucun adaptateur** d'un déploiement n'a besoin d'un driver
  **ODBC 32-bit** (tous les pilotes source disponibles en 64-bit) ; le build **x64** seul suffit alors.

Les deux conditions sont **orthogonales** : on peut sortir du 32-bit en restant net48, ou inversement.

## Références

- `blueprint.md` (v2 — architecture cible plateforme + agent)
- `tasks/analyse-impact-pivot-plateforme.md` (analyse d'impact du pivot, §6 = choix de l'option C)
- `tasks/decisions.md` (journal de décisions, 2026-06-03)
- `docs/conception/F12-Architecture-Plateforme-Agent.md` (contrat d'ingestion, supervision, déploiement)
- `docs/architecture/provenance-socle-stratum.md` (note de provenance du socle vendored)
- `docs/adr/socle/` (ADR du socle Stratum hérités — voir le README pour la résolution de la
  collision du n°0010 et la distinction des numérotations)
