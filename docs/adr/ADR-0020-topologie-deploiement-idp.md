# ADR-0020 — Topologie de déploiement de l'IdP (Keycloak par instance, empreinte mesurée)

- **Statut** : Accepté
- **Date** : 2026-06-11
- **Items** : OPS01c (ADR topologie IdP), s'appuie sur OPS01a (appliance Docker)
- **Contexte décisionnel** : décision D10 (2026-06-03), ADR-0002 (spike d'empreinte Keycloak vs
  OpenIddict), `docs/conception/F12-Architecture-Plateforme-Agent.md` §6.2 / §7 (n°1),
  `blueprint.md` §3.3 (les trois topologies de déploiement)

## Contexte

La **direction** de l'authentification est tranchée depuis SOL01/D10 : l'auth OIDC + RBAC est
consommée **derrière une abstraction d'IdP** (`IIdentityProviderAuthenticator`,
`src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs`). Keycloak n'est
qu'**une** implémentation (`KeycloakIdentityProviderAuthenticator`) ; une alternative in-process
(OpenIddict) reste branchable sans toucher au métier. ADR-0002 a livré le spike comparatif
d'empreinte et a **explicitement différé** deux choses : (a) le choix final Keycloak/OpenIddict,
pris sur mesure par instance, et (b) **la topologie de déploiement du fournisseur retenu**,
renvoyée « à l'ADR OPS01 ». **Le présent ADR tranche (b)** — il ne rouvre PAS (a).

La question de topologie : maintenant que l'appliance OPS01a existe (`deploy/docker/appliance/`,
Keycloak + base dédiée + Host + Caddy), faut-il **un Keycloak par instance** (co-localisé dans
chaque appliance) ou **un Keycloak mutualisé** servant plusieurs instances/éditeurs ? L'empreinte
mémoire de l'IdP conditionne la faisabilité d'une petite appliance self-hosted et le pricing des
instances hébergées — d'où l'exigence d'une **mesure**, pas d'une estimation.

## Spike — empreinte mesurée sur l'appliance (2026-06-11)

