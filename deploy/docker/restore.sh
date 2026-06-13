#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# deploy/docker/restore.sh — Restauration de l'appliance Liakont dans une instance VIERGE (OPS01b).
#
# Restaure une sauvegarde produite par backup.sh : chaque base (système + tenants + Keycloak) est
# restaurée par pg_restore, et le volume applicatif (coffre WORM + clés Data Protection + staging
# + PDF) est restitué. L'INTÉGRITÉ de la sauvegarde est vérifiée (SHA-256) AVANT toute restauration :
# une sauvegarde altérée est refusée (jamais de restauration silencieusement corrompue).
#
# Cible nominale : une appliance NEUVE (PRA, ou migration OPS06b). Le volume du coffre WORM ne doit
# pas être écrasé par mégarde : restore.sh refuse un volume non vide sauf -f (cible neuve assumée).
#
# Après restauration, VÉRIFIER l'intégrité du coffre via le vérifieur TRK06 (voir SAUVEGARDE-PRA.md) :
# c'est la preuve que les archives fiscales sont récupérables. Le test d'intégration
# BackupRestoreRoundTripTests prouve « restore → vérifieur du coffre VERT » de bout en bout.
#
# Usage :   ./restore.sh -s <dossier_sauvegarde> [-f]
#   -s  dossier d'une sauvegarde (ex : ./backups/20260611T203000Z)   (obligatoire)
#   -f  forcer la restitution du volume même s'il n'est pas vide (DANGEREUX : coffre WORM)
#
# Surcharges d'environnement : voir _backup-common.sh (mêmes variables que backup.sh).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/docker/_backup-common.sh
. "${SCRIPT_DIR}/_backup-common.sh"

SOURCE=""
FORCE=0
while getopts ":s:fh" opt; do
  case "${opt}" in
    s) SOURCE="${OPTARG}" ;;
    f) FORCE=1 ;;
    h) grep -E '^#' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) bk_die "Option inconnue. -h pour l'aide." ;;
  esac
done

[ -n "${SOURCE}" ] || bk_die "Préciser le dossier de sauvegarde : -s <dossier>"
[ -d "${SOURCE}" ] || bk_die "Dossier de sauvegarde introuvable : ${SOURCE}"
SOURCE="$(cd "${SOURCE}" && pwd)"

bk_require_cmds docker tar

# ── 1. Intégrité : refuser une sauvegarde altérée AVANT de restaurer quoi que ce soit ──
bk_verify_sha256sums "${SOURCE}"

# ── 2. Bases : restaurer chaque dump (système + tenants + Keycloak) ──
shopt -s nullglob
for dump in "${SOURCE}"/db-*.dump; do
  name="$(basename "${dump}")"
  name="${name#db-}"
  name="${name%.dump}"
  if [ "${name}" = "keycloak" ]; then
    bk_restore_database "${LIAKONT_KC_DB_SERVICE}" "${LIAKONT_KC_DB_USER}" "${LIAKONT_KC_DB}" "${dump}"
  else
    bk_restore_database "${LIAKONT_DB_SERVICE}" "${LIAKONT_DB_USER}" "${name}" "${dump}"
  fi
done
shopt -u nullglob

# ── 3. Volume applicatif (coffre WORM + clés DP + staging + PDF) ──
VOLUME_TAR="${SOURCE}/volume-app-data.tar.gz"
if [ -f "${VOLUME_TAR}" ]; then
  bk_restore_volume "${LIAKONT_APP_VOLUME}" "${VOLUME_TAR}" "${LIAKONT_APP_SERVICE:-liakont}" "${FORCE}"
else
  bk_warn "Archive de volume absente (${VOLUME_TAR}) : seules les bases ont été restaurées."
fi

bk_log "Restauration terminée depuis ${SOURCE}"
bk_log "ÉTAPE FINALE : vérifier l'intégrité du coffre (vérifieur TRK06) — voir SAUVEGARDE-PRA.md §4."
