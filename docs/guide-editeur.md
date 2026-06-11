# Guide de l'éditeur / opérateur d'instance — Liakont

**Public :** l'**opérateur d'instance** — l'éditeur qui héberge Liakont pour ses clients, ou
IT Innovations (exploitant). C'est la personne qui déploie l'instance, crée les clients
(tenants), enregistre et installe les agents, surveille la flotte, met à jour, sauvegarde et
assure la réversibilité.
**Objet :** le cycle de vie complet d'une instance et de ses clients.

> Pour l'usage quotidien de la console comptable (documents, table TVA, blocages), voir le
> **[guide de l'opérateur](guide-operateur.md)**. Pour installer l'agent chez le client final,
> voir **[docs/installation-agent.md](installation-agent.md)**.

---

## 1. Architecture en bref

Liakont se compose de **deux** éléments :

- **La plateforme** (.NET 10, ASP.NET Core + Blazor, PostgreSQL) : tout le métier — ingestion,
  mapping TVA, validation, états des documents, envoi à la Plateforme Agréée (PA), archivage
  WORM, console web, supervision. **Multi-tenant** : un tenant = un client.
- **L'agent d'extraction** (.NET Framework 4.8, service Windows) : installé **chez le client
  final**, près de sa base métier. Il **extrait en lecture seule** (ODBC), met en file locale,
  puis **pousse en HTTPS** vers la plateforme et envoie un **heartbeat**. Un agent = un tenant
  (clé API scopée). **L'agent n'a aucune logique métier.**

```
[Base métier client] ──ODBC lecture seule──▶ AGENT ──HTTPS push + heartbeat──▶ PLATEFORME
                                                                               ├─ Ingestion
                                                                               ├─ Mapping TVA
                                                                               ├─ Validation
                                                                               ├─ Envoi PA
                                                                               ├─ Archive (WORM)
                                                                               └─ Console + Supervision
```

> Référence : `blueprint.md` (doctrine) et
> `docs/conception/F12-Architecture-Plateforme-Agent.md` (contrat d'ingestion, supervision,
> déploiement, paramétrage).

---

## 2. Périmètre V1 (à rappeler à vos clients)

Liakont couvre l'**émission** (factures clients + avoirs) et l'**e-reporting**. **La réception
des factures fournisseurs n'est PAS couverte en V1** : elle reste assurée par le **portail de
la Plateforme Agréée** du client, en attendant une phase ultérieure (décision 2026-06-02,
Offre Éditeur réalignée). Énoncez-le clairement à vos clients pour éviter toute attente erronée.

---

## 3. Déployer une instance (appliance Docker)

L'instance se déploie comme une **appliance Docker Compose** auto-portée.

- Fichier : `deploy/docker/appliance/docker-compose.yml`
- Mode d'emploi détaillé : **[deploy/docker/appliance/README.md](../deploy/docker/appliance/README.md)**

Services de l'appliance :

| Service | Rôle | Volume persistant |
|---|---|---|
| `postgres` | Base **système** + bases **tenant** (database-per-tenant) | `liakont-db-data` |
| `keycloak-db` | Base Keycloak (utilisateurs, sessions, realms) | `liakont-keycloak-db-data` |
| `keycloak` | Fournisseur d'identité (OIDC) | — |
| `liakont` | L'Hôte (plateforme + console) | `liakont-app-data` (clés DPAPI, staging, PDF, **archive**) |
| `caddy` | Reverse-proxy + TLS Let's Encrypt (seul exposé, ports 80/443) | `caddy-data`, `caddy-config` |

Mise en service (résumé — voir le README appliance pour les détails) :

```bash
cd deploy/docker/appliance
cp .env.example .env          # renseigner PUBLIC_HOSTNAME, KEYCLOAK_HOSTNAME, secrets DB/Keycloak…
docker compose build --pull
docker compose up -d
docker compose logs -f liakont caddy keycloak
```

Au démarrage, `postgres` s'initialise puis l'Hôte applique ses migrations (base système) ; le
realm Keycloak est importé **sans aucun compte utilisateur** (aucun secret par défaut).

> **Authentification derrière une abstraction.** Keycloak est **une** implémentation du
> fournisseur d'identité ; le produit ne s'y couple pas en dur. La projection rôle→permission
> est documentée dans `docs/architecture/identity-permissions-liakont.md` (pont OIDC ADR-0017).
> Un super-admin d'instance porte le rôle `stratum-admin`.

---

## 4. Créer un client (tenant)

> **État actuel :** l'**assistant « Clients »** dans la console (item OPS03) **n'est pas encore
> livré**. Le provisioning d'un tenant se fait aujourd'hui par l'**API d'administration** et la
> **console d'admin Keycloak**. L'assistant graphique rejoindra la console ultérieurement.

Aujourd'hui, créer un client se fait en deux temps :

1. **Provisionner le tenant** (base dédiée + realm) via l'endpoint d'administration, réservé au
   super-admin d'instance :
   - `POST /api/v1/admin/tenants` — crée la base et le realm du tenant (renvoie le nom de base,
     le realm et l'autorité OIDC).
   - `POST /api/v1/admin/tenants/{tenantId}/seed` — importe (de façon **idempotente**) le
     paramétrage du tenant (profil légal + fiscal + planification + seuils + comptes PA **sans
     secret**) depuis un dossier serveur au format `config/exemples/tenant-seed/`.
   - `POST /api/v1/admin/tenants/{tenantId}/reprovision` et l'endpoint de **désactivation** sont
     également disponibles pour la maintenance.

2. **Créer le premier utilisateur** du client via la **console d'admin Keycloak**
   (`https://<KEYCLOAK_HOSTNAME>/admin`, compte bootstrap) : créer l'utilisateur dans le realm,
   lui attribuer ses rôles (`lecture`, `operateur`, `parametrage`, `superviseur`,
   `stratum-admin`) et le mapping utilisateur → tenant.

> **Le paramétrage client est du DÉPLOIEMENT, pas du code.** Le profil légal, la table TVA
> réelle, la chaîne ODBC et les comptes PA d'un client vivent dans `deployments/<client>/`
> (versionné), jamais dans le code. Le dépôt n'embarque que des **exemples fictifs** dans
> `config/exemples/`. Référence : `docs/conception/F12-A-Parametrage-Tenant.md`.

Une fois le tenant créé et son profil importé, l'opérateur comptable du client peut compléter
sa **table TVA** et la faire valider (voir le [guide opérateur § 9](guide-operateur.md)).

---

## 5. Enregistrer un agent (`/agents`)

Page « Gestion des agents » (autorisation **paramétrage**). Un agent = un poste d'extraction
chez le client.

- **« Enregistrer un nouvel agent… »** : saisissez un nom. Liakont émet une **clé API unique**,
  **affichée une seule fois** — copiez-la immédiatement (bouton « Copier ») : elle n'est jamais
  ré-affichée ni stockée en clair.
- **« Renouveler la clé… »** : invalide l'ancienne clé et en émet une nouvelle (rotation).
- **« Révoquer… »** : désactive définitivement l'agent (confirmation par le nom). Message :
  « L'agent « {nom} » a été révoqué. »

États affichés : **Actif** / **Muet** (heartbeat trop ancien) / **Révoqué**, avec dernière
remontée et version.

> **Sécurité (CLAUDE.md n°10).** La clé d'agent n'est jamais journalisée ni pré-remplie dans un
> formulaire. Côté poste client, elle est chiffrée par **DPAPI**. La clé est **scopée au tenant** :
> un agent n'écrit que dans son propre tenant.

---

## 6. Installer l'agent chez le client final

L'installation de l'agent sur la machine du client est décrite **en détail** dans
**[docs/installation-agent.md](installation-agent.md)**. Points clés :

- **Prérequis poste** : Windows 7 SP1+ / Server 2012+, **.NET Framework 4.8**, le pilote ODBC
  de la source (ex. Pervasive 32 bits pour EncheresV6) + DSN/chaîne valide, droits admin
  (installation seulement), HTTPS sortant vers la plateforme.
- **Bitness** : **x86 par défaut** si un adaptateur utilise un ODBC 32 bits ; x64 seulement si
  **tous** les adaptateurs sont 64 bits (service et adaptateurs partagent la même bitness).
- **Fabriquer le paquet** (côté éditeur) :
  `powershell -ExecutionPolicy Bypass -File tools/package-agent.ps1 -Platform x86`
  → `artifacts/agent-packages/Liakont.Agent-<version>-x86.zip`. Le paquet peut être
  **pré-configuré** (URL plateforme, adaptateur, clé, planification).
- **Installer** (sur le poste) : `install-agent.ps1` (instances nommées possibles pour un
  serveur mutualisé).
- **Diagnostic** : `Liakont.Agent.Cli.exe check-config | test-odbc | test-api`.

> Voir aussi `docs/conception/F13-Installateur-Agent-Profils-Integrateur.md` (installateur GUI
> et profils intégrateur).

---

## 7. Configurer le compte Plateforme Agréée (`/parametrage/comptes-pa`)

Page « Comptes plateforme agréée » (autorisation **paramétrage**).

- **« Créer un compte »** : choisir le plug-in PA (ex. B2Brouter), l'environnement
  (staging / production), les identifiants de compte et la **clé API** (champ masqué).
- **« Éditer »** : le champ clé API est **toujours vide** à l'édition — laissez-le vide pour
  conserver la clé, renseignez-le pour la remplacer.
- **« Désactiver »** : le compte reste en base mais n'est plus utilisé pour la transmission.
- **« Publier »** : publie le SIREN du tenant auprès de la PA (date de début par défaut =
  aujourd'hui).

Un tenant peut avoir **plusieurs** comptes PA (par exemple B2Brouter staging **et** production).

> **Secrets chiffrés, par tenant (CLAUDE.md n°10).** Les clés API des PA sont **chiffrées au
> repos** (ASP.NET Core Data Protection, clés dans le volume `liakont-app-data`) et **jamais
> exposées** en lecture ni journalisées. Conflit si un compte existe déjà pour ce plug-in et cet
> environnement : « Un compte plateforme agréée existe déjà pour ce plug-in et cet environnement.
> Modifiez le compte existant ou choisissez un autre environnement. »

> **Le comportement produit suit les CAPACITÉS du plug-in PA, jamais son nom.** Si une PA ne
> supporte pas encore une fonction (ex. transmission des paiements), la console l'indique et les
> données partiront automatiquement dès activation — il n'y a aucun `if (pa == …)` ni flag produit.

---

## 8. Superviser la flotte (`/supervision`)

La **Supervision** est la **seule** vue **cross-tenant**, en **lecture seule**. Elle donne, pour
**tous les tenants** de l'instance, les alertes actives, l'état des agents et les files de
documents. Les tenants à traiter (alertes critiques) apparaissent en tête.

- Page d'ensemble `/supervision` (autorisation **supervision**) : par tenant — nombre d'alertes
  actives (et pire gravité), nombre d'agents, documents bloqués / rejetés PA / en attente,
  dernière remontée d'agent. Action « Détails » → `/supervision/{tenantId}`.
- Détail `/supervision/{tenantId}` : compteurs (Bloqués / Rejetés PA / En attente), liste des
  agents (version, dernier heartbeat) et liste des alertes avec action **« Acquitter »**.

### Le témoin de vie (dead-man's switch)

Un **bandeau de vivacité** indique si le dispositif de supervision **tourne**. C'est essentiel :
**une absence d'alerte n'est rassurante que si le dispositif fonctionne**. Si le témoin signale
un arrêt, traitez-le en priorité — sinon un silence pourrait masquer une panne.

### Les alertes — gravités et règles

Deux gravités : **🔴 Critique** (risque de conformité : action requise) et **🟠 Avertissement**
(anomalie à traiter, sans urgence immédiate). Une alerte reste active tant que sa condition n'a
pas disparu ; **au plus une alerte active par (tenant, règle)**. **Acquitter** ≠ résoudre :
l'acquittement note la prise en charge, l'alerte ne se referme qu'une fois la cause disparue.

Règles **actuellement implémentées** :

| Règle | Gravité | Déclenchement | Que faire |
|---|---|---|---|
| **Agent muet** (`agent.mute`) | 🔴 Critique | Un agent (non révoqué) n'a plus émis de heartbeat depuis plus de N heures (défaut **24 h**). | Vérifier que le poste est allumé et que le service Liakont Agent y est démarré. |
| **Documents bloqués** (`documents.blocked`) | 🟠 Avertissement | Le plus ancien document **Bloqué** stagne depuis plus de N jours (défaut **5 j**). | Corriger les données/paramétrage (ex. table TVA) pour que les documents repartent. |
| **Rejets PA** (`documents.pa_rejected`) | 🔴 Critique | Le plus ancien document **Rejeté par la PA** non retraité depuis plus de N jours (défaut **2 j**). | Corriger et renvoyer, ou traiter manuellement. |

> Les messages d'alerte sont **actionnables** (en français, avec le poste/le document le plus
> ancien concerné et l'action corrective).

D'autres règles sont **prévues mais non encore implémentées** (déclarées dans
`docs/conception/F12-Architecture-Plateforme-Agent.md` §5) : run d'extraction manqué, file de
push qui grossit, échéance déclarative proche, version d'agent obsolète. Ne vous reposez pas
encore sur celles-ci.

### Régler les seuils, le contact et le routage (`/parametrage/alertes`)

Page « Alertes & supervision » (autorisation **paramétrage**), par tenant :

- **Seuils** : heures de silence agent, jours documents bloqués, jours rejets PA (« Enregistrer »).
- **Contact d'alerte** : l'e-mail destinataire des alertes critiques du tenant.
- **Matrice de routage** : associe règle/gravité → liste d'e-mails destinataires.

> La **matrice de routage** reprend les gravités des règles ci-dessus : une alerte critique
> mérite un destinataire et, côté support, un SLA (voir le kit support, lot DOC02, à produire).

---

## 9. Mettre à jour l'instance (auto-hébergée)

L'instance est une **appliance Docker** : la mise à jour consiste à reconstruire l'image de
l'Hôte depuis le code et à relancer la stack, les **volumes persistants** (bases, archive, clés)
étant conservés :

```bash
cd deploy/docker/appliance
git pull                      # récupérer la nouvelle version
docker compose build --pull   # reconstruire l'image liakont
docker compose up -d          # relance ; les volumes sont préservés
docker compose logs -f liakont
```

> **Ne supprimez jamais le volume `liakont-app-data`** : il contient les **clés de chiffrement
> (DPAPI/Data Protection)**, sans lesquelles les secrets tenant (clés API des PA) deviennent
> illisibles après redéploiement, **et** le coffre d'archive.

> **Les agents se mettent à jour** via le mécanisme d'auto-update (manifeste signé + vérification
> de hash), piloté par la politique de flotte côté plateforme. Voir
> `docs/conception/F12-Architecture-Plateforme-Agent.md` et les ADR correspondants.

---

## 10. Sauvegardes et PRA

Scripts livrés dans `deploy/docker/` :

- **Sauvegarde** : `backup.sh` — exporte chaque base (système + tenants + Keycloak), archive le
  volume applicatif (`liakont-app-data`), calcule les empreintes SHA-256 et écrit un
  `manifest.json`. S'exécute **sur l'instance vivante** (dump cohérent en ligne) et fait une
  rotation des sauvegardes.
  ```bash
  ./backup.sh -d /var/backups/liakont -k 14     # conserve les 14 dernières
  ```
  Cron conseillé : tous les jours à 01:30 UTC.

- **Restauration** : `restore.sh` — **vérifie d'abord les SHA-256** (refuse une sauvegarde
  altérée), restaure les bases, puis le volume (refuse d'écraser un volume non vide sans `-f`).
  Cible : une appliance **vierge** (nouvelle machine, PRA, migration).
  ```bash
  ./restore.sh -s /var/backups/liakont/<horodatage>
  ```

> **Base ET volume sont indissociables.** L'intégrité de l'archive (chaîne de hashes ancrée)
> exige **les deux** : la base porte la chaîne scellée, le volume porte les paquets et preuves.
> Une sauvegarde sans l'un des deux donne une archive cassée. **Après toute restauration,
> vérifiez l'intégrité** (console « Paramétrage » → « Vérifier l'intégrité de l'archive », ou
> `POST /api/v1/archive/verify` par tenant) **avant de remettre l'instance en service** : un
> résultat « non intègre » ou un coffre vide = restauration incomplète, ne pas remettre en ligne.

