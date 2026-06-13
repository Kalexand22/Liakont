# Sauvegarde, restauration et Plan de Reprise d'Activité (PRA) — appliance Liakont

Item **OPS01b** (F12 §6.2). Cette procédure fait partie du livrable : *une sauvegarde jamais
restaurée est un faux vert*. L'opérateur héberge des **archives fiscales de tiers conservées 10 ans**
— leur récupérabilité est une obligation, pas une option.

Outillage : [`backup.sh`](backup.sh) · [`restore.sh`](restore.sh) · auto-test
[`test-backup-restore.sh`](test-backup-restore.sh) · bibliothèque [`_backup-common.sh`](_backup-common.sh).

---

## 1. Périmètre — ce qui est sauvegardé

| Élément | Source | Artefact |
|---|---|---|
| Base **système** (`liakont`) + tenant par défaut | service `postgres` | `db-liakont.dump` (`pg_dump -Fc`) |
| Une base **par tenant** (database-per-tenant) | service `postgres`, énumérées depuis `outbox.tenants` | `db-<base>.dump` |
| Base **Keycloak** (utilisateurs, sessions, realm runtime) | service `keycloak-db` | `db-keycloak.dump` |
| **Coffre WORM** + **trousseau Data Protection** + staging + PDF | volume `liakont-app-data` | `volume-app-data.tar.gz` |
| Empreintes d'intégrité | calculées à la sauvegarde | `SHA256SUMS` + `manifest.json` |

**Pourquoi la base ET le volume.** Le coffre d'archive est vérifié (vérifieur TRK06) à partir de
**deux sources indissociables** : la chaîne de hashes scellée **en base** (`documents.archive_entries`,
`documents.archive_anchors`) ET les **fichiers de paquet + preuves d'ancrage sur le volume**.
Sauvegarder l'un sans l'autre est un faux vert : sans le volume la chaîne ne se recalcule pas
(coffre rompu) ; sans la base le coffre restauré est **vide** (toutes les écritures fiscales perdues).
Les deux contrôles négatifs du test d'intégration `BackupRestoreRoundTripTests` le prouvent.

**Non sauvegardé (reconstructible) :** certificats Caddy (`caddy-data` — réémis par Let's Encrypt),
base de données Keycloak *system* hors `keycloak-db` (sans objet : le realm est aussi importé du
fichier au démarrage). L'image du Host se reconstruit depuis le code (`docker compose build`).

> ⚠️ **Le volume `liakont-app-data` est porteur de secret** (trousseau Data Protection en clair hors
> Windows — cf. `appliance/README.md` §4). **Chiffrez les sauvegardes** (`age`, `gpg`, ou volume de
> destination chiffré) et restreignez l'accès aux fichiers `volume-app-data.tar.gz`.

---

## 2. Sauvegarde

```bash
cd deploy/docker
./backup.sh -d /var/backups/liakont -k 14   # -d : destination ; -k : nb de sauvegardes conservées
```

Chaque exécution crée un dossier horodaté `…/<YYYYMMDDThhmmssZ>/` contenant les dumps, l'archive du
volume, `SHA256SUMS` et `manifest.json`. La sauvegarde tourne **sur l'instance en marche** (`pg_dump`
est cohérent en ligne) — aucun arrêt de service requis.

**Planification (cron, exemple quotidien à 01:30) :**

```cron
30 1 * * *  cd /opt/liakont/deploy/docker && ./backup.sh -d /var/backups/liakont -k 14 >> /var/log/liakont-backup.log 2>&1
```

**Rétention.** `-k` ne gère que la **rotation locale** (sauvegardes récentes sur la machine). La
conservation **10 ans** des archives fiscales impose une stratégie **3-2-1** : copier les sauvegardes
**hors site** (objet/cloud chiffré, bande), au moins une copie immuable. La rotation locale ne remplace
pas l'archivage long terme.

---

## 3. Objectifs RTO / RPO

Valeurs **par défaut recommandées**, à ajuster par contrat d'hébergement (SLA) — ce sont des cibles
d'exploitation, pas des règles fiscales :

| Objectif | Cible par défaut | Levier |
|---|---|---|
| **RPO** (perte de données max.) | **24 h** | fréquence des sauvegardes (cron). Échéance déclarative imminente → passer à une cadence horaire la veille. |
| **RTO** (temps de remise en service) | **≤ 4 h** pour une instance | taille des bases + du coffre, débit de restauration, disponibilité du bundle d'installation. |

> Une panne d'instance bloque **tous ses tenants simultanément** à l'approche d'une échéance
> déclarative : le RTO conditionne le respect des dates de transmission. Pour une instance mutualisée
> à fort enjeu, viser un RPO horaire la veille des échéances et tester la restauration régulièrement.

---

## 4. Restauration dans une instance VIERGE (PRA)