Mesure sur la **stack appliance OPS01a** (`deploy/docker/appliance/docker-compose.yml`), services
`keycloak` + `keycloak-db` uniquement (Host et Caddy non requis pour mesurer l'IdP), image
`quay.io/keycloak/keycloak:26.0` (→ **26.0.8**, JVM Quarkus 3.15.1) en **mode PRODUCTION**
(`start --import-realm`, l'image exécute `kc.sh start --optimized`), realm `liakont` importé.
Relevé `docker stats --no-stream` après démarrage à froid, **au repos** (aucune session pilotée).

**Hôte de mesure** : Docker Engine 28.4.0 (Docker Desktop), VM Docker voyant **31,02 GiB** de RAM /
24 vCPU. Point méthodologique central ci-dessous.

| Configuration du conteneur Keycloak | RSS Keycloak | RSS `keycloak-db` | Démarrage |
|---|---|---|---|
| **Sans plafond mémoire** (compose appliance tel quel) | **≈ 1,83 GiB** | ≈ 38 MiB | 9,1 s |
| **Plafonné `mem_limit: 1g`** | **≈ 456 MiB** (44,5 % du plafond) | ≈ 36 MiB | 6,5 s, **pas d'OOM** |

Référence comparative, **même hôte, même instant** (mode dev `start-dev`, base ADR-0002) :

| Référence | RSS Keycloak |
|---|---|
| Compose dev `docker-compose.keycloak.yml` (`start-dev`, sans plafond, ~5 h d'uptime) | ≈ 888 MiB |
| ADR-0002 (dev, démarrage à froid, 2026-06-03) | ≈ 682 MiB |

### Lecture de la mesure (méthodologie — à ne pas confondre avec un chiffre « absolu »)

L'empreinte RSS de Keycloak est **pilotée par le plafond mémoire du conteneur**, pas par une
constante : la JVM dimensionne son tas via `MaxRAMPercentage` du **cgroup**. Sans plafond, sur un
hôte à 31 GiB, la JVM se sert largement → ≈ 1,83 GiB. Plafonnée à 1 GiB, elle se cale → **≈ 456 MiB
au repos** et démarre sans OOM. Le ≈ 1,83 GiB « sans plafond » n'est donc **pas** l'empreinte d'une
appliance correctement configurée : c'est l'empreinte d'une JVM laissée libre sur un gros hôte.

**Budget mémoire IdP retenu pour une instance** : **≈ 0,5 GiB au repos** (Keycloak conteneur
plafonné) + ≈ 40 MiB pour `keycloak-db`, à **provisionner ≈ 1 GiB** pour absorber les sessions et
la croissance multi-realms (voir caveat). L'écart d'**un ordre de grandeur** avec OpenIddict
in-process (incrément de quelques dizaines de Mo, ADR-0002) est confirmé.

**Caveats** (mesure au repos, single realm) : sous charge (sessions concurrentes) et en
**multi-realms** (un realm par tenant dans une instance multi-tenant), le tas croît — le caveat
« à ré-mesurer en charge » d'ADR-0002 reste valable avant tout dimensionnement ferme d'une offre
hébergée à forte densité de tenants. Un plafond trop bas expose à l'OOM / au thrash GC : le plafond
est un garde-fou d'hôte, pas une cure d'amaigrissement gratuite.

## Décision

### 1. Topologie retenue : **un Keycloak PAR INSTANCE** (co-localisé dans l'appliance)

Chaque instance (self-hosted éditeur, dédiée hébergée, mutualisée) embarque **son propre** conteneur
Keycloak et **sa propre** base `keycloak-db`, conformément à OPS01a. La multi-location **à
l'intérieur** d'une instance se fait par **un realm par tenant** (mécanique du socle déjà câblée :
`RealmRegistry`, `MultiRealmJwksKeyResolver`). On **n'adopte PAS** un Keycloak mutualisé entre
plusieurs instances/éditeurs. Motifs :

- **Isolation par éditeur** : `blueprint.md` §3.3 — « la marque grise = une instance de plateforme
  PAR éditeur ». Un IdP mutualisé entre éditeurs serait un point de défaillance et un périmètre de
  compromission partagés, et couplerait leurs cadences de montée de version / patch CVE.
- **Réversibilité de V1 (non négociable)** : OPS06b migre une instance dédiée hébergée → self-hosted
  par dump/restore + bascule DNS. Un Keycloak par instance fait **voyager l'IdP avec l'instance**
  (dump de sa propre `keycloak-db`, déjà couvert par la granularité par base d'OPS01b). Extraire le
  realm d'un éditeur d'un Keycloak mutualisé serait un **bloqueur de réversibilité**.
- **Cohérence self-hosted** : en self-hosted il n'y a de toute façon qu'un seul éditeur — la
  mutualisation de l'IdP n'a pas d'objet.
- **Coût acceptable** : sur instance hébergée (mutualisée *au niveau machine*, mais IdP par
  conteneur) et dédiée, ≈ 0,5–1 GiB pour l'IdP est négligeable devant le bénéfice d'isolation.

### 2. Plafonner la mémoire du conteneur Keycloak de l'appliance (suivi OPS01a)

La mesure montre qu'**il faut un plafond** : sans `mem_limit`, la JVM happe ≈ 1,83 GiB sur un gros
hôte, ce qui rend une petite VPS self-hosted faussement non viable. Recommandation **actée** :
ajouter un plafond mémoire (`mem_limit` / `deploy.resources.limits.memory`, ≈ 1 GiB par défaut) au
service `keycloak` de `deploy/docker/appliance/docker-compose.yml`, afin que la JVM se cale et
qu'une petite appliance reste viable. C'est une **petite reprise d'OPS01a** (hors périmètre du
présent ADR, qui mesure et décide la topologie) — voir « Conséquences ».

### 3. Échappatoire petite appliance : le seam D10 (OpenIddict), **direction non rouverte ici**

Pour les plus petites instances self-hosted où même ≈ 0,5–1 GiB pèse, l'alternative **OpenIddict
in-process** (quelques dizaines de Mo, ADR-0002) reste **branchable derrière
`IIdentityProviderAuthenticator`** sans toucher au métier (seul le sélecteur d'`AppBootstrap`
change). Le choix Keycloak/OpenIddict **n'est pas rouvert** par cet ADR : il est tranché en amont
(D10 + spike ADR-0002), pris sur mesure par instance. Le présent ADR ne décide que la **topologie**
du fournisseur Keycloak (par instance), pas l'identité du fournisseur.

## Conséquences

- **OPS01a confirmé** dans sa topologie : Keycloak + `keycloak-db` **par instance**, realm `liakont`
  importé au démarrage, multi-realms intra-instance pour les tenants.
- **Suivi (petite reprise d'OPS01a)** : plafonner la mémoire du conteneur `keycloak` dans
  `deploy/docker/appliance/docker-compose.yml` (≈ 1 GiB). Le présent ADR ne modifie pas le compose
  (changement chirurgical : OPS01c = mesure + décision) ; la reprise est tracée comme suivi.
- **Pricing des instances hébergées** : budget IdP ≈ **1 GiB RAM/instance** (Keycloak plafonné) +
  ≈ 40 MiB `keycloak-db`, à additionner au Host (.NET 10) + PostgreSQL applicatif pour dimensionner
  une instance.
- **Avant une offre hébergée à forte densité de tenants** : ré-mesurer en charge (sessions
  concurrentes, N realms) — le chiffre au repos single-realm est un plancher, pas un plafond d'usage.
- **Aucun couplage Keycloak hors de la couche d'auth** (`src/Host/Liakont.Host/Security/`) reste
  garanti par la frontière D10 — inchangé.

## Références

- Décision D10 — abstraction d'IdP dès SOL01 ; `blueprint.md` §5/§12, `docs/conception/F12` §7 n°1
- Seam D10 — `src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs`,
  impl. `src/Host/Liakont.Host/Security/Keycloak/KeycloakIdentityProviderAuthenticator.cs`
- ADR-0002 — spike d'empreinte Keycloak vs OpenIddict (différait la topologie « à l'ADR OPS01 »)
- `docs/conception/F12-Architecture-Plateforme-Agent.md` §6.2 (appliance), §7 n°1 (auth des instances)
- `blueprint.md` §3.3 (les trois topologies de déploiement), §6 (déploiement)
- Appliance OPS01a — `deploy/docker/appliance/docker-compose.yml`, base de la mesure
- Réversibilité OPS06b (dédiée hébergée → self-hosted), granularité par base OPS01b
