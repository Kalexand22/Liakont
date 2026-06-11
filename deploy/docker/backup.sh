#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# deploy/docker/backup.sh — Sauvegarde de l'appliance Liakont (OPS01b, F12 §6.2).
#
# Sauvegarde PAR BASE (base système + une base par tenant + base Keycloak) au format custom
# pg_dump (-Fc, restauration sélective exigée par la réversibilité OPS06), PLUS le volume
# applicatif `liakont-app-data` qui porte le coffre WORM (conservation fiscale 10 ans), le
# trousseau Data Protection (secrets tenant chiffrés), le staging du pivot et les PDF reçus.
#
# Un manifeste (manifest.json) liste chaque artefact avec son empreinte SHA-256 : restore.sh
# REFUSE de restaurer un artefact altéré (une sauvegarde jamais vérifiée est un faux vert).
#
# Usage :   ./backup.sh [-d <dossier_destination>] [-k <nb_a_conserver>]
#   -d  dossier racine des sauvegardes (défaut : $BACKUP_DIR ou ./backups)
#   -k  rotation : nombre de sauvegardes à conserver (défaut : $BACKUP_KEEP ou 14)
#
# Surcharges d'environnement (défauts = appliance OPS01a) :
#   LIAKONT_PROJECT (liakont-appliance) · LIAKONT_COMPOSE_FILE (docker-compose.yml du dossier)
#   LIAKONT_DB_SERVICE (postgres) · LIAKONT_DB_USER (liakont) · LIAKONT_SYSTEM_DB (liakont)
#   LIAKONT_KC_DB_SERVICE (keycloak-db) · LIAKONT_KC_DB_USER (keycloak) · LIAKONT_KC_DB (keycloak)
#   LIAKONT_APP_VOLUME (<project>_liakont-app-data)
#
# Planification (cron) et objectifs RTO/RPO : voir SAUVEGARDE-PRA.md.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/docker/_backup-common.sh
. "${SCRIPT_DIR}/_backup-common.sh"

BACKUP_DIR="${BACKUP_DIR:-${SCRIPT_DIR}/backups}"
BACKUP_KEEP="${BACKUP_KEEP:-14}"

while getopts ":d:k:h" opt; do
  case "${opt}" in
    d) BACKUP_DIR="${OPTARG}" ;;
    k) BACKUP_KEEP="${OPTARG}" ;;
    h) grep -E '^#' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) bk_die "Option inconnue. -h pour l'aide." ;;
  esac
done

bk_require_cmds docker tar

# Un horodatage UTC trié = un dossier de sauvegarde ; la rotation conserve les N plus récents.
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DEST="${BACKUP_DIR}/${STAMP}"
mkdir -p "${DEST}"
bk_log "Sauvegarde de l'appliance « ${LIAKONT_PROJECT} » → ${DEST}"

# ── 1. Bases applicatives : système + une par tenant (énumérées par existence sur le cluster) ──
# Capture HORS substitution de processus : un bk_die de l'énumération doit tuer backup.sh (set -e),
# pas seulement le sous-shell d'un « < <(…) » (sinon APP_DBS vide → sauvegarde sans aucune base, sortie 0).
app_dbs_out="$(bk_list_app_databases)"
[ -n "${app_dbs_out}" ] || bk_die "Aucune base applicative trouvée (la base système devrait toujours être présente) — abandon."
mapfile -t APP_DBS <<< "${app_dbs_out}"
bk_log "Bases applicatives à sauvegarder : ${APP_DBS[*]}"
for db in "${APP_DBS[@]}"; do
  bk_dump_database "${LIAKONT_DB_SERVICE}" "${LIAKONT_DB_USER}" "${db}" "${DEST}/db-${db}.dump"
done

# ── 2. Base Keycloak (utilisateurs/realm runtime ; tolérée absente sur une stack réduite) ──
if bk_service_running "${LIAKONT_KC_DB_SERVICE}"; then
  bk_dump_database "${LIAKONT_KC_DB_SERVICE}" "${LIAKONT_KC_DB_USER}" "${LIAKONT_KC_DB}" "${DEST}/db-keycloak.dump"
else
  bk_warn "Service ${LIAKONT_KC_DB_SERVICE} absent : base Keycloak non sauvegardée (stack réduite)."
fi

# ── 3. Volume applicatif (coffre WORM + clés Data Protection + staging + PDF) ──
bk_archive_volume "${LIAKONT_APP_VOLUME}" "${DEST}/volume-app-data.tar.gz"

# ── 4. Manifeste + empreintes SHA-256 (vérifiées au restore) ──
bk_write_manifest "${DEST}" "${STAMP}" "${APP_DBS[@]}"

# ── 5. Rotation : ne conserver que les BACKUP_KEEP sauvegardes les plus récentes ──
bk_rotate "${BACKUP_DIR}" "${BACKUP_KEEP}"

bk_log "Sauvegarde terminée : ${DEST}"
bk_log "Vérifier/Restaurer : ./restore.sh -s \"${DEST}\""
