# Appliance Docker Liakont — installation (éditeur self-hosted)

Déploiement d'une **instance complète** Liakont sur une machine, avec son fournisseur d'identité
et son reverse proxy TLS. Source : F12 §6.2 (« l'appliance Docker »), item OPS01a.

La stack lancée par `docker compose` :

| Service | Rôle | Volume persistant |
|---|---|---|
| `liakont` | Host applicatif (.NET 10) — la console + l'API agent | `liakont-app-data` (`/app/App_Data`) |
| `postgres` | Base **système** + bases **tenant** (database-per-tenant) | `liakont-db-data` |
| `keycloak` | Fournisseur d'identité (realm `liakont` importé) | — (état en base) |
| `keycloak-db` | Base PostgreSQL dédiée à Keycloak | `liakont-keycloak-db-data` |
| `caddy` | Reverse proxy + **TLS automatique** (Let's Encrypt) | `caddy-data`, `caddy-config` |

> **Seul Caddy publie des ports** (80/443) sur la machine. Le Host, Keycloak et PostgreSQL restent
> internes au réseau de l'appliance — aucune surface d'attaque directe.

---

## 1. Prérequis

- **Docker Engine** ≥ 24 avec le plugin **Docker Compose v2** (`docker compose version`).
- **RAM** : prévoir **~4 Go** disponibles (Keycloak/JVM ~1–1,5 Go, Host ~0,5 Go, PostgreSQL ×2,
  marge système). CPU 2 vCPU minimum.
- **Disque** : ≥ 20 Go (les volumes grandissent avec les archives fiscales — conservation 10 ans).
- **DNS public** : deux enregistrements A/AAAA pointant vers la machine — un pour la console
  (`PUBLIC_HOSTNAME`), un pour Keycloak (`KEYCLOAK_HOSTNAME`).
- **Ports ouverts** depuis Internet : **80** et **443** (TCP, et 443/UDP pour HTTP/3). Indispensables
  à l'obtention du certificat Let's Encrypt par Caddy.

---

## 2. Installation pas à pas

```bash
# 1. Se placer dans le dossier de l'appliance
cd deploy/docker/appliance

# 2. Créer la configuration d'instance à partir du modèle
cp .env.example .env

# 3. Renseigner .env : hôtes publics + secrets générés (AUCUN secret par défaut)
#    Générer chaque secret, par ex. :  openssl rand -base64 32
#    À remplir au minimum :
#      PUBLIC_HOSTNAME, KEYCLOAK_HOSTNAME, PUBLIC_BASE_URL, KEYCLOAK_PUBLIC_URL, ACME_EMAIL,
#      POSTGRES_PASSWORD, KC_DB_PASSWORD, KC_BOOTSTRAP_ADMIN_USERNAME, KC_BOOTSTRAP_ADMIN_PASSWORD,
#      KEYCLOAK_LIAKONT_CLIENT_SECRET
nano .env

# 4. Construire l'image applicative et démarrer la stack
#    --pull : repart d'images de base à jour (le SDK doit satisfaire global.json — cf. Dockerfile).
docker compose build --pull
docker compose up -d

# 5. Suivre l'amorçage (migrations de base, import du realm, obtention du certificat)
docker compose logs -f liakont caddy keycloak
```

Au premier démarrage :

- `postgres` s'initialise, puis le **Host applique ses migrations** (base système ; les bases tenant
  sont migrées à leur création) ;
- `keycloak` importe le **realm `liakont`** (rôles, client confidentiel, mappeurs de claims) ;
- `caddy` obtient les **certificats TLS** pour les deux hôtes publics (peut prendre 1–2 minutes).

Vérifications :

- Console : `https://<PUBLIC_HOSTNAME>` répond et redirige vers la page de connexion.
- Keycloak : `https://<KEYCLOAK_HOSTNAME>` affiche la page d'accueil ; la console d'admin
  (`/admin`) accepte le compte `KC_BOOTSTRAP_ADMIN_*`.

---

## 3. Première connexion

Le realm est importé **sans aucun compte utilisateur** (aucun secret/compte par défaut). Deux options
pour créer le premier accès :

1. **Via la console d'admin Keycloak** (`https://<KEYCLOAK_HOSTNAME>/admin`, compte bootstrap) : créer
   un utilisateur dans le realm `liakont`, lui poser un mot de passe et le(s) rôle(s) voulu(s)
   (`lecture`, `operateur`, `parametrage`, `superviseur`, `stratum-admin`).
2. **Via le provisioning de tenant** (écran « Clients ») une fois l'item OPS03 livré : l'assistant crée
   le tenant, son premier utilisateur et le mapping utilisateur→tenant.

Les **rôles** du realm sont projetés en permissions Liakont (matrice §3 d'`identity-permissions-liakont.md`,
pont OIDC ADR-0017). Un super-admin d'instance porte le rôle `stratum-admin`.

---

## 4. Ce qui survit au redéploiement (volumes)

Un `docker compose pull && docker compose up -d` (mise à jour) **préserve** :

- les **bases** (système, tenants, Keycloak) — `liakont-db-data`, `liakont-keycloak-db-data` ;
- le **trousseau Data Protection** (`/app/App_Data/dataprotection-keys`) — **indispensable** : sans lui,
  les secrets tenant chiffrés (clés API des PA) deviendraient illisibles après redéploiement ;
- le **coffre d'archive WORM** (`/app/App_Data/archive-store`) — données fiscales conservées 10 ans ;
- le **staging** du pivot et les **PDF reçus** (`/app/App_Data/staging-store`, `/app/App_Data/ingestion-pdf`) ;
- les **certificats** TLS (`caddy-data`).

> **⚠️ Le volume `liakont-app-data` est PORTEUR DE SECRET.** Le trousseau Data Protection y est
> persisté **en clair** (pas de DPAPI hors Windows) : c'est la clé maîtresse qui déchiffre les secrets
> tenant (clés API des PA) stockés en base. Un accès au volume suffit donc à déchiffrer ces secrets —
> protéger le volume (permissions disque, accès Docker restreint) et chiffrer ses sauvegardes. Le
> **chiffrement du trousseau au repos** (certificat / KMS) et la politique de sauvegarde formelle sont
> tranchés par **OPS01b** (sauvegarde/PRA) / **OPS01c** (ADR topologie). En V1, la promesse « secrets
> chiffrés » (CLAUDE.md n°10) est donc conditionnée à la protection de ce volume.

> **Sauvegarde** : la procédure `pg_dump` par base + copie des volumes d'archive est outillée par
> l'item **OPS01b** (sauvegarde/restauration/PRA). Tant qu'OPS01b n'est pas livré, sauvegarder
> manuellement les volumes ci-dessus avant toute opération sensible.

---

## 5. Mise à jour de l'instance

```bash
cd deploy/docker/appliance
git pull                          # nouvelle version du code/compose
docker compose build --pull       # rebuild image Host (base SDK/runtime à jour, cf. global.json)
docker compose up -d              # redéploiement
```

Le Host **applique automatiquement les migrations** au démarrage. Les volumes sont conservés
(cf. §4). L'outillage de montée de version multi-bases (échec partiel, rollback) est porté par
**OPS02** (`update-instance.ps1`).

---

## 6. Architecture réseau (pourquoi ça marche derrière le proxy)

- Caddy **termine le TLS** et relaie en HTTP interne vers `liakont:8080` et `keycloak:8080`. Il pose
  `X-Forwarded-Proto/-Host/-For`.
- Le Host **honore ces en-têtes** (`ForwardedHeaders__Enabled=true`, confiance **bornée** au sous-réseau
  interne `172.28.0.0/16`) : les `redirect_uri` OIDC et les cookies `Secure` reflètent l'URL **publique**.
- OIDC : `Keycloak__Authority` est l'**issuer public** (redirection navigateur + validation), tandis que
  `Keycloak__MetadataAddress` pointe sur Keycloak en **réseau interne** (`http://keycloak:8080/...`). Avec
  `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`, le **back-channel** (découverte, jeton, JWKS) reste interne —
  aucun aller-retour par Caddy — pendant que les endpoints **frontaux** restent publics.

---

## 7. Dépannage

- **Le certificat TLS n'est pas obtenu** : vérifier que le DNS des deux hôtes pointe sur la machine et
  que les ports 80/443 sont joignables depuis Internet. Logs : `docker compose logs caddy`.
- **« invalid client credentials » à la connexion** : le secret du client n'a pas été substitué dans
  Keycloak. Vérifier que `KEYCLOAK_LIAKONT_CLIENT_SECRET` est renseigné dans `.env` (il est injecté à
  la fois dans Keycloak et dans le Host). Le flag `-Dkeycloak.migration.replace-placeholders=true` est
  posé sur le service `keycloak` (substitution des `${...}` du realm). **Repli** si la substitution
  n'est pas honorée (le champ `secret` resterait littéral `${KEYCLOAK_LIAKONT_CLIENT_SECRET}`) : poser
  le secret manuellement dans la console d'admin Keycloak (realm `liakont` → Clients → `liakont` →
  Credentials → coller la valeur de `KEYCLOAK_LIAKONT_CLIENT_SECRET`), ou via l'admin API post-import.
  Cette substitution est revalidée contre un Keycloak 26 réel à la recette (gate `GATE_TOOLKIT`).