Objectifs par défaut : **RPO ≤ 24 h** (fréquence des sauvegardes), **RTO ≤ 4 h**. Détails et
matrice de preuves : voir le README/PRA dans `deploy/docker/`.

---

## 11. Réversibilité — exporter un client

La **réversibilité** est garantie : un client peut récupérer l'intégralité de son dossier.

- Depuis la console « Paramétrage » (autorisation **paramétrage**) : **export complet du tenant**
  — un ZIP réunissant tout le paramétrage et la piste d'audit du dossier, **sans aucun secret**
  (`GET /api/v1/tenant-export`). Une confirmation est demandée.
- L'**export d'audit par période** (`GET /api/v1/audit-export?from=&to=`) est aussi disponible
  (autorisation **lecture**).

> **État actuel :** l'export de réversibilité du tenant (ci-dessus) est livré. Les outils
> complets de **migration d'instance** et de **fin de vie** d'un client (lot OPS06) sont **en
> cours de développement** ; cette section sera complétée quand ils seront disponibles.

---

## 12. Sécurité des secrets — récapitulatif

- **Clés API des PA** : en base, **chiffrées** (Data Protection), par tenant, jamais en clair ni
  journalisées.
- **Clé API de l'agent** : **DPAPI** côté poste client ; affichée une seule fois à l'émission.
- **Aucune donnée client dans le code** : SIREN, table TVA réelle, chaîne ODBC, compte PA →
  `deployments/<client>/` ; le dépôt n'embarque que des exemples fictifs (`config/exemples/`).