Cible nominale : une appliance **neuve** (machine de secours, ou migration OPS06b). Préparer la cible
comme une installation standard (`appliance/README.md` §2 : `.env`, `docker compose build`) **sans
démarrer le Host applicatif sur des données**, puis :

```bash
cd deploy/docker

# 1. Amener la stack cible (bases vides + volume vide) — ou au minimum postgres + keycloak-db.
docker compose -f appliance/docker-compose.yml up -d postgres keycloak-db

# 2. Restaurer la sauvegarde choisie (l'intégrité SHA-256 est vérifiée AVANT toute écriture).
./restore.sh -s /var/backups/liakont/20260611T013000Z

# 3. Démarrer le reste de la stack.
docker compose -f appliance/docker-compose.yml up -d
```

`restore.sh` :

1. **vérifie `SHA256SUMS`** et **refuse** une sauvegarde altérée (aucune restauration partielle) ;
2. **`pg_restore`** chaque base (système + tenants + Keycloak) — crée la base si absente ;
3. **restitue le volume** `liakont-app-data` (coffre WORM + clés DP + staging + PDF). Par sécurité,
   il **refuse d'écraser un volume non vide** (le coffre WORM ne doit jamais être écrasé par
   mégarde) ; sur une cible neuve le volume est vide. Forcer avec `-f` est réservé à une cible
   **assumée neuve** (PRA / migration).

> **Bascule DNS / mise à jour `.env`.** Après restauration sur une nouvelle machine, mettre à jour les
> hôtes publics (`.env`) et basculer le DNS vers la nouvelle IP. La bascule complète d'instance (export
> + transport + bascule DNS outillés) est portée par **OPS06b**, qui réutilise cette mécanique.

---

## 5. Vérification post-restauration (preuve que le coffre est récupérable)

La restauration n'est **terminée** qu'une fois le **vérifieur du coffre (TRK06) VERT** pour chaque
tenant — c'est la preuve que les archives fiscales sont intègres et opposables.

- **Par l'API**, pour chaque tenant (authentifié `liakont.settings`) :
  `POST /api/v1/archive/verify` → rapport JSON (`isFullyVerified: true` + résumé français). Un
  `isFullyVerified: false` ou un coffre vide inattendu = restauration incomplète (base ou volume
  manquant) : **ne pas mettre l'instance en service**.
- **Par la console** : page **Paramétrage** → état de vérification du coffre.

> Le test d'intégration `src/Modules/Archive/Tests.Integration/BackupRestoreRoundTripTests.cs` exécute
> ce parcours de bout en bout (archive réelle → `pg_dump`/`pg_restore` dans une base vierge + volume
> restitué → vérifieur VERT), et démasque par deux contrôles négatifs les sauvegardes incomplètes.

---

## 6. Preuve testée

| Niveau | Quoi | Lancement |
|---|---|---|
| **Intégration** (CI) | `restore → vérifieur TRK06 vert` + 2 contrôles négatifs (base seule / volume seul) | `tools/run-tests.ps1` |
| **Mécanique scripts** | round-trip réel `backup.sh`/`restore.sh` sur stack réduite (bases + volume + refus d'altération + refus d'un volume introuvable) | `deploy/docker/test-backup-restore.sh` (docker requis ; **hors `verify-fast`/`run-tests`** — lancé avant merge + à `GATE_TOOLKIT`) |
| **Bout-en-bout machine** | restauration d'une instance complète (Host + Keycloak + DNS réel) | **recette humaine `GATE_TOOLKIT`** |

---

## 7. Supervision des sauvegardes (lien OPS04)

Une sauvegarde silencieusement en échec est un faux vert. `backup.sh` **se termine en code non nul** à
la première erreur (`set -euo pipefail`) — le `cron` ci-dessus journalise stdout/stderr. La
**méta-supervision de flotte (OPS04)** consommera cet état (« dernière sauvegarde réussie ») pour
**alerter sur une sauvegarde en échec** au niveau instance. En attendant OPS04, surveiller le code de
retour du cron et l'âge du dernier dossier de sauvegarde (alerte si > RPO).

---

## 8. Limites et revalidation

- La vérification automatisée d'OPS01b couvre la **mécanique** (dump/restore par base, tar du volume,
  intégrité SHA-256) et la **propriété d'intégrité** (vérifieur vert après restauration). La
  restauration **bout-en-bout d'une instance complète** sur machine propre (Host + Keycloak + bascule
  DNS) est **revalidée à la recette humaine `GATE_TOOLKIT`** (mutation machine, hors sandbox) — même
  partage que pour OPS01a/OPS05.
- Chiffrement **au repos** du trousseau Data Protection (certificat / KMS) : hors périmètre OPS01b
  (sauvegarde/PRA) ; la garde ici est de **chiffrer les sauvegardes** et **protéger le volume**.
