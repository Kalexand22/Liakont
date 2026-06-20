# ADR-0002 — Spike d'empreinte de l'IdP : Keycloak vs OpenIddict

- **Statut** : Accepté (spike livré) — décision finale d'IdP **différée** (prise sur mesure).
  **État d'implémentation acté le 2026-06-20 (RDF09)** : OpenIddict **NON IMPLÉMENTÉ** ; seule
  topologie V1 livrée = **Keycloak par instance** (voir avenant 2026-06-20 en fin d'ADR).
- **Date** : 2026-06-03 (avenant 2026-06-20 — RDF09)
- **Contexte décisionnel** : décision D10 (2026-06-03), `blueprint.md` §5/§12, `docs/conception/F12` §7 (n°1), item SOL01

## Contexte

L'authentification humaine de Liakont est OIDC + RBAC (module Identity du socle). En V1/dev,
l'implémentation est **Keycloak** (realm `liakont-dev`). Mais le produit se déploie aussi en
**self-hosted éditeur** sur de petites instances (VPS 15–40 €/mois) : l'empreinte mémoire de l'IdP
pèse directement sur le coût et la faisabilité d'une instance.

La décision D10 a tranché une **direction**, pas un produit : l'auth est consommée **derrière une
abstraction d'IdP** (`IIdentityProviderAuthenticator`, voir SOL01 / D10) — Keycloak n'est qu'UNE
implémentation, une alternative in-process (OpenIddict) doit être branchable sans rouvrir le code
métier. Le choix final est pris **sur MESURE**. Cet ADR consigne la mesure.

## Spike — mesure d'empreinte (2026-06-03)

Stack de dev `deploy/docker/docker-compose.keycloak.yml` (Keycloak 26 + PostgreSQL 16 dédié),
realm `liakont-dev` importé, mesure `docker stats --no-stream` après démarrage à froid :

| Composant | RSS mesuré | Nature |
|---|---|---|
| `liakont-keycloak` (Keycloak 26, JVM Quarkus) | **≈ 682 MiB** | Processus/conteneur séparé, JVM |
| `liakont-keycloak-db` (PostgreSQL 16 dédié à Keycloak) | ≈ 37 MiB | Base interne de Keycloak |
| **Total pile Keycloak** | **≈ 720 MiB** | + sa propre base |

Pour comparaison (référence, non mesuré ici car non encore implémenté) :

| Option | Empreinte attendue | Nature |
|---|---|---|
| **Keycloak 26** | ≈ 0,7–1 Go (JVM, croît avec sessions/realms) | Conteneur + base séparés |
| **OpenIddict** (in-process) | **incrément de quelques dizaines de Mo** dans le process `Liakont.Host` (.NET) | Aucune JVM, aucun conteneur ni base séparés |

L'écart est d'un ordre de grandeur (~700 Mo vs ~quelques dizaines de Mo). Sur une instance
mutualisée hébergée, l'empreinte Keycloak est négligeable ; sur une petite appliance self-hosted,
elle peut représenter l'essentiel de la RAM disponible.

## Arbitrage

| Critère | Keycloak | OpenIddict (in-process) |
|---|---|---|
| Empreinte mémoire | Lourde (~0,7–1 Go + base) | Légère (~dizaines de Mo) |
| Admin UI, fédération, MFA, social login | Fournis nativement | À construire/intégrer soi-même |
| Realms multi-tenant, JWKS par realm | Natif (déjà câblé : `RealmRegistry`, `MultiRealmJwksKeyResolver`) | À implémenter |
| Ops (mises à jour, CVE, sauvegarde) | Composant tiers à exploiter | Dans le process applicatif |
| Maturité OIDC | Très élevée | Bibliothèque .NET éprouvée, surface plus mince |

## Décision

1. **V1/dev = Keycloak** (realm `liakont-dev`), conformément à D10. Le Host démarre et s'authentifie
   derrière l'abstraction `IIdentityProviderAuthenticator` (implémentation
   `KeycloakIdentityProviderAuthenticator`).
2. **La décision finale d'IdP est différée et prise sur mesure**, par instance/topologie : Keycloak
   (par instance ou mutualisé) pour les déploiements où l'empreinte est acceptable ; OpenIddict
   in-process **envisagé mais NON IMPLÉMENTÉ** pour les petites appliances self-hosted. Le **seam D10
   borne le couplage Keycloak à la couche d'auth** (`IIdentityProviderAuthenticator`) — il rend
   l'alternative *branchable*, mais ne la livre pas : à ce jour il existe **0 implémentation
   OpenIddict** (le registre `SelectIdentityProvider` n'a qu'une seule entrée, Keycloak). Brancher
   OpenIddict reste un travail à faire (voir avenant 2026-06-20).
3. **La topologie de l'IdP retenu** (par instance vs mutualisé) est tranchée à l'**ADR-0020**
   (OPS01c), pas ici. → Tranché (2026-06-11) : **Keycloak par instance**, empreinte re-mesurée sur
   la stack appliance (OPS01a), conteneur à plafonner.

## Conséquences

- Aucun couplage Keycloak hors de la couche d'auth (`src/Host/Liakont.Host/Security/`) — vérifié
  par la frontière D10 (`IIdentityProviderAuthenticator`).