- **Volume `liakont-app-data`** : contient les clés de chiffrement — à protéger et sauvegarder
  comme un secret.

---

## 13. Documentation de référence

| Document | Rôle |
|---|---|
| [blueprint.md](../blueprint.md) | Doctrine d'architecture du produit |
| [docs/conception/F12-Architecture-Plateforme-Agent.md](conception/F12-Architecture-Plateforme-Agent.md) | Architecture plateforme + agent, contrat d'ingestion, supervision, déploiement |
| [docs/conception/F12-A-Parametrage-Tenant.md](conception/F12-A-Parametrage-Tenant.md) | Paramétrage d'un tenant (profil, fiscal, table TVA, comptes PA, seuils) |
| [docs/installation-agent.md](installation-agent.md) | Installation de l'agent chez le client final |
| [docs/conception/F13-Installateur-Agent-Profils-Integrateur.md](conception/F13-Installateur-Agent-Profils-Integrateur.md) | Installateur GUI + profils intégrateur |
| [deploy/docker/appliance/README.md](../deploy/docker/appliance/README.md) | Mise en service de l'appliance |
| [docs/architecture/ajouter-un-plugin-pa.md](architecture/ajouter-un-plugin-pa.md) | Ajouter un plug-in Plateforme Agréée |
| [guide de l'opérateur](guide-operateur.md) | Usage quotidien de la console comptable |
