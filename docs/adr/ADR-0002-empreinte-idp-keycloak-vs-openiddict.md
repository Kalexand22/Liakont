# ADR-0002 — Spike d'empreinte de l'IdP : Keycloak vs OpenIddict

- **Statut** : Accepté (spike livré) — décision finale d'IdP **différée** (prise sur mesure)
- **Date** : 2026-06-03
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
   in-process envisagé pour les petites appliances self-hosted. Le **seam D10 rend ce choix
   réversible** sans toucher au code métier — il peut être finalisé en début de segment plateforme.
3. **La topologie de l'IdP retenu** (par instance vs mutualisé) est tranchée à l'**ADR OPS01**
   (appliance / provisioning), pas ici.

## Conséquences

- Aucun couplage Keycloak hors de la couche d'auth (`src/Host/Liakont.Host/Security/`) — vérifié
  par la frontière D10 (`IIdentityProviderAuthenticator`).
- Brancher OpenIddict = ajouter une implémentation de `IIdentityProviderAuthenticator` + son
  provisioning, sans modification des modules métier.
- À ré-mesurer en charge (sessions, multi-realms) avant la décision OPS01.

## Références

- Décision D10 — `tasks/decisions.md`, `blueprint.md` §5/§12, `docs/conception/F12` §7
- Seam D10 — `src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs`
- ADR socle Keycloak hérité — `docs/adr/socle/ADR-0013-keycloak-identity-provider.md`
- `docs/architecture/identity-permissions-liakont.md` (permissions/rôles)