- **Realm périmé après modification de `realm-liakont.json`** : l'import (`--import-realm`) ne réécrit
  PAS un realm déjà présent. Pour réappliquer la définition : `docker compose down` puis supprimer le
  volume `liakont-keycloak-db-data` (⚠️ efface les utilisateurs Keycloak créés) avant `up`.
- **Essai LOCAL / hors-ligne** (sans DNS public, certificat Let's Encrypt impossible) : utiliser un
  certificat interne Caddy en remplaçant les blocs d'hôte du `Caddyfile` par `tls internal`, et pointer
  les hôtes vers `127.0.0.1` dans le fichier `hosts` de la machine. La connexion OIDC back-channel reste
  fonctionnelle (interne, en http). Cet usage est un essai, pas une production.

---

## 8. Périmètre de cet item (OPS01a) et suites

Cet item livre **build + run** de l'appliance. Sont portés par les items suivants du lot OPS :

- **OPS01b** — sauvegarde / restauration **testée** de bout en bout + PRA (RTO/RPO).
- **OPS01c** — ADR de topologie de l'IdP (empreinte mémoire **mesurée**).
- **OPS02** — provisioning/mise à jour d'instances (`new-instance.ps1`, migration multi-bases).
- **OPS03** — provisioning de tenants depuis la console (assistant « Clients »).

> Le démarrage de bout en bout sur machine propre (DNS réel + login) est **revalidé à la recette
> humaine** (gate `GATE_TOOLKIT`). La vérification automatisée de cet item couvre le build du Host,
> la validation du `compose`, et la cohérence des artefacts.
