# Ingestion Module

> Module métier Liakont (namespace `Liakont.Modules.*`). Spec : `docs/conception/F12-Architecture-Plateforme-Agent.md` (§3 contrat d'ingestion, §4 module Ingestion). Items PIV05 (gestion des agents, clés API, heartbeat, configuration) et PIV04 (RÉCEPTION des documents : batch, anti-doublon, altération, PDF, régimes source), sur la même API agent.

## Purpose

Porte d'entrée de la plateforme pour les agents installés chez les clients.

PIV05 couvre :

1. **Gestion des agents** : entité `Agent` (cycle de vie register/revoke/rotate), une clé API par
   agent, modèle `prefix + hash` (la clé complète n'est affichée qu'une fois).
2. **Authentification par clé API** (`IAgentAuthenticator`) : en-tête `X-Agent-Key` → résolution du
   préfixe vers l'agent → vérification d'empreinte → identité (agent + tenant). C'est la
   **résolution du tenant** réutilisée par l'ingestion des documents (PIV04).
3. **Heartbeat** : persistance de l'état (historique append-only, rétention 90 j) + réponse de
   configuration.
4. **Configuration d'agent** : `IAgentConfigurationProvider` — défaut SÛR tant que le registre de
   versions (OPS07) et la planification pilotée par le tenant (F12 D3) n'existent pas.

PIV04 couvre la **réception des documents** (agent authentifié, tenant résolu) :

5. **Réception par lots** (`IngestDocumentBatchCommand`) : lot NON transactionnel, **résultat
   individuel par document** (`accepted` / `duplicate` / `rejected`), 100 documents max (413 au-delà),
   document malformé rejeté entièrement (jamais d'acceptation partielle).
6. **Anti-doublon par tenant** : empreinte canonique du payload (PIV02) ; un payload déjà reçu pour
   le tenant → `duplicate` sans effet (protège du re-push complet d'un agent réinstallé).
7. **Détection d'altération** (F06) : même `source_reference`, empreinte différente → `accepted` +
   événement `SourceAlterationDetected` (consommé par TRK03).
8. **Régimes de TVA source** : métadonnée de push persistée par tenant (upsert), consommée par TVA03.
9. **Stockage PDF par tenant** (`IIngestedPdfStore`, système de fichiers — ADR-0008) : PDF rattaché
   ou pool de réconciliation, sans dépendance au module Document du socle.

Le module ne **transforme** rien : il enregistre, authentifie, journalise, **dédoublonne** et
**délègue** (le mapping/validation/états vivent dans les modules métier aval). La création du document
en état `Detected` est déléguée au port `IDocumentIntake` (no-op sûr jusqu'à TRK02) ; le déclencheur
durable du pipeline aval (PIP01) est l'événement `DocumentReceived` publié via l'outbox.

## Type / Schema

Schéma `ingestion`. **Particularité de placement** : le registre d'agents (`agents`) et l'historique
des heartbeats (`agent_heartbeats`) vivent dans la base **SYSTÈME** (partagée), pas dans une base
tenant. C'est nécessaire : l'authentification d'une clé API doit résoudre son tenant **avant**
d'ouvrir la base de ce tenant (l'auth précède tout contexte tenant — F12 §3.1), exactement comme le
registre `outbox.tenants`. Le schéma `ingestion` est aussi créé (vide) dans les bases tenant par le
mécanisme de migration partagé : c'est un artefact bénin ; les données ne vivent que dans la base
système et l'accès passe par `ISystemConnectionFactory`.

## Boundaries

- **Owns :** schéma `ingestion` (tables `agents`, `agent_heartbeats`, `received_documents`,
  `source_tax_regimes`) dans la base SYSTÈME, et le stockage FICHIER des PDF reçus (par tenant, racine
  de déploiement — ADR-0008).
- **Reads / Writes :** son propre schéma uniquement, via `ISystemConnectionFactory`. Les opérations
  de gestion (register/revoke/rotate/list) ET la réception (anti-doublon, régimes source) sont scopées
  au `tenant_id` courant — résolu depuis l'agent authentifié, jamais le corps (anti-fuite cross-tenant).
  La résolution de clé (authentification) est cross-tenant par nécessité — c'est de l'infrastructure
  d'auth, pas une requête métier.
- **Does NOT :** ne transforme aucune donnée ; aucune logique fiscale (régimes source conservés BRUTS) ;
  n'invente aucune configuration (défaut sûr documenté) ; n'expose jamais une clé API (ni claire ni son
  empreinte) ; aucun chemin d'update/delete sur l'historique des heartbeats ni sur le registre de
  réception (append-only) ; ne porte ni machine à états ni piste d'audit de document (délégués au module
  Documents via `IDocumentIntake`).

## Type / Schema — réception (PIV04)

- `received_documents` (base SYSTÈME) : registre d'anti-doublon append-only ; `tenant_id`, `source_reference`,
  `payload_hash` (empreinte canonique PIV02), `document_id`, `contract_version`, `received_at`. Index unique
  `(tenant_id, payload_hash)` = anti-doublon par tenant ; index `(tenant_id, source_reference, received_at DESC)`
  = détection d'altération + dernier hash connu.
- `source_tax_regimes` (base SYSTÈME) : régimes source observés ; clé `(tenant_id, code)`, `label`,
  `occurrences` (DERNIÈRE observation — remplacée, non cumulée → upsert idempotent au retry), `last_seen_at`.
  Code BRUT, jamais interprété.

## Endpoints (API agent → plateforme)

Groupe `/api/agent/v1` (distinct de l'API console `/api/v{version}` OIDC), authentifié par
`X-Agent-Key` via le filtre d'authentification agent (Host), protégé par rate limiting (brute force
par IP). En-tête `X-Contract-Version` négocié (426 si inconnue/trop ancienne).

| Méthode | Route | Permission | Description |
|---|---|---|---|
| POST | `/api/agent/v1/heartbeat` | clé API agent | État de l'agent → heure serveur + configuration |
| GET | `/api/agent/v1/configuration` | clé API agent | Configuration courante (démarrage de l'agent) |
| POST | `/api/agent/v1/documents/batch` | clé API agent | Push d'un lot (≤100) → résultat par document (PIV04) |
| POST | `/api/agent/v1/documents/{sourceReference}/pdf` | clé API agent | PDF rattaché à un document (PIV04) |
| POST | `/api/agent/v1/pdf-pool` | clé API agent | PDF non rattaché → pool de réconciliation (PIV04) |

> Heartbeat/configuration utilisent la policy de rate limiting anti-flood (`agent-api`) ; l'ingestion
> (batch + PDF) utilise une policy distincte dimensionnée pour le débit (`agent-api-ingestion`, PIV04).

## Cross-module Interfaces

| Interface | Projet | Description |
|---|---|---|
| `IAgentAuthenticator` | Contracts | Authentifie une clé API → identité (agent + tenant). Consommé par le filtre d'auth du Host et par l'ingestion des documents (PIV04). |
| `IAgentQueries` | Contracts | Liste les agents d'un tenant (console, supervision). |
| `ISourceTaxRegimeQueries` | Contracts | Liste les régimes source observés d'un tenant (consommé par TVA03). |
| `IDocumentIntake` | Contracts | Port de création d'un document en état `Detected` (implémenté par TRK02 ; no-op sûr d'ici là). |
| `IIngestedPdfStore` | Contracts | Stockage fichier des PDF reçus par tenant (consommé directement par le Host, comme `IAgentAuthenticator` — streaming, non-MediatR). |
| Commandes/Requêtes MediatR | Contracts | `RegisterAgent`, `RevokeAgent`, `RotateAgentKey`, `RecordHeartbeat`, `GetAgentConfiguration`, `GetAgents`, `IngestDocumentBatch`. |

## Published Events

| Type | Payload | Déclencheur | Consommateur |
|---|---|---|---|
| `ingestion.document.received` | `DocumentReceivedV1` | Tout document accepté (nouveau ou altéré) | PIP01 (pipeline aval) |
| `ingestion.source.altered` | `SourceAlterationDetectedV1` | Référence source connue, empreinte différente (F06) | TRK03 (piste d'audit d'altération) |

> Les deux événements sont écrits dans l'outbox DANS LA MÊME TRANSACTION que l'inscription au registre
> de réception (cohérence transactionnelle), base SYSTÈME (drainée par le worker d'outbox du socle).
> Le pivot COMPLET est stagé durablement (`IPayloadStagingStore`, PIP00/ADR-0014) AVANT ce commit : un
> `DocumentReceived` n'est jamais publié sans contenu déjà stagé (INV-INGESTION-017).

## Consumed Events

Aucun.

## Dependencies

- `Common.Abstractions` (MediatR/Messaging, MultiTenancy `ITenantContext`, Exceptions, `IntegrationEvent`).
- `Common.Infrastructure` (Dapper, migrations DbUp, `ISystemConnectionFactory`, `TransactionScope`, `IOutboxWriter`, `IEventTypeRegistry`).
- `Liakont.Agent.Contracts` (DTOs de transport et pivot, sérialisation canonique `CanonicalJson`/`PayloadHasher`, en-têtes du contrat).
- `Staging.Contracts` (`IPayloadStagingStore`, PIP00/ADR-0014) : l'intake stage le pivot complet AVANT le commit registre+outbox (Contracts uniquement — frontière respectée, CLAUDE.md n°14).
- Aucune dépendance vers le Domain/Infrastructure d'un autre module (frontière Contracts-only respectée ; `IDocumentIntake` est un port local).
