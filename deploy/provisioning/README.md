# Provisioning d'instances Liakont (OPS02)

Outillage de **création** et de **mise à jour** d'instances Liakont, par-dessus la stack appliance
(`deploy/docker/appliance`, OPS01a). Source : **F12 §6.3** (« Provisioning »).

> Modèle (F12 §6.2) : **un répertoire/stack par instance**. Chaque instance est un projet Docker
> Compose distinct (`liakont-<nom>`) → ses conteneurs, volumes et réseau sont préfixés et **isolés**
> des autres instances sur la même machine. Plusieurs instances coexistent sans interférence.

| Fichier | Rôle |
|---|---|
| `new-instance.ps1` | Crée une instance (répertoire dédié, secrets uniques, realm, .env) — modes `hosted` / `self-hosted` |
| `update-instance.ps1` | Montée de version **multi-bases** sûre (maintenance 503 → sauvegarde → migration → santé/rollback) |
| `migrate-instance.ps1` | **Migration d'instance** (réversibilité OPS06b) : EXPORT d'un bundle complet (toutes bases + volume) sur la source, APPLY (restauration + santé + bascule DNS) sur la cible |
| `decommission-tenant.ps1` | **Fin de vie d'un tenant** (résiliation/RGPD — OPS06c, décision D7) : désactivation logique, puis suppression encadrée (export vérifié + double confirmation + audit d'instance) |
| `Provisioning.psm1` | Fonctions partagées (validation de nom, secrets, registre, enveloppes Docker, audit de fin de vie) |
| `maintenance.Caddyfile` | Caddyfile de maintenance (503 + Retry-After sur `/api/agent/*`) posé pendant une migration |
| `instances.example.yaml` | **Exemple FICTIF** du format du registre (le registre réel n'est pas versionné) |

> **Aucune donnée client n'est versionnée** (CLAUDE.md n°7). Le `.env` de chaque instance (secrets) et
> le registre réel `instances.yaml` (noms d'éditeurs, URLs) sont **gitignorés**. Seul
> `instances.example.yaml` (fictif) est suivi.

---

## 1. Prérequis

- **Docker Engine ≥ 24** avec le plugin **Compose v2** (`docker compose version`).
- Les prérequis de l'appliance (RAM, DNS, ports 80/443) — voir `deploy/docker/appliance/README.md`.
- **Windows PowerShell 5.1** ou **PowerShell 7+** (les scripts tournent sur les deux).

---

## 2. Créer une instance

### Mode hébergé (`hosted`) — déploiement sur l'infra IT Innovations

```powershell
./new-instance.ps1 -InstanceName acme-prod -Editor "ACME SAS" `
    -PublicHostname liakont.acme.example -KeycloakHostname id.acme.example `
    -AcmeEmail ops@acme.example -Mode hosted
```

Le script :

1. crée le répertoire d'instance `instances/acme-prod/` (copie du compose appliance — contexte de
   build réécrit en chemin absolu —, du `Caddyfile`, du `Caddyfile` de maintenance et du realm) ;
2. génère **4 secrets forts et uniques** dans `instances/acme-prod/.env`
   (`POSTGRES_PASSWORD`, `KC_DB_PASSWORD`, `KC_BOOTSTRAP_ADMIN_PASSWORD`,
   `KEYCLOAK_LIAKONT_CLIENT_SECRET`) — **jamais de valeur par défaut ni partagée entre instances** ;
3. déploie : `docker compose -p liakont-acme-prod up -d --build` ;
4. inscrit l'instance dans le **registre** `instances.yaml`.

> Branding **par défaut Liakont** tant que BRD01 n'est pas appliqué ; le branding complet (nom
> éditeur, logo, couleurs) est enrichi par BRD01.

### Mode auto-hébergé (`self-hosted`) — bundle à remettre à l'éditeur

```powershell
./new-instance.ps1 -InstanceName acme-prod -Editor "ACME SAS" `
    -PublicHostname liakont.acme.example -KeycloakHostname id.acme.example `
    -AcmeEmail ops@acme.example -Mode self-hosted
```

Aucun déploiement ici : le script produit `instances/acme-prod-bundle.zip` (config + realm +
`BUNDLE-README.md`) à remettre à l'éditeur, qui le dépose dans **sa** copie de l'appliance et démarre.
L'instance n'est inscrite au registre que si l'éditeur a souscrit la méta-supervision
(option `-WithSupervision`, OPS04).

Ajouter `-DryRun` à toute commande affiche le plan **sans rien écrire**.

---

## 3. Mettre à jour une instance (montée de version multi-bases)

```powershell
./update-instance.ps1 -InstanceName acme-prod
```

La séquence garantit qu'**une instance ne tourne jamais avec des bases à des versions différentes**
(database-per-tenant) :

