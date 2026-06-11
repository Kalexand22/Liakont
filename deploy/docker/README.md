# Environnement de développement local — console Liakont

Amorçage complet de la console web depuis les artefacts committés, sans intervention
manuelle (bug-inbox « amorçage console »). Tout ce qui suit est du **dev local
uniquement** : identifiants fictifs, secrets placeholder, aucune donnée client.

> **Déploiement d'une instance complète (production / self-hosted)** : ce dossier ne couvre que le
> Keycloak de **dev** (PostgreSQL et Host tournent sur la machine). L'**appliance Docker** complète
> (Host + PostgreSQL + Keycloak + Caddy/TLS, secrets sans valeur par défaut) est dans
> [`appliance/`](appliance/README.md) — item OPS01a, F12 §6.2.

## 1. Prérequis

- Docker (Keycloak + sa base) ;
- PostgreSQL local sur `localhost:5432` avec la base `liakont`
  (utilisateur `liakont` / `liakont_dev_password`, cf. `appsettings.Development.json`).

## 2. Keycloak

```powershell
powershell -ExecutionPolicy Bypass -File tools/keycloak-dev.ps1 start
```

(équivaut à `docker compose -f deploy/docker/docker-compose.keycloak.yml up -d`, et attend en plus
que le realm soit effectivement importé et joignable.)

Le realm `liakont-dev` est importé automatiquement depuis `keycloak/realm-export.json`.

> **Realm déjà importé / périmé ?** L'import (`--import-realm`, stratégie `IGNORE_EXISTING`) ne
> réimporte PAS un realm `liakont-dev` déjà présent dans le volume. Un poste ayant déjà servi garde
> donc l'ANCIEN realm (anciens comptes) et la console répond « identifiants invalides » sans signal.
> Remède — réinitialiser proprement (supprime le volume puis réimporte) :
>
> ```powershell
> powershell -ExecutionPolicy Bypass -File tools/keycloak-dev.ps1 reset
> ```
>
> Au démarrage du Host en `Development`, un diagnostic best-effort vérifie le realm et émet un
> avertissement explicite dans les logs s'il est joignable mais périmé (compte attendu absent),
> en rappelant la commande `reset` ci-dessus. Actions disponibles du script : `start`, `stop`,
> `reset`, `status`. Console d'admin Keycloak : http://localhost:8080 (`admin`/`admin`).

### Utilisateurs de test (mot de passe : `Test@1234`)

| Username | Rôles realm | Usage |
|---|---|---|
| `lecture` | lecture | Consultation seule |
| `operateur` | lecture, operateur | Actions opérateur (envoi, déblocage) |
| `parametrage` | + parametrage | Table TVA, gestion des agents d'extraction |
| `superviseur` | + superviseur | Supervision cross-tenant |
| `sysadmin` | stratum-admin | **Super-admin d'instance** : admin des planifications de jobs, provisioning tenant |

Les usernames sont **courts** (pas des e-mails) : le value object Username du module
Identity n'accepte que 3-50 caractères alphanumériques (la connexion par e-mail reste
possible, `loginWithEmailAllowed`). Tous les utilisateurs portent un claim `company_id`
codé en dur dans le client (`company_id (societe fictive de dev)`) : les pages
company-scopées (webhooks, intégrations) sont exerçables en dev.

## 3. Tenant de développement

