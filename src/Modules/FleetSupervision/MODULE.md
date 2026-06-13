# Module FleetSupervision

## Purpose

Méta-supervision de **flotte** (OPS04, F12 §6) : le niveau **au-dessus des tenants**. Là où le module
`Supervision` surveille les **tenants d'UNE instance**, `FleetSupervision` permet à **IT Innovations** (l'exploitant)
de surveiller les **INSTANCES** de la plateforme (opérées et self-hosted souscrites).

Deux rôles, pilotés par configuration (`FleetSupervision` dans les appsettings), **désactivés par défaut** :

- **Reporting** (sur chaque instance) : un job SYSTÈME (`InstanceHeartbeatTrigger` /
  `InstanceHeartbeatSendHandler`) collecte la télémétrie **technique** locale (version, santé Host/PostgreSQL/
  Keycloak, **nombre** de tenants, espace disque, dernière sauvegarde réussie) et la **POST** au central.
- **Central** (instance mutualisée IT Innovations) : reçoit les heartbeats (`POST /api/fleet/v1/heartbeat`,
  authentifié par clé `X-Fleet-Key`), persiste l'état du parc (base **système**, schéma `fleet`), expose le
  **dashboard de flotte** (`/flotte`, permission `liakont.fleet`) et **notifie** par email les instances
  self-hosted en retard sur la dernière version publiée (`FleetUpdateNotificationTrigger`).

Les **alertes** de flotte (instance muette / sauvegarde en échec / version obsolète) sont **calculées** à la
lecture (`FleetAlertEvaluator`, fonction pure) à partir de l'état stocké + des seuils — **non persistées** (à
la différence des alertes tenant du module Supervision, qui ont un cycle de vie).

## Cloisonnement éditeur (acceptance OPS04)

La télémétrie ne transporte **QUE** des métriques techniques — **jamais** de donnée métier d'un éditeur (pas
de nom de tenant, pas de SIREN, pas de compteur de documents, aucun montant). `TenantCount` est un **entier**,
jamais une liste de noms. La forme du contrat `InstanceHeartbeatReport` est verrouillée par
`InstanceHeartbeatReportIsolationTests` (liste blanche de propriétés + JSON sérialisé sans jeton métier) :
ajouter un champ portant une donnée d'éditeur fait **échouer le test**.

## Boundaries

- **Schéma owné** : `fleet` (PostgreSQL, base **SYSTÈME** du central). `fleet.instances` = dernière télémétrie
  connue par instance (état opérationnel **mutable**, upsert par instance ; `first_seen_utc` et
  `notified_version` préservés). Le store cible exclusivement la base système via `ISystemConnectionFactory`.
- **Mécanique de migration** : le runner applique les scripts d'un module sur la base système **ET** sur
  chaque base tenant. Le schéma `fleet` est donc aussi créé, **vide et inutilisé**, dans les bases tenant —
  sans effet (aucune écriture tenant n'y arrive) ni fuite cross-tenant.
- **Dépendances inter-module** : uniquement par les **Contracts** (module-rules §3) — `Job.Contracts`
  (jobs système) et `Notification.Contracts` (`IEmailTransport`). Aucun secret porté par le module (la clé
  d'ingestion et le mot de passe SMTP sont du paramétrage d'instance, côté Host). Gardé par
  `FleetSupervisionBoundaryTests`.
- **Endpoint central** : vit dans le Host (`Liakont.Host.FleetApi`), comme l'API agent — authentifié par clé
  (pas OIDC), actif seulement quand le rôle central est activé (sinon 404).

## Notes de déploiement

- Le **central** configure `FleetSupervision:Central` (`Enabled`, `IngestionKey` [secret], `LatestVersion`,
  seuils) ; chaque **instance** configure `FleetSupervision:Reporting` (`Enabled`, `CentralUrl`, `InstanceId`,
  `HostingMode`, `FleetKey` [secret], `ContactEmail`, `BackupMarkerPath`, `KeycloakProbeUrl`, `DataPath`).
- La **dernière sauvegarde réussie** est lue depuis un **fichier marqueur** que le job de sauvegarde (OPS01b)
  touche à chaque succès (`BackupMarkerPath`) — pas de couplage aux internes d'OPS01b.
- La **dernière version publiée** (`LatestVersion`) est un paramétrage du central en V1 ; un branchement sur la
  publication de versions (OPS07) la fournira automatiquement plus tard.
- La **planification** (cron) des deux jobs est un geste opérateur via l'admin des planifications (comme la
  supervision tenant et l'ancrage du coffre) — non amorcée par défaut.
