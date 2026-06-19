# Environnement de démo — Bucodi

Stack **Liakont auto-portée, isolée et locale**, habillée aux couleurs de **Bucodi**, pour la recette
des gates et la démonstration commerciale. Tout tourne en `http` sur `localhost` (pas de DNS ni TLS) :
c'est un **bac à démo jetable**, pas un déploiement de production.

> Pour un déploiement réel (DNS public, TLS Let's Encrypt, secrets `.env`, mode Production), voir
> [`deploy/docker/appliance/`](../../deploy/docker/appliance/README.md). La présente stack en est une
> variante locale (sans Caddy, mode Development auto-seed).

## Contexte de la branche

`demo/bucodi` regroupe deux lots pour la recette d'ensemble :
- `feat/signature` (snapshot `b0dd8e3`) — signature/validation de document, FacturX, autofacturation 389 ;
- `feat/deploiement-toolkit` (snapshot `9ef9400`) — toolkit OPS (provisioning, migration d'instance,
  fin de vie tenant, export de réversibilité).

## Prérequis

- **Docker Desktop** (Engine ≥ 24, Compose v2) démarré.
- ~4 Go de RAM libres, ~5 Go de disque.
- Ports libres sur la machine : **8090** (console), **8081** (Keycloak), **5442** (PostgreSQL).

## Démarrage rapide

```powershell
# depuis deployments/bucodi/
powershell -ExecutionPolicy Bypass -File demo.ps1 up
```

Le script construit l'image du Host, démarre les 4 services, attend que le realm Keycloak et la console
répondent, puis affiche les accès. Premier `up` : quelques minutes (build de l'image + import du realm).

| Action | Commande |
|---|---|
| Démarrer | `demo.ps1 up` |
| Arrêter (garder les données) | `demo.ps1 down` |
| Remettre à zéro (vider base + realm) | `demo.ps1 reset` |
| État + sondes | `demo.ps1 status` |
| Suivre les logs | `demo.ps1 logs` |

Équivalent manuel : `docker compose -p liakont-bucodi up -d --build`.

## Accès

| Service | URL | Identifiants |
|---|---|---|
| Console Liakont (Bucodi) | http://localhost:8090 | comptes du realm (ci-dessous) |
| Keycloak (admin IdP) | http://localhost:8081/admin | `admin` / `admin` |
| PostgreSQL plateforme | `localhost:5442` | `liakont` / `liakont_demo_pwd` (base `liakont`) |

### Connexion (realm `bucodi`)

La base démarre à **0 tenant** (RB2) ; le realm ne contient qu'un **super-admin** d'amorçage. On crée
tenants et utilisateurs **proprement depuis la console** (RB4).

| Compte | Mot de passe | Rôle |
|---|---|---|
| `sysadmin` | `Test@1234` (temporaire) | `stratum-admin` — super-admin d'instance |

**Parcours démo :**
1. Se connecter en `sysadmin` → **changement de mot de passe forcé** + **enrôlement 2FA TOTP**
   (QR à scanner : Google Authenticator / FreeOTP / Microsoft Authenticator) — RB3.
2. Le super-admin atterrit sur **Supervision** (contexte cross-tenant, sans entrées ni tenant
   imposés — RB1).
3. **Clients › « Nouveau client »** : créer le tenant Bucodi (provisioning : base + enregistrement).
4. **Clients › « Gérer les utilisateurs »** : créer les comptes du tenant (lecture/operateur/
   parametrage/superviseur) et réinitialiser les mots de passe — RB4, sans console Keycloak.

> Démo sans friction : pour éviter l'enrôlement 2FA + le changement de mot de passe, désactiver les
> actions requises `CONFIGURE_TOTP` / `UPDATE_PASSWORD` dans la console admin Keycloak (realm `bucodi`
> → Authentication → Required actions) — à ne PAS faire en production.

## Ce qui est habillé « Bucodi »

Le branding passe par le mécanisme d'instance **BRD01** (section `Branding`, sans toucher au socle
vendored Stratum) :
- **Nom commercial** : « Bucodi » (titre d'onglet, barre latérale, écran de connexion, emails).
- **Couleurs** (charte réelle Bucodi) : primaire charbon `#1B1D20` (barre latérale + CTA), accent
  jaune signature `#FFE96E`.
- **Logo** : emblème vectoriel `wwwroot/branding/bucodi-logo.svg` (anneau + rose des vents jaune) ;
  **favicon** `bucodi-mark.svg`.
- **Page de connexion Keycloak** : realm `displayName` = « Bucodi ».

> En **thème sombre**, le socle Stratum conserve sa palette scellée (contrat BRD01) : la marque ne
> repeint que le thème clair. La démo est donc à présenter en thème clair.

## Architecture & isolation

Projet Compose dédié **`liakont-bucodi`** → conteneurs/volumes/réseau préfixés, **aucune collision**
avec l'env de dev (`liakont-keycloak` sur 8080, Postgres dev sur 5432).

| Service | Conteneur | Port hôte | Volume |
|---|---|---|---|
| Host .NET 10 (Development) | `bucodi-host` | 8090 → 8080 | `bucodi-app-data` (`/app/App_Data`) |
| PostgreSQL plateforme | `bucodi-postgres` | 5442 → 5432 | `bucodi-db-data` |
| Keycloak 26.0 | `bucodi-keycloak` | 8081 → 8080 | — (état en base) |
| PostgreSQL Keycloak | `bucodi-keycloak-db` | interne | `bucodi-keycloak-db-data` |

**Base de données** : le Host **crée et migre la base système au démarrage** (DbUp, tous environnements).
**Aucun tenant n'est auto-seedé** (RB2 : `DevTenantSeed__TenantId` vide) — la base démarre à **0 tenant** ;
seul le super-admin `sysadmin` est amorcé (section `AdminSeed`, base système). Les tenants se créent via
l'écran **Clients**.

**OIDC en localhost** (même schéma que l'appliance) : Keycloak annonce un issuer **public fixe**
(`KC_HOSTNAME=http://localhost:8081`, vu par le navigateur) tandis que le Host interroge Keycloak en
**interne** (`keycloak:8080`, `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`) pour la découverte/JWKS/jeton.

## Remise à zéro

```powershell
demo.ps1 reset      # vide base + realm (down -v) et redémarre proproprement
```

Utile si le realm a été modifié (`--import-realm` ne réécrit pas un realm déjà importé) ou pour
repartir d'une base vierge.

## Dépannage

- **« invalid redirect_uri »** au login : le realm n'autorise que `http://localhost:8090/*`. Accéder
  à la console par cette URL exacte (pas `127.0.0.1`).
- **Realm périmé** après édition de `keycloak/realm-bucodi.json` : `demo.ps1 reset` (supprime le volume).
- **Le Host redémarre en boucle** : vérifier que `bucodi-postgres` est `healthy` (`demo.ps1 status`) ;
  les logs Host via `demo.ps1 logs`.
- **Port déjà utilisé** : un autre service occupe 8090/8081/5442 → arrêter l'occupant ou ajuster les
  `ports:` du `docker-compose.yml`.

## Variante sans Docker pour le Host (repli)

Si l'on préfère lancer le Host hors conteneur (itération rapide) : démarrer seulement Keycloak + la
base via Docker, puis `dotnet run` le Host en pointant dessus. Voir
[`deploy/docker/README.md`](../../deploy/docker/README.md) (flux de dev : Keycloak Docker + PostgreSQL
local + `dotnet run`). Le branding et les comptes restent identiques.