1. **Maintenance** — le `Caddyfile` bascule en maintenance et est rechargé à chaud : les push d'agents
   reçoivent un **503 explicite** (`Retry-After`) pendant toute l'opération. Comportement agent (F12
   §3.3) : back-off, les éléments restent dans le buffer local et sont re-poussés au heartbeat suivant
   — **aucune perte de document**.
2. **Sauvegarde pré-migration OBLIGATOIRE** — `pg_dump` **par base** (système + chaque tenant actif),
   dans `instances/acme-prod/backups/<horodatage>/`. Pas de sauvegarde ⇒ migration **annulée**.
3. **Nouvelle image** — `docker compose build --pull`.
4. **Migration** — `docker compose up -d`. Au démarrage, le Host migre la base **système** puis
   **toutes** les bases tenant (`MigrateExistingTenantsAsync`, boucle DbUp). Si une migration tenant
   **JOIGNABLE** échoue, le Host **avorte son démarrage** (l'`AggregateException` remonte dans
   `AppBootstrap.InitializeDataAsync`) : aucune requête servie sur une base à demi-migrée.
   > **Attention** : un tenant dont la DB est **injoignable** (NpgsqlException) est ignoré
   > silencieusement — le Host démarre quand même. Après une mise à jour réussie, vérifiez
   > `docker compose -p <project> logs liakont` pour tout avertissement « tenant migration skipped ».
5. **Santé** :
   - **succès** → sortie de maintenance (503 levé), registre mis à jour (version + date) ;
   - **échec** → le service Host est **ARRÊTÉ**, l'instance reste **hors ligne** (jamais d'état mixte
     en marche), la **maintenance reste active** (agents bloqués), et un **rapport de rollback** est
     affiché.

> `-MigrationTimeoutSeconds` (défaut 300) borne l'attente du démarrage stable du Host.

### Rollback (en cas d'échec)

1. Restaurer les bases depuis `instances/<nom>/backups/<horodatage>/` (un `.sql` par base).
   Le dump est pris avec `--clean --if-exists` : la restauration est **idempotente** même si la base
   contient déjà le nouveau schéma. Dans le conteneur `postgres` :
   ```
   psql -v ON_ERROR_STOP=1 -U liakont -d <base> -f <base>.sql
   ```
2. redéployer la **version précédente** du code ;
3. relancer `update-instance.ps1` une fois l'instance saine : il lève la maintenance.

---

## 4. Migrer une instance (réversibilité — OPS06b)

La **réversibilité** (déplacer une instance d'une cible à une autre — *dédiée hébergée* → *self-hosted*,
ou changement de machine) est une exigence de V1 (F12 §6.3). `migrate-instance.ps1` la réalise en deux
phases autour d'un **bundle de migration** intègre, en réutilisant la mécanique de sauvegarde/restauration
**par base** déjà testée d'OPS01b (`deploy/docker/backup.sh` / `restore.sh`).

### EXPORT (sur la SOURCE)

```powershell
./migrate-instance.ps1 -InstanceName acme-prod -TargetMode self-hosted
```

`pg_dump` de **toutes** les bases (système + chaque tenant, **actif OU suspendu** — un tenant suspendu
sous rétention fiscale n'est jamais omis) + archive du **volume applicatif** (coffre d'archive WORM +
clés Data Protection) + **secrets** de l'instance, le tout dans un `.zip` horodaté avec empreintes
SHA-256. L'export est **en lecture seule** : la source reste en service.

> ⚠️ Le bundle contient des **secrets** et des **données fiscales** — transférez-le par un canal sûr et
> **supprimez-le après application**. Il est écrit sous `instances/` (gitignoré). Voir `MIGRATE-README.md`
> dans le bundle.

### APPLY (sur la CIBLE)

```powershell
./migrate-instance.ps1 -ApplyBundle ./instances/acme-prod-migration-<horodatage>.zip
```

Matérialise la cible (config + **secrets préservés** → Keycloak/Host cohérents), **vérifie l'intégrité**
(SHA-256) avant de restaurer, restaure toutes les bases + le volume (le coffre WORM n'est restitué que
sur une cible **vierge**), attend un **démarrage stable du Host** (contrôle de santé), puis affiche la
procédure de **bascule DNS**. En cas d'échec de santé, la source est intacte — **rollback = ne pas
basculer le DNS**.

Ajouter `-DryRun` à l'une ou l'autre phase affiche la séquence **sans rien écrire**.

> Migration d'une **instance entière** (cet outil) ≠ fin de vie d'**un tenant** au sein d'une instance
> (résiliation/RGPD avec conservation fiscale) : pour cette dernière, voir le § 5 (`decommission-tenant.ps1`).

---

## 5. Fin de vie d'un tenant (résiliation/RGPD — OPS06c, décision D7)

