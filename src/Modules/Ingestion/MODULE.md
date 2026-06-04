# Ingestion Module

> Module métier Liakont (namespace `Liakont.Modules.*`). Spec : `docs/conception/F12-Architecture-Plateforme-Agent.md` (§3 contrat d'ingestion, §4 module Ingestion). Item PIV05 (gestion des agents, clés API, heartbeat, configuration). La RÉCEPTION des documents (batch, PDF, anti-doublon) est livrée par PIV04 sur la même API agent.

## Purpose

Porte d'entrée de la plateforme pour les agents installés chez les clients. PIV05 couvre :

1. **Gestion des agents** : entité `Agent` (cycle de vie register/revoke/rotate), une clé API par
   agent, modèle `prefix + hash` (la clé complète n'est affichée qu'une fois).
2. **Authentification par clé API** (`IAgentAuthenticator`) : en-tête `X-Agent-Key` → résolution du
   préfixe vers l'agent → vérification d'empreinte → identité (agent + tenant). C'est la
   **résolution du tenant** réutilisée par l'ingestion des documents (PIV04).
3. **Heartbeat** : persistance de l'état (historique append-only, rétention 90 j) + réponse de
   configuration.
4. **Configuration d'agent** : `IAgentConfigurationProvider` — défaut SÛR tant que le registre de
   versions (OPS07) et la planification pilotée par le tenant (F12 D3) n'existent pas.

Le module ne **transforme** rien : il enregistre, authentifie, journalise l'activité des agents et
délègue (le mapping/validation/états vivent dans les modules métier aval).

## Type / Schema

Schéma `ingestion`. **Particularité de placement** : le registre d'agents (`agents`) et l'historique
des heartbeats (`agent_heartbeats`) vivent dans la base **SYSTÈME** (partagée), pas dans une base
tenant. C'est nécessaire : l'authentification d'une clé API doit résoudre son tenant **avant**
d'ouvrir la base de ce tenant (l'auth précède tout contexte tenant — F12 §3.1), exactement comme le
registre `outbox.tenants`. Le schéma `ingestion` est aussi créé (vide) dans les bases tenant par le
mécanisme de migration partagé : c'est un artefact bénin ; les données ne vivent que dans la base
système et l'accès passe par `ISystemConnectionFactory`.

## Boundaries

- **Owns :** schéma `ingestion` (tables `agents`, `agent_heartbeats`) dans la base SYSTÈME.
- **Reads / Writes :** son propre schéma uniquement, via `ISystemConnectionFactory`. Les opérations
  de gestion (register/revoke/rotate/list) sont scopées au `tenant_id` courant (anti-fuite
  cross-tenant). La résolution de clé (authentification) est cross-tenant par nécessité — c'est de
  l'infrastructure d'auth, pas une requête métier.
- **Does NOT :** ne transforme aucune donnée ; aucune logique fiscale ; n'invente aucune
  configuration (défaut sûr documenté) ; n'expose jamais une clé API (ni claire ni son empreinte) ;
  aucun chemin d'update/delete sur l'historique des heartbeats.

## Endpoints (API agent → plateforme)

Groupe `/api/agent/v1` (distinct de l'API console `/api/v{version}` OIDC), authentifié par
`X-Agent-Key` via le filtre d'authentification agent (Host), protégé par rate limiting (brute force
par IP). En-tête `X-Contract-Version` négocié (426 si inconnue/trop ancienne).

| Méthode | Route | Permission | Description |
|---|---|---|---|
| POST | `/api/agent/v1/heartbeat` | clé API agent | État de l'agent → heure serveur + configuration |
| GET | `/api/agent/v1/configuration` | clé API agent | Configuration courante (démarrage de l'agent) |

> POST `documents/batch`, `documents/{ref}/pdf`, `pdf-pool` sont livrés par **PIV04** sur ce groupe.

## Cross-module Interfaces

| Interface | Projet | Description |
|---|---|---|
| `IAgentAuthenticator` | Contracts | Authentifie une clé API → identité (agent + tenant). Consommé par le filtre d'auth du Host et, à venir, par l'ingestion des documents (PIV04). |
| `IAgentQueries` | Contracts | Liste les agents d'un tenant (console, supervision). |
| Commandes/Requêtes MediatR | Contracts | `RegisterAgent`, `RevokeAgent`, `RotateAgentKey`, `RecordHeartbeat`, `GetAgentConfiguration`, `GetAgents`. |

## Published Events

Aucun en PIV05. (L'événement `DocumentReceived` — déclencheur du pipeline aval — est publié par
PIV04 lors de la réception des documents.)

## Consumed Events

Aucun.

## Dependencies

- `Common.Abstractions` (MediatR/Messaging, MultiTenancy `ITenantContext`, Exceptions).
- `Common.Infrastructure` (Dapper, migrations DbUp, `ISystemConnectionFactory`, `TransactionScope`).
- `Liakont.Agent.Contracts` (DTOs de transport : heartbeat, configuration ; en-têtes du contrat).
- Aucune dépendance vers un autre module métier (frontière Contracts-only respectée).