- Brancher OpenIddict = ajouter une implémentation de `IIdentityProviderAuthenticator` + son
  provisioning, sans modification des modules **métier**. ⚠ **À ne pas lire comme « seul le sélecteur
  change »** : le périmètre réel à réécrire dépasse l'authentificateur (provisioning realm/utilisateur,
  2FA, résolution par issuer/JWKS) — voir avenant 2026-06-20.
- À ré-mesurer en charge (sessions, multi-realms) avant la décision OPS01 — décision prise : ADR-0020.

## Références

- Décision D10 — `tasks/decisions.md`, `blueprint.md` §5/§12, `docs/conception/F12` §7
- Seam D10 — `src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs`
- ADR socle Keycloak hérité — `docs/adr/socle/ADR-0013-keycloak-identity-provider.md`
- `docs/architecture/identity-permissions-liakont.md` (permissions/rôles)

## Avenant 2026-06-20 — État réel d'implémentation du seam IdP (RDF09)

- **Statut** : Accepté — **Date** : 2026-06-20 — **Item** : RDF09 (redline ADR fondateurs, finding RL-IDP-1)

### Constat

Le seam D10 (`IIdentityProviderAuthenticator`) n'a qu'**UNE** implémentation : `KeycloakIdentityProviderAuthenticator`
(`src/Host/Liakont.Host/Security/Keycloak/`). Il y a **0 implémentation OpenIddict** (ni prod, ni test).
Le registre de fabriques `SelectIdentityProvider` (`src/Host/Liakont.Host/Startup/AppBootstrap.cs`) ne
contient qu'une seule entrée (`"Keycloak"`). Par conséquent **le cas qui motivait le spike — la petite
appliance VPS 15–40 €/mois où ≈ 0,5–1 GiB de Keycloak pèse — n'a PAS de solution livrée** : la seule
topologie V1 réellement déployable est **Keycloak par instance** (ADR-0020), qui doit provisionner
**≈ 1 GiB** pour l'IdP (cohérent avec **RDF04**, qui acte le plancher d'empreinte de l'appliance).

### Pourquoi « brancher OpenIddict = seul le sélecteur change » est trompeur

L'abstraction D10 borne bien le couplage Keycloak à la couche d'auth, mais **brancher OpenIddict
n'est pas un simple ajout d'entrée au sélecteur** : un fournisseur in-process devrait aussi ré-livrer,
hors de l'authentificateur lui-même, tout le câblage Keycloak-spécifique aujourd'hui présent :

- **Provisioning realm/utilisateur** câblé dans la couche multi-tenant indépendamment du sélecteur :
  `IKeycloakRealmProvisioner` / `IKeycloakUserProvisioner` (`src/Common/Abstractions/MultiTenancy/`),
  et côté Host `ITenantUserProvisioningService` → `KeycloakTenantUserProvisioner`,
  `ITenantUserManagementService` → `KeycloakTenantUserManagementService` (`AppBootstrap`).
- **2FA** au niveau realm Keycloak (`CONFIGURE_TOTP` + `otpPolicy`, durci par **RDF02**) — propre à Keycloak.
- **Résolution par issuer / JWKS multi-realms** : `RealmRegistry`, `MultiRealmJwksKeyResolver`,
  `SeedRealmRegistryFromDatabaseAsync` (`src/Host/Liakont.Host/Security/`) — bâtis sur la mécanique de realms Keycloak.

L'affirmation « il suffit d'ajouter une implémentation, seul le sélecteur change » est donc **retirée**
(ici et qualifiée dans ADR-0020 §3) : elle sous-estime le travail réel de réversibilité.

### Décision (acter, sans rouvrir la direction)

1. **OpenIddict est NON IMPLÉMENTÉ.** La direction « réversible derrière `IIdentityProviderAuthenticator` »
   (D10) reste valide ; **la livraison ne l'est pas**. V1 = **Keycloak par instance** uniquement.
2. Le **MVP OpenIddict de preuve de réversibilité** (qui obligerait la plus petite appliance à
   provisionner ~1 GiB de moins) est un **go/no-go opérateur (DEC-1)** — hors de cet item.
3. Aucune décision commerciale (pricing, topologie d'offre) n'est tranchée ici : cet avenant ne fait
   qu'**acter l'état d'implémentation** et corriger la phrase trompeuse.

### Références (avenant)

- Seam + unique implémentation — `src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs`,
  `src/Host/Liakont.Host/Security/Keycloak/KeycloakIdentityProviderAuthenticator.cs`
- Sélecteur (registre à une entrée) — `src/Host/Liakont.Host/Startup/AppBootstrap.cs` (`SelectIdentityProvider`)
- Câblage Keycloak hors sélecteur — `src/Common/Abstractions/MultiTenancy/IKeycloakRealmProvisioner.cs`,
  `IKeycloakUserProvisioner.cs`, `src/Host/Liakont.Host/Security/RealmRegistry.cs`, `MultiRealmJwksKeyResolver.cs`
- ADR-0020 §3 (échappatoire OpenIddict, direction non rouverte), RDF02 (2FA realm), RDF04 (plancher ~1 GiB appliance), DEC-1 (go/no-go MVP)