Au démarrage en environnement `Development`, le Host amorce lui-même le tenant `default`
dans `outbox.tenants` (section `DevTenantSeed` d'`appsettings.Development.json`), rattaché
au realm `liakont-dev` et à la base partagée `liakont`. Aucune insertion SQL manuelle,
aucun appel `/admin/tenants` n'est nécessaire en dev.

> Le provisioning réel d'un tenant (réservé production) passe par `POST /admin/tenants`
> (rôle `SystemAdmin` requis) et crée une base et un realm dédiés — hors périmètre dev.

## 4. Lancer la console

```powershell
dotnet run --project src/Host/Liakont.Host
```

`src/Host/Liakont.Host/Properties/launchSettings.json` est **versionné** (profil `Development`,
`applicationUrl http://localhost:55996`) : `dotnet run` seul démarre donc en environnement
`Development` sur le port **55996** (le seul présent dans les redirectUris du realm), sans qu'il
faille poser `ASPNETCORE_ENVIRONMENT` ni `ASPNETCORE_URLS` à la main.

Accès : **http://localhost:55996** ou **http://default.localhost:55996**.

Deux résolveurs de tenant couvrent le circuit Blazor :

- **sous-domaine** (`default.localhost`) : `SubdomainTenantResolver` ;
- **hôte nu** (`localhost`) : `OidcIssuerTenantResolver` via le claim `iss` conservé
  dans le cookie de session (mappé au sign-in OIDC).

### Accès super-admin (admin des planifications, provisioning tenant)

L'admin des planifications de jobs (`/admin/jobs`) et le provisioning de tenant exigent un
**super-admin**. Sous OIDC, ce statut vient d'un **claim de rôle realm** (`stratum-admin`), pas
d'un utilisateur Identity en base. Le realm committé fournit le compte **`sysadmin`** (mot de
passe `Test@1234`) porteur du rôle realm `stratum-admin` : se connecter avec lui suffit pour voir
la section **Jobs**.

`AdminSeed` (section `AdminSeed` d'`appsettings.Development.json`) amorce en plus l'utilisateur
Identity correspondant (par défaut `ExternalId` = le sub de `sysadmin`) pour lui accorder les
permissions des modules socle. **L'amorçage est tolérant** : si un utilisateur portant déjà cet
`ExternalId` existe (auto-provisionné à une connexion OIDC antérieure), il est promu / no-op
journalisé — **jamais un crash du Host** (un `ExternalId` mal configuré ne casse pas l'instance).

## 5. Planifications des jobs système (supervision, ancrage du coffre)

Deux jobs SYSTÈME récurrents protègent la plateforme :

| Job | Cadence | Source |
|---|---|---|
| Évaluation de la supervision (dead-man's-switch) | toutes les 15 min (`*/15 * * * *`) | F12 §5.1 |
| Ancrage quotidien du coffre d'archive (TRK06) | quotidien (`0 0 * * *`) | ADR-0011 |

> **Les expressions cron sont interprétées en UTC** (parseur Cronos du `JobScheduler`). Un cron
> `0 0 * * *` se déclenche donc à **minuit UTC**, pas à l'heure locale.

- **En développement** : le Host amorce ces planifications automatiquement au démarrage
  (`DevJobScheduleSeeder`, Development uniquement, **create-only** — un schedule existant n'est
  jamais écrasé). Aucun geste manuel.
- **En production** : la planification est un **geste opérateur** via l'admin des planifications
  (`/admin/jobs`, super-admin) — la fréquence et l'activation relèvent du déploiement (ADR-0011).
- Dans **tous les environnements**, un diagnostic de démarrage best-effort
  (`SystemJobScheduleHealthCheck`) émet un **avertissement explicite** dans les logs si un de ces
  jobs système n'a **aucune planification active** (sinon la supervision serait muette et le coffre
  jamais ancré, sans aucun signal — constat de la recette).

## 6. Données de démonstration

```powershell
powershell -ExecutionPolicy Bypass -File tools/dev-seed-demo-docs.ps1 -AgentKey "prefix.secret"
```

(Enregistrer d'abord un agent d'extraction via la page « Agents d'extraction » avec
l'utilisateur `parametrage` pour obtenir une clé API — jamais versionnée.)

Les 15 documents fictifs sont datés **relativement à la date du jour** (répartis sur les derniers
mois, plusieurs dans le mois courant) — la page Documents, filtrée par défaut sur le mois courant,
n'est donc pas vide après le seed. Chaque document porte une **ligne complète** (régime source brut
`20`/`10`/`5.5`/`0`, ventilation de TVA) : une fois la table de mapping TVA créée et validée pour ces
régimes (page « Paramétrage comptable — Table TVA »), le parcours nominal *ingérer → vérifier →
envoyer* est déroulable jusqu'à « Prêt à envoyer ».
