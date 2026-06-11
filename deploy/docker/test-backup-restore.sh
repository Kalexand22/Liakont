#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# deploy/docker/test-backup-restore.sh — Auto-test du round-trip sauvegarde/restauration (OPS01b).
#
# Exerce les VRAIS scripts backup.sh / restore.sh contre une stack RÉDUITE (postgres + keycloak-db +
# un volume applicatif) montée à la volée : seed → backup → vérifie le manifeste → prouve le refus
# d'une sauvegarde altérée → restore dans une instance VIERGE → vérifie que bases ET volume ont
# survécu. Aucune dépendance à l'image du Host : on teste la MÉCANIQUE de sauvegarde (pg_dump/restore
# + tar du volume + SHA-256), la preuve « restore → coffre TRK06 vert » étant portée par le test
# d'intégration BackupRestoreRoundTripTests.
#
# Prérequis : docker (compose). Sortie 0 = OK ; toute assertion en échec = sortie non nulle.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d)"
SRC_PROJECT="liakont-bktest-src-$$"
TGT_PROJECT="liakont-bktest-tgt-$$"
COMPOSE_FILE="${WORK}/compose.yml"
FAILED=0

log()  { printf '[smoke] %s\n' "$*"; }
fail() { printf '[smoke] ÉCHEC : %s\n' "$*" >&2; FAILED=1; }
# docker avec la conversion de chemins Git-Bash neutralisée (chemins internes /data) — no-op sur Linux.
dock() { MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' docker "$@"; }

cleanup() {
  docker compose -p "${SRC_PROJECT}" -f "${COMPOSE_FILE}" down -v --remove-orphans >/dev/null 2>&1 || true
  docker compose -p "${TGT_PROJECT}" -f "${COMPOSE_FILE}" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "${WORK}"
}
trap cleanup EXIT

command -v docker >/dev/null 2>&1 || { echo "docker requis pour l'auto-test" >&2; exit 2; }

cat > "${COMPOSE_FILE}" <<'YAML'
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: liakont
      POSTGRES_PASSWORD: testpw
      POSTGRES_DB: liakont
  keycloak-db:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: keycloak
      POSTGRES_PASSWORD: testpw
      POSTGRES_DB: keycloak
  liakont:
    image: alpine:3.20
    command: sleep infinity
    volumes:
      - liakont-app-data:/app/App_Data
volumes:
  liakont-app-data:
YAML

compose_src() { docker compose -p "${SRC_PROJECT}" -f "${COMPOSE_FILE}" "$@"; }
compose_tgt() { docker compose -p "${TGT_PROJECT}" -f "${COMPOSE_FILE}" "$@"; }

wait_pg() {
  local proj="$1" svc="$2" user="$3" i
  for i in $(seq 1 30); do
    if docker compose -p "${proj}" -f "${COMPOSE_FILE}" exec -T "${svc}" pg_isready -U "${user}" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  return 1
}

# ── Source : monter + seeder (2 tenants, registre, marqueurs, volume) ──
log "Démarrage de la stack source"
compose_src up -d >/dev/null
wait_pg "${SRC_PROJECT}" postgres liakont || { echo "postgres source indisponible" >&2; exit 1; }
wait_pg "${SRC_PROJECT}" keycloak-db keycloak || { echo "keycloak-db source indisponible" >&2; exit 1; }

log "Seed des bases et du volume"
compose_src exec -T postgres createdb -U liakont stratum_t1
compose_src exec -T postgres createdb -U liakont stratum_t2
compose_src exec -T postgres psql -U liakont -d liakont -v ON_ERROR_STOP=1 <<'SQL'
CREATE SCHEMA IF NOT EXISTS outbox;
CREATE TABLE outbox.tenants (id text PRIMARY KEY, database_name text, is_active boolean);
INSERT INTO outbox.tenants VALUES ('t1','stratum_t1',true), ('t2','stratum_t2',true);
CREATE TABLE marker (v text);
INSERT INTO marker VALUES ('system');
SQL
compose_src exec -T postgres psql -U liakont -d stratum_t1 -c "CREATE TABLE marker (v text); INSERT INTO marker VALUES ('t1-data');" >/dev/null
compose_src exec -T postgres psql -U liakont -d stratum_t2 -c "CREATE TABLE marker (v text); INSERT INTO marker VALUES ('t2-data');" >/dev/null
compose_src exec -T keycloak-db psql -U keycloak -d keycloak -c "CREATE TABLE marker (v text); INSERT INTO marker VALUES ('kc-data');" >/dev/null
dock run --rm -v "${SRC_PROJECT}_liakont-app-data":/data alpine:3.20 sh -c \
  'mkdir -p /data/archive-store/acme/2026/05/F-1 /data/dataprotection-keys; \
   printf hello > /data/archive-store/acme/2026/05/F-1/manifest.json; \
   printf dpkey > /data/dataprotection-keys/key-1.xml'

# ── Sauvegarde via le VRAI backup.sh ──
log "Sauvegarde (backup.sh)"
LIAKONT_PROJECT="${SRC_PROJECT}" LIAKONT_COMPOSE_FILE="${COMPOSE_FILE}" \
  bash "${SCRIPT_DIR}/backup.sh" -d "${WORK}/backups" -k 14
BK="$(find "${WORK}/backups" -mindepth 1 -maxdepth 1 -type d -name '*Z' | sort | tail -n 1)"
[ -n "${BK}" ] || { echo "aucune sauvegarde produite" >&2; exit 1; }
log "Sauvegarde : ${BK}"

for f in db-liakont.dump db-stratum_t1.dump db-stratum_t2.dump db-keycloak.dump volume-app-data.tar.gz SHA256SUMS manifest.json; do
  [ -f "${BK}/${f}" ] || fail "artefact attendu absent : ${f}"
done

# ── Garde anti-corruption : une sauvegarde altérée DOIT être refusée ──
log "Contrôle : restauration refusée si artefact altéré"
TAMPER="${WORK}/tampered"
cp -r "${BK}" "${TAMPER}"
printf 'x' >> "${TAMPER}/db-liakont.dump"
if LIAKONT_PROJECT="${TGT_PROJECT}" LIAKONT_COMPOSE_FILE="${COMPOSE_FILE}" \
     bash "${SCRIPT_DIR}/restore.sh" -s "${TAMPER}" >/dev/null 2>&1; then
  fail "restore.sh a accepté une sauvegarde altérée (faux vert d'intégrité)"
else
  log "OK : sauvegarde altérée refusée"
fi

# ── Restauration dans une instance VIERGE ──
log "Démarrage de la stack cible (vierge)"
compose_tgt up -d >/dev/null
wait_pg "${TGT_PROJECT}" postgres liakont || { echo "postgres cible indisponible" >&2; exit 1; }
wait_pg "${TGT_PROJECT}" keycloak-db keycloak || { echo "keycloak-db cible indisponible" >&2; exit 1; }

log "Restauration (restore.sh)"
LIAKONT_PROJECT="${TGT_PROJECT}" LIAKONT_COMPOSE_FILE="${COMPOSE_FILE}" \
  bash "${SCRIPT_DIR}/restore.sh" -s "${BK}"

# ── Assertions : bases ET volume ont survécu au round-trip ──
assert_query() {
  local svc="$1" db="$2" user="$3" expected="$4" got
  got="$(compose_tgt exec -T "${svc}" psql -U "${user}" -d "${db}" -At -c 'SELECT v FROM marker' 2>/dev/null | tr -d '[:space:]' || true)"
  if [ "${got}" = "${expected}" ]; then log "OK : ${db}.marker = ${got}"; else fail "${db}.marker attendu='${expected}' obtenu='${got}'"; fi
}
assert_query postgres liakont liakont system
assert_query postgres stratum_t1 liakont t1-data
assert_query postgres stratum_t2 liakont t2-data
assert_query keycloak-db keycloak keycloak kc-data

TENANTS="$(compose_tgt exec -T postgres psql -U liakont -d liakont -At -c 'SELECT count(*) FROM outbox.tenants' 2>/dev/null | tr -d '[:space:]' || true)"
[ "${TENANTS}" = "2" ] && log "OK : registre des tenants restauré (2)" || fail "registre des tenants : attendu 2, obtenu '${TENANTS}'"

VOL_CONTENT="$(dock run --rm -v "${TGT_PROJECT}_liakont-app-data":/data:ro alpine:3.20 \
  sh -c 'cat /data/archive-store/acme/2026/05/F-1/manifest.json 2>/dev/null' || true)"
[ "${VOL_CONTENT}" = "hello" ] && log "OK : coffre WORM restitué" || fail "contenu du coffre restitué inattendu : '${VOL_CONTENT}'"

VOL_KEY="$(dock run --rm -v "${TGT_PROJECT}_liakont-app-data":/data:ro alpine:3.20 \
  sh -c 'cat /data/dataprotection-keys/key-1.xml 2>/dev/null' || true)"
[ "${VOL_KEY}" = "dpkey" ] && log "OK : trousseau Data Protection restitué" || fail "clé DP restituée inattendue : '${VOL_KEY}'"

if [ "${FAILED}" -eq 0 ]; then
  log "RÉUSSITE : round-trip sauvegarde/restauration complet."
  exit 0
else
  echo "[smoke] DES ASSERTIONS ONT ÉCHOUÉ." >&2
  exit 1
fi