La **résiliation d'un client** doit concilier le droit à l'effacement (RGPD) avec le **coffre WORM** et la
**conservation fiscale 10 ans**. La décision **D7** (2026-06-03) fixe une séquence **stricte** : pas de
suppression sans **preuve** que le client a récupéré une **archive intègre**. `decommission-tenant.ps1`
la réalise en deux actions.

### Désactivation (par défaut)

```powershell
./decommission-tenant.ps1 -InstanceName acme-prod -Tenant acme
```

Marque le tenant **inactif** dans le catalogue système (`outbox.tenants.is_active = false`) et, au mieux,
suspend le service live (`tenant_profiles.statut = 1` « Suspendu », statut OPS03). **Plus de push, plus de
connexion ; les données restent intactes.** État réversible — et **légitime en permanence** : un tenant
résilié qui ne demande pas l'effacement reste désactivé indéfiniment (D7, point 6).

### Suppression (`-Delete`) — séquence D7 complète

```powershell
./decommission-tenant.ps1 -InstanceName acme-prod -Tenant acme -Delete `
    -VerifiedExportPath ./exports/acme-reversibilite.zip `
    -Operator ops@itinnov.example -Recipient dpo@acme.example `
    -Yes -ConfirmTenantName acme
```

1. **Désactivation** (ci-dessus) ;
2. **Export complet VÉRIFIÉ** — l'export de réversibilité (produit par OPS06a) est **re-contrôlé** par
   `tools/verifier-integrite-archive.ps1` (TRK06/TRK05). La suppression est **REFUSÉE** si l'export est
   **ALTÉRÉ** (`VERDICT=TAMPERED`), **PARTIEL** (`VERDICT=INCOMPLETE` — il faut l'export de réversibilité
   **COMPLET**, pas un export de contrôle fiscal), absent ou illisible ;
3. **Transfert de responsabilité documenté** — la notice rappelle que la conservation fiscale (10 ans)
   passe au **client destinataire** avec l'export ;
4. **Double confirmation** + **saisie exacte du nom du tenant** (`-Yes` + `-ConfirmTenantName`, ou en
   interactif les invites « OUI » puis re-saisie du nom) ;
5. **Audit d'instance écrit AVANT la suppression** — fichier **hôte**
   `instances/<instance>/tenant-decommission-audit.jsonl` (append-only, gitignoré), consignant **qui,
   quand, le tenant, la référence de l'export vérifié et le destinataire**. Il **SURVIT** à la suppression
   de la base du tenant : c'est la preuve de fin de conservation chez l'opérateur ;
6. **Suppression** de la base du tenant + retrait du catalogue système.

> **Bloquer plutôt qu'envoyer faux** (CLAUDE.md n°3) : tant que toutes les gardes ne sont pas vertes,
> **aucune action destructrice** n'est entreprise (l'export n'est que **lu**). Ajouter `-DryRun` affiche
> la séquence — après les gardes — **sans rien supprimer**.

---

## 6. Registre des instances

`instances.yaml` (non versionné) liste les instances **opérées par IT Innovations** : `name`,
`editor`, `url`, `hosting`, `version`, `project`, `created_at`, `updated_at`. **Aucun secret** n'y
figure (mots de passe et clés vivent dans le `.env` de chaque instance). Les instances self-hosted
n'y apparaissent que si l'éditeur a souscrit la méta-supervision (OPS04). Voir
`instances.example.yaml` pour le format (valeurs fictives).

---

## 7. Vérification

La logique des scripts (validation, secrets, registre, états vide/sale/échec, codes de sortie de
`migrate-instance.ps1`, **gardes de fin de vie de `decommission-tenant.ps1`** : refus sur export
absent/altéré/partiel, confirmation, audit d'instance append-only) est couverte par le self-test
`tools/test-provisioning.ps1` (câblé dans `tools/run-tests.ps1`, PowerShell pur, sans Docker — la
vérification d'intégrité y appelle le **vrai** `verifier-integrite-archive.ps1`).

Le **round-trip réel** avec Docker est porté par trois auto-tests (recette humaine `GATE_TOOLKIT`,
comme l'appliance OPS01a) :

- `deploy/docker/test-backup-restore.sh` — sauvegarde/restauration (OPS01b) ;
- `deploy/provisioning/test-migrate-instance.sh` — **migration de bout en bout** (OPS06b) : EXPORT +
  APPLY sur une stack réduite (source + cible), avec un tenant **suspendu** (preuve qu'il migre quand
  même) et vérification que toutes les bases + le coffre WORM + les clés Data Protection ont survécu,
  santé verte sur la cible ;
- `deploy/provisioning/test-decommission-tenant.sh` — **fin de vie d'un tenant de bout en bout** (OPS06c) :
  désactivation, **refus** de suppression sur export altéré, puis suppression sur export vert avec
  vérification que la base tenant est supprimée, le tenant retiré du catalogue, et que l'**audit
  d'instance survit** à la suppression de la base.
