# F12 — Architecture plateforme & agent — Contrat d'ingestion, supervision, déploiement
### Document de conception — Liakont.Host / Liakont.Modules.Ingestion / Liakont.Agent

> Statut : 🟨 conception interne issue du pivot d'architecture (2026-06-03). À revoir ensemble.
> Référence : `blueprint.md` v2 (doctrine), `tasks/analyse-impact-pivot-plateforme.md` (décision),
> `tasks/decisions.md` (2026-06-03).
>
> Ce document absorbe le « F12 — Configuration / déploiement » prévu dans l'index (jamais rédigé) :
> la configuration et le déploiement sont des facettes de l'architecture plateforme/agent.
>
> **Amendement 2026-06-03** : l'installateur **GUI à écrans guidés** et son **paramétrage par
> profil intégrateur** sont spécifiés à part dans **F13**. L'agent est destiné à un **dépôt Git
> séparé** (`liakont-agent`), son contrat partagé via **NuGet versionné** — voir **ADR-0005** ; la
> bascule outillage/orchestration est différée aux lots AGT/SOL02 (jusque-là l'agent reste dans
> `agent/` et `verify-fast` build les deux solutions).
>
> **Amendement 2026-06-10 (multi-instances, OPS05 pt 5)** : l'agent supporte **plusieurs instances
> sur un même poste** (serveur SaaS mutualisé d'un éditeur : N bases clientes = N services, un par
> base). Une instance est identifiée par son **nom** (option `--instance <nom>`, défaut `Default`)
> qui dérive le service (`LiakontAgent$<nom>`), le mutex de run (`Global\LiakontAgentRun-<nom>`) et
> la racine de données (`%ProgramData%\Liakont\<nom>\`) — voir `Liakont.Agent.Core/AgentInstance.cs`.
> Les chemins cités en §2.3/§2.4 (« `C:\ProgramData\Liakont\…` ») se lisent comme ceux de
> l'instance **Default**, strictement inchangés (compat installations déployées).

---

## 1. Objectif et positionnement

Ce document spécifie ce que le blueprint v2 ne fait que cadrer :

1. **L'agent** : le seul composant installé chez le client final — son cycle de vie, son buffer,
   sa configuration, sa mise à jour.
2. **Le contrat d'ingestion** : l'API versionnée par laquelle l'agent pousse les documents
   pivot vers la plateforme.
3. **La supervision proactive** : comment la plateforme détecte les pannes avant le client.
4. **La configuration et le déploiement** : instance, tenant, agent, appliance, provisioning.

Les specs métier (F01-F09) restent la référence pour le contenu des données ; F12 décrit
**comment elles circulent et où elles vivent**.

## 2. L'agent (`Liakont.Agent`, .NET Framework 4.8)

### 2.1 Composants

| Projet | Rôle |
|---|---|
| `Liakont.Agent` | Service Windows : planification locale, boucle de push, heartbeat |
| `Liakont.Agent.Core` | IExtractor (contrat), file locale SQLite, client HTTP, config DPAPI |
| `Liakont.Agent.Adapters.EncheresV6` | Plug-in source #1 (ODBC Pervasive, x86) |
| `Liakont.Agent.Cli` | Diagnostic : `check-config`, `test-odbc`, `test-api`, `run` manuel, `show-queue` |
| `Liakont.Agent.Installer` | Installateur GUI à écrans + moteur de profil intégrateur (F13) — réutilise `Core` + commandes CLI |
| `Liakont.Agent.Contracts` | (référencé, pas contenu — vit dans `src/Contracts/`, netstandard2.0) DTOs du contrat |

### 2.2 Cycle d'un run d'extraction (local, planifié)

```
1. TIMER       Planification locale (fichier de config, ex. ["03:00"]) + catchUpOnStart
2. EXTRACT     IExtractor lit la base source (ODBC, LECTURE SEULE) sur la période
               → documents pivot (JSON conforme au contrat F01-F02)
               → écrits dans la file locale (SQLite), clé = (source_reference, payload_hash)
3. COLLECT     Si capacités de l'adaptateur :
               ProvidesSourceDocuments     → PDF liés ajoutés à la file
               ProvidesUnlinkedDocumentPool → PDF du pool ajoutés à la file
4. PUSH        Drainage de la file vers la plateforme (batchs, retries, backoff exponentiel)
               → un élément n'est retiré de la file QU'APRÈS accusé de réception plateforme
5. HEARTBEAT   État du run : compteurs, erreurs locales, taille de file restante, version agent
```

**Règles :**
- L'agent **ré-extrait sans état métier** : il ne sait pas ce qui est Issued/Blocked (c'est le
  travail de la plateforme). Il garde seulement un **filigrane d'extraction** (dernière période
  traitée) et la liste des (source_reference, payload_hash) déjà ACKés, pour ne pas re-pousser
  inutilement. La plateforme reste l'autorité anti-doublon.
- Si la donnée source change après un push (ré-extraction → hash différent pour la même
  source_reference), l'agent pousse le nouveau hash : c'est la plateforme qui détecte
  l'altération (F06 / SourceAlterationDetector) — jamais l'agent qui filtre.
- Coupure réseau pendant le push : les éléments restent en file, le run suivant (ou la boucle
  de retry) les reprend. **Aucune donnée n'est perdue, aucun doublon n'est créé** (idempotence §3.3).

### 2.3 File locale (SQLite)

| Table | Contenu | Rétention |
|---|---|---|
| `push_queue` | Éléments à pousser (type, payload JSON/chemin PDF, tentatives, dernière erreur) | Purgée après ACK |
| `pushed_log` | (source_reference, payload_hash, date ACK) | 90 jours (anti re-push, PAS une piste d'audit) |
| `agent_state` | Filigrane d'extraction, dernière config reçue | — |

> La piste d'audit légale vit sur la PLATEFORME (F06). La file locale est un tampon technique :
> sa perte ne perd aucune donnée légale (au pire, ré-extraction + déduplication plateforme).

### 2.4 Configuration de l'agent (fichier local)

```jsonc
// C:\ProgramData\Liakont\agent.json
{
  "platformUrl": "https://liakont.editeur-x.fr",
  "apiKey": "<chiffré DPAPI machine scope — jamais en clair>",
  "extraction": {
    "adapter": "EncheresV6",
    "odbcConnectionString": "<chiffré DPAPI>",
    "pdfPoolPath": "D:\\GestionVentes\\PDF",
    "schedule": ["03:00"],
    "catchUpOnStart": true
  },
  "heartbeatMinutes": 15
}
```

- Les secrets (clé API, chaîne ODBC) sont chiffrés **DPAPI machine scope** par
  `Liakont.Agent.Cli encrypt` lors de l'installation.
- La planification **effective** peut être pilotée par la plateforme (§3.2 `GET configuration`) :
  le fichier local est le défaut/secours, la plateforme peut la surcharger (pilotage centralisé).

### 2.5 Heartbeat et auto-update

- Heartbeat toutes les `heartbeatMinutes` (défaut 15 min), même hors run : version de l'agent,
  état du service, taille de la file, horodatage du dernier run, dernières erreurs.
- La réponse du heartbeat peut contenir : `latestAgentVersion`, `updateRequired` (bool),
  `updateUrl`. Si `updateRequired`, l'agent télécharge le package, vérifie le MANIFESTE DE
  VERSION SIGNÉ par la clé applicative dédiée (clé privée hors plateforme — décision D6
  2026-06-03, voir §7 décision n°4) ainsi que le hash du package, se remplace et redémarre
  (pattern updater : un petit exe séparé fait le swap). Le registre des versions et la
  politique de flotte sont côté plateforme (OPS07).
- Un agent dont la version n'est plus supportée (< N-1) est signalé en supervision et la
  plateforme peut refuser ses push (HTTP 426 Upgrade Required).

### 2.6 Ce que l'agent ne fait JAMAIS

- Aucune logique métier : pas de mapping TVA, pas de validation, pas de machine à états.
- Aucune écriture sur la base source (lecture seule stricte — règle CLAUDE.md n°5).
- Aucun appel aux PA (c'est la plateforme qui envoie).
- Aucun stockage de données au-delà du tampon technique (§2.3).
- Aucune écoute réseau entrante (HTTPS sortant uniquement — rien à ouvrir sur le firewall client).

## 3. Le contrat d'ingestion (API agent → plateforme)

### 3.1 Principes

| Principe | Mise en œuvre |
|---|---|
| **Versionné** | Préfixe d'URL `/api/agent/v1/` + version du contrat dans `Liakont.Agent.Contracts`. La plateforme supporte N et N-1 |
| **Authentifié** | Header `X-Agent-Key: <prefix>.<secret>` — la plateforme ne stocke que prefix + hash (modèle ApiKey du socle). Une clé = UN agent = UN tenant |
| **Idempotent** | Re-pousser un document déjà reçu (même `payload_hash`) → 200 `duplicate`, aucun effet |
| **Par lots** | Push par batch (max 100 documents), résultat individuel par document, lot NON transactionnel |
| **Sortant uniquement** | L'agent initie toutes les connexions ; la plateforme ne contacte jamais l'agent |

### 3.2 Endpoints

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/api/agent/v1/heartbeat` | État de l'agent → réponse : config effective + version attendue |
| GET | `/api/agent/v1/configuration` | Planification, période à extraire, capacités à activer |
| POST | `/api/agent/v1/documents/batch` | Push de documents pivot (résultat par document : `accepted` / `duplicate` / `rejected` + motif) |
| POST | `/api/agent/v1/documents/{sourceReference}/pdf` | PDF lié à un document (capacité ProvidesSourceDocuments) |
| POST | `/api/agent/v1/pdf-pool` | PDF non liés (capacité ProvidesUnlinkedDocumentPool → réconciliation F06/TRK08) |

### 3.3 Réponses et erreurs

| Code | Signification | Comportement agent |
|---|---|---|
| 200 | Reçu (`accepted` ou `duplicate` par élément) | Retire de la file les éléments ACKés |
| 400 | Payload invalide (contrat non respecté) | Met l'élément en erreur (pas de retry infini), le signale au heartbeat |
| 401/403 | Clé API invalide/révoquée | Stop + erreur explicite (CLI `test-api` pour diagnostiquer) |
| 413 | Lot trop gros | Re-découpe le lot |
| 426 | Version d'agent non supportée | Déclenche l'auto-update |
| 429/5xx | Surcharge / indisponibilité | Backoff exponentiel, les éléments restent en file |

### 3.4 DTOs (assemblage `Liakont.Agent.Contracts`, netstandard2.0)

- `PivotDocumentDto` (+ lignes, taxes, parties, totaux) — structure définie par **F01-F02**,
  sérialisation JSON canonique (ordre de propriétés stable → hash reproductible).
- `HeartbeatRequestDto` / `HeartbeatResponseDto`
- `PushBatchRequestDto` / `PushBatchResponseDto` (+ `DocumentPushResultDto`)
- `AgentConfigurationDto`
- **Règle absolue : DTOs purs, AUCUNE logique** (test d'architecture). Sérialisables à
  l'identique par Newtonsoft.Json (agent) et System.Text.Json (plateforme) — garanti par les
  **tests de contrat golden files** exécutés des deux côtés.

## 4. Côté plateforme : le module Ingestion

### 4.1 Réception

```
POST documents/batch
  → auth clé API → résolution du tenant
  → par document :
      payload_hash déjà connu pour ce tenant ?      → duplicate (aucun effet)
      source_reference connue avec un autre hash ?   → accepted + événement AlterationDetected (F06)
      sinon                                          → accepted : Document créé en état Detected
  → publication d'un événement (outbox) → déclenche le pipeline aval (mapping → validation → ...)
```

L'ingestion ne transforme **rien** : elle enregistre, déduplique, journalise et délègue.

### 4.2 Gestion des agents

- Écran console (droit « paramétrage ») : enregistrer un agent pour un tenant → génère la clé
  API (affichée UNE fois), révocation, rotation.
- Un tenant peut avoir plusieurs agents (rare : multi-sites), chacun sa clé.
- Historique des heartbeats conservé 90 jours (supervision, pas audit légal).

## 5. Supervision proactive

### 5.1 Le principe : dead-man's switch

C'est **la plateforme qui détecte l'absence**, pas l'agent qui signale sa présence. Un job
planifié (module Job, toutes les 15 min) évalue les règles d'alerte sur tous les tenants
de l'instance.

### 5.2 Règles d'alerte (paramétrables par tenant, valeurs par défaut)

| Règle | Seuil défaut | Gravité |
|---|---|---|
| Agent muet (aucun heartbeat) | > 24 h | 🔴 Critique |
| Run d'extraction manqué (heartbeat OK mais pas de run) | > 36 h | 🔴 Critique |
| File de push qui grossit (erreurs répétées) | > 50 éléments ou > 6 h | 🟠 Avertissement |
| Documents bloqués non traités | > 5 jours | 🟠 Avertissement |
| Rejets PA non traités | > 2 jours | 🔴 Critique |
| Échéance de période déclarative proche avec documents non transmis | J-3 | 🔴 Critique |
| Version d'agent obsolète (< N-1) | — | 🟠 Avertissement |

### 5.3 Destinataires et canaux

- **Opérateur de l'instance** (éditeur ou IT Innovations) : toutes les alertes, dashboard +
  email (module Notification).
- **Contact du tenant** (optionnel, paramétrable) : alertes critiques uniquement, email.
- Le dashboard de supervision (module Supervision, console web) montre la santé de tous les
  tenants de l'instance : dernier heartbeat, file, documents par état, alertes actives.

## 6. Configuration et déploiement

### 6.1 Les trois niveaux de configuration

| Niveau | Où | Quoi | Qui modifie |
|---|---|---|---|
| **Instance** | `appsettings`/variables d'environnement (Docker) | PostgreSQL, Keycloak, SMTP, branding (nom, logo, couleurs, domaine) | Opérateur de l'instance (déploiement) |
| **Tenant** | En base (par tenant), édité via la console | SIREN, raison sociale, table TVA (+ validation expert-comptable), compte(s) PA + clés chiffrées, planification, seuils d'alerte, contact | Droit « paramétrage » (journalisé, revalidation) |
| **Agent** | Fichier local + DPAPI chez le client | URL plateforme, clé API, ODBC, planification locale | Installateur / CLI agent |

> Le **détail du niveau Tenant** (profil, paramétrage fiscal, comptes PA, planification, seuils,
> droits par section, seed `deployments/<client>/`) est spécifié dans l'annexe
> [F12-A — Paramétrage par tenant](F12-A-Parametrage-Tenant.md).

### 6.2 L'appliance Docker (lot OPS)

```yaml
# deploy/docker/docker-compose.yml (structure cible)
services:
  liakont:        # Liakont.Host (.NET 10)
  postgres:         # PostgreSQL 16+ (volumes persistants)
  keycloak:         # Keycloak + realm Liakont importé au démarrage
# + reverse proxy / TLS selon l'environnement (Caddy/Traefik ou celui de l'hébergeur)
```

- **Self-hosted éditeur** : l'éditeur déploie ce compose sur son serveur, pointe son DNS.
- **Instances hébergées** : même compose, provisionné par IT Innovations (un répertoire/stack
  par instance).
- **Sauvegardes** : `pg_dump` quotidien + volume d'archive — la procédure fait partie du
  livrable appliance (pas une option).

### 6.3 Provisioning

| Opération | Outil | Résultat |
|---|---|---|
| Créer une **instance** (éditeur) | Script `deploy/provisioning/new-instance.ps1` | Stack Docker + realm Keycloak + base + branding |
| Créer un **tenant** (client final) | Écran console (opérateur d'instance) | Base tenant + paramétrage initial + utilisateurs |
| Enregistrer un **agent** | Écran console (droit paramétrage) | Clé API + package d'installation pré-configuré |
| **Réversibilité** tenant | Export console : tracking + archive + paramétrage | Dossier complet remis au client |
| **Réversibilité** instance | `pg_dump` + volumes + DNS | Migration dédiée-hébergée → self-hosted |

### 6.4 Matrice de compatibilité

| Plateforme | Agents supportés |
|---|---|
| v1.x | Contrat v1 |
| v2.x | Contrats v2 et v1 (N-1) |

La CI vérifie que les golden files du contrat N-1 passent toujours sur la plateforme N.

## 7. Décisions à valider ensemble

| # | Décision | Options | Recommandation |
|---|---|---|---|
| 1 | Auth des instances | Keycloak (par instance / mutualisé) / alternative légère in-process (OpenIddict) | **Direction tranchée (2026-06-03, D10)** : l'auth est mise DERRIÈRE une abstraction d'IdP dès SOL01 (Keycloak n'est qu'une implémentation, non figée) + spike comparatif d'empreinte Keycloak vs OpenIddict ; le choix final est pris sur MESURE (l'empreinte ~1-2 GB de Keycloak pèse sur les petites instances self-hosted). La topologie du retenu est tranchée à l'ADR OPS01 |
| 2 | Stockage du coffre d'archive | FileSystem / S3-compatible / Azure Blob / GCS | **Tranché (2026-06-03, D9)** : `IArchiveStore` est un 3ᵉ axe enfichable à CAPACITÉS, choisi par l'éditeur au niveau instance. V1 = FileSystem (appliance) + S3-compatible (Amazon, MinIO, OVH, Scaleway — un seul code) ; Azure Blob et GCS = plug-ins fast-follow à la demande. L'intégrité (hashes chaînés + ancrage) reste produit, indépendante du WORM natif du backend ; Object Lock / immutable blob utilisés EN PLUS quand disponibles |
| 3 | Pilotage de la planification d'extraction | Fichier agent seul / plateforme prioritaire | **Plateforme prioritaire** (pilotage centralisé), fichier local en secours |
| 4 | Signature des packages d'auto-update | Authenticode / hash publié via l'API / manifeste signé par clé dédiée | **TRANCHÉ (2026-06-03, décision D6)** : manifeste de version SIGNÉ par une clé applicative dédiée (clé privée hors plateforme — poste de release) + hash du package, vérifiés par l'agent en V1. Authenticode s'ajoute dès qu'un certificat de signature de code est disponible. Motif : un hash seul publié par la même API que `updateUrl` ne protège pas le client final si la plateforme est compromise |
| 5 | Transport des PDF | Multipart dans le batch / endpoint séparé | **Endpoint séparé** (les PDF sont gros, le batch documents reste léger) |
| 6 | Reverse proxy de l'appliance | Inclus (Caddy) / délégué à l'hébergeur | Inclus en self-hosted (TLS automatique), délégué en hébergé |
