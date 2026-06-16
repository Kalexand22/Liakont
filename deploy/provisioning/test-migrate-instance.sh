#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# deploy/provisioning/test-migrate-instance.sh — Auto-test du round-trip de MIGRATION (OPS06b).
#
# Exerce le VRAI migrate-instance.ps1 (EXPORT + APPLY) contre une stack RÉDUITE montée à la volée :
#   SOURCE  : postgres + keycloak-db + un service « liakont » (busybox httpd, répond en HTTP:8080) +
#             un service « caddy » (alpine, fournit wget pour la sonde de santé) + un volume applicatif.
#   - seed  : base système (registre outbox.tenants avec un tenant ACTIF *et* un tenant SUSPENDU),
#             une base par tenant, base Keycloak, coffre WORM + clé Data Protection dans le volume ;
#   EXPORT  : migrate-instance.ps1 -InstanceName … → bundle de migration (.zip) ;
#   APPLY   : migrate-instance.ps1 -ApplyBundle … → CIBLE vierge, restauration + contrôle de santé ;
#   - assert: TOUTES les bases (système + 2 tenants, dont le SUSPENDU — preuve fiscale : un tenant
#             suspendu n'est jamais silencieusement omis) + Keycloak + le coffre WORM + la clé DP ont
#             survécu à la migration, et la santé est verte sur la cible.
#
# On teste la MÉCANIQUE de migration (backup.sh/restore.sh orchestrés par migrate-instance.ps1 +
# matérialisation de la cible + sonde de santé) avec un faux Host (busybox) : la santé du VRAI Host
# (migrations DbUp, OIDC) est revalidée à la recette humaine GATE_TOOLKIT, comme l'appliance (OPS01a).
#
# Prérequis : docker (compose) + pwsh|powershell. Sortie 0 = OK ; toute assertion en échec = non nul.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MIGRATE_PS1="${SCRIPT_DIR}/migrate-instance.ps1"
WORK="$(mktemp -d)"
SRC_NAME="mig-src-$$"
TGT_NAME="mig-tgt-$$"
SRC_PROJECT="liakont-${SRC_NAME}"
TGT_PROJECT="liakont-${TGT_NAME}"
INSTANCES_ROOT="${WORK}/instances"
BUNDLE_DIR="${WORK}/bundles"
SRC_DIR="${INSTANCES_ROOT}/${SRC_NAME}"
COMPOSE_FILE="${SRC_DIR}/docker-compose.yml"
FAILED=0

log()  { printf '[migrate-smoke] %s\n' "$*"; }
fail() { printf '[migrate-smoke] ÉCHEC : %s\n' "$*" >&2; FAILED=1; }
dock() { MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' docker "$@"; }

cleanup() {
  docker compose -p "${SRC_PROJECT}" -f "${COMPOSE_FILE}" down -v --remove-orphans >/dev/null 2>&1 || true
  docker compose -p "${TGT_PROJECT}" --project-directory "${INSTANCES_ROOT}/${TGT_NAME}" \
    -f "${INSTANCES_ROOT}/${TGT_NAME}/docker-compose.yml" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "${WORK}"
}
trap cleanup EXIT

command -v docker >/dev/null 2>&1 || { echo "docker requis pour l'auto-test" >&2; exit 2; }

# Exécutable PowerShell (pwsh prioritaire ; powershell en repli sous Windows).
PS_EXE=""
if command -v pwsh >/dev/null 2>&1; then PS_EXE="pwsh"; elif command -v powershell >/dev/null 2>&1; then PS_EXE="powershell"; fi
[ -n "${PS_EXE}" ] || { echo "pwsh|powershell requis pour l'auto-test (migrate-instance.ps1)" >&2; exit 2; }

# Convertit un chemin pour l'exécutable PowerShell. Sous MSYS/Cygwin (Git Bash sur Windows), traduit
# « /c/… » en chemin Windows (Windows PowerShell n'interprète pas les chemins POSIX). Sous Linux/macOS,
# chemin inchangé (cygpath absent) : la cible nominale de cet auto-test est l'appliance Linux.
to_pspath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
MIGRATE_PS1_PS="$(to_pspath "${MIGRATE_PS1}")"
INSTANCES_ROOT_PS="$(to_pspath "${INSTANCES_ROOT}")"
BUNDLE_DIR_PS="$(to_pspath "${BUNDLE_DIR}")"

run_ps() { "${PS_EXE}" -NoProfile -ExecutionPolicy Bypass -File "${MIGRATE_PS1_PS}" "$@"; }

# ── Stack réduite (services nommés comme l'appliance : la sonde de santé sonde « caddy » → « liakont:8080 ») ──
mkdir -p "${SRC_DIR}/keycloak"
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
    image: busybox:1.36
    # Écrit dans le volume DÈS le démarrage (comme le VRAI Host qui crée le trousseau Data Protection
    # au bootstrap) : si APPLY démarrait le Host AVANT restore.sh, le volume serait non vide → la garde
    # anti-écrasement WORM de restore.sh refuserait la restitution → ce test ÉCHOUERAIT (garde de
    # non-régression du P1 : APPLY ne démarre que postgres+keycloak-db avant la restauration).
    command: sh -c "mkdir -p /app/App_Data/dataprotection-keys && printf bootkey > /app/App_Data/dataprotection-keys/host-boot.xml && exec httpd -f -p 8080 -h /app/App_Data"
    volumes:
      - liakont-app-data:/app/App_Data
  caddy:
    image: alpine:3.20
    command: sleep infinity
volumes:
  liakont-app-data:
YAML

# Config d'instance (la migration préserve .env/Caddyfile/realm — valeurs FICTIVES de test).
cat > "${SRC_DIR}/.env" <<'ENV'
PUBLIC_HOSTNAME=liakont.exemple.test
KEYCLOAK_HOSTNAME=id.exemple.test
POSTGRES_PASSWORD=testpw
ENV
printf 'reverse_proxy liakont:8080\n' > "${SRC_DIR}/Caddyfile"
printf '{ "realm": "liakont" }\n' > "${SRC_DIR}/keycloak/realm-liakont.json"

compose_src() { docker compose -p "${SRC_PROJECT}" -f "${COMPOSE_FILE}" "$@"; }

wait_pg() {
  local proj="$1" dir="$2" file="$3" svc="$4" user="$5" i
  for i in $(seq 1 30); do
    if docker compose -p "${proj}" --project-directory "${dir}" -f "${file}" exec -T "${svc}" pg_isready -U "${user}" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  return 1
}

# ── Source : monter + seeder (tenant ACTIF t1 + tenant SUSPENDU t2, registre, marqueurs, volume) ──
log "Démarrage de la stack source (${SRC_PROJECT})"
compose_src up -d >/dev/null
wait_pg "${SRC_PROJECT}" "${SRC_DIR}" "${COMPOSE_FILE}" postgres liakont || { echo "postgres source indisponible" >&2; exit 1; }
wait_pg "${SRC_PROJECT}" "${SRC_DIR}" "${COMPOSE_FILE}" keycloak-db keycloak || { echo "keycloak-db source indisponible" >&2; exit 1; }

log "Seed des bases et du volume (t2 = tenant SUSPENDU → doit migrer quand même)"
compose_src exec -T postgres createdb -U liakont stratum_t1
compose_src exec -T postgres createdb -U liakont stratum_t2
compose_src exec -T postgres psql -U liakont -d liakont -v ON_ERROR_STOP=1 <<'SQL'
CREATE SCHEMA IF NOT EXISTS outbox;
CREATE TABLE outbox.tenants (id text PRIMARY KEY, database_name text, is_active boolean);
INSERT INTO outbox.tenants VALUES ('t1','stratum_t1',true), ('t2','stratum_t2',false);
CREATE TABLE marker (v text);
INSERT INTO marker VALUES ('system');
SQL
compose_src exec -T postgres psql -U liakont -d stratum_t1 -c "CREATE TABLE marker (v text); INSERT INTO marker VALUES ('t1-data');" >/dev/null
compose_src exec -T postgres psql -U liakont -d stratum_t2 -c "CREATE TABLE marker (v text); INSERT INTO marker VALUES ('t2-suspendu');" >/dev/null
compose_src exec -T keycloak-db psql -U keycloak -d keycloak -c "CREATE TABLE marker (v text); INSERT INTO marker VALUES ('kc-data');" >/dev/null
dock run --rm -v "${SRC_PROJECT}_liakont-app-data":/data alpine:3.20 sh -c \
  'mkdir -p /data/archive-store/acme/2026/05/F-1 /data/dataprotection-keys; \
   printf hello > /data/archive-store/acme/2026/05/F-1/manifest.json; \
   printf dpkey > /data/dataprotection-keys/key-1.xml'

# ── EXPORT : produire le bundle de migration via migrate-instance.ps1 ──
log "EXPORT (migrate-instance.ps1)"
run_ps -InstanceName "${SRC_NAME}" -InstancesRoot "${INSTANCES_ROOT_PS}" -BundleDir "${BUNDLE_DIR_PS}" -TargetMode self-hosted
BUNDLE="$(find "${BUNDLE_DIR}" -maxdepth 1 -name "${SRC_NAME}-migration-*.zip" | sort | tail -n 1)"
[ -n "${BUNDLE}" ] || { echo "aucun bundle de migration produit" >&2; exit 1; }
log "Bundle : ${BUNDLE}"

# ── Garde anti-faux-vert : un .zip SANS manifeste de migration DOIT être refusé par APPLY ──
log "Contrôle : APPLY refuse un .zip qui n'est pas un bundle de migration"
NOTBUNDLE="${WORK}/notbundle.zip"
printf x > "${WORK}/junk.txt"
if command -v zip >/dev/null 2>&1; then
  ( cd "${WORK}" && zip -q notbundle.zip junk.txt )
else
  "${PS_EXE}" -NoProfile -Command "Compress-Archive -Path '$(to_pspath "${WORK}/junk.txt")' -DestinationPath '$(to_pspath "${NOTBUNDLE}")' -Force"
fi
if run_ps -ApplyBundle "$(to_pspath "${NOTBUNDLE}")" -TargetInstanceName "${TGT_NAME}" -InstancesRoot "${INSTANCES_ROOT_PS}" >/dev/null 2>&1; then
  fail "APPLY a accepté un .zip sans migration-manifest.json (faux vert)"
else
  log "OK : .zip non-bundle refusé"
fi

# ── APPLY : restaurer dans une CIBLE vierge + contrôle de santé ──
log "APPLY (migrate-instance.ps1 → cible ${TGT_PROJECT})"
run_ps -ApplyBundle "$(to_pspath "${BUNDLE}")" -TargetInstanceName "${TGT_NAME}" -InstancesRoot "${INSTANCES_ROOT_PS}" \
  -RegistryPath "$(to_pspath "${WORK}/instances.yaml")" -HealthTimeoutSeconds 120

# ── La cible doit être inscrite au registre (cohérence avec new-instance/update-instance) ──
if [ -f "${WORK}/instances.yaml" ] && grep -q "${TGT_NAME}" "${WORK}/instances.yaml"; then
  log "OK : cible inscrite au registre"
else
  fail "cible non inscrite au registre (${WORK}/instances.yaml)"
fi

TGT_DIR="${INSTANCES_ROOT}/${TGT_NAME}"
TGT_COMPOSE="${TGT_DIR}/docker-compose.yml"
compose_tgt() { docker compose -p "${TGT_PROJECT}" --project-directory "${TGT_DIR}" -f "${TGT_COMPOSE}" "$@"; }

# ── Assertions : bases (dont le tenant SUSPENDU) + volume ont survécu à la migration ──
assert_query() {
  local svc="$1" db="$2" user="$3" expected="$4" got
  got="$(compose_tgt exec -T "${svc}" psql -U "${user}" -d "${db}" -At -c 'SELECT v FROM marker' 2>/dev/null | tr -d '[:space:]' || true)"
  if [ "${got}" = "${expected}" ]; then log "OK : ${db}.marker = ${got}"; else fail "${db}.marker attendu='${expected}' obtenu='${got}'"; fi
}
assert_query postgres liakont liakont system
assert_query postgres stratum_t1 liakont t1-data
assert_query postgres stratum_t2 liakont t2-suspendu
assert_query keycloak-db keycloak keycloak kc-data

TENANTS="$(compose_tgt exec -T postgres psql -U liakont -d liakont -At -c 'SELECT count(*) FROM outbox.tenants' 2>/dev/null | tr -d '[:space:]' || true)"
[ "${TENANTS}" = "2" ] && log "OK : registre des tenants migré (2, dont 1 suspendu)" || fail "registre des tenants : attendu 2, obtenu '${TENANTS}'"

SUSPENDED="$(compose_tgt exec -T postgres psql -U liakont -d liakont -At -c "SELECT is_active FROM outbox.tenants WHERE id='t2'" 2>/dev/null | tr -d '[:space:]' || true)"
[ "${SUSPENDED}" = "f" ] && log "OK : état suspendu du tenant t2 préservé" || fail "état suspendu t2 : attendu 'f', obtenu '${SUSPENDED}'"

VOL_CONTENT="$(dock run --rm -v "${TGT_PROJECT}_liakont-app-data":/data:ro alpine:3.20 \
  sh -c 'cat /data/archive-store/acme/2026/05/F-1/manifest.json 2>/dev/null' || true)"
[ "${VOL_CONTENT}" = "hello" ] && log "OK : coffre WORM migré" || fail "contenu du coffre migré inattendu : '${VOL_CONTENT}'"

VOL_KEY="$(dock run --rm -v "${TGT_PROJECT}_liakont-app-data":/data:ro alpine:3.20 \
  sh -c 'cat /data/dataprotection-keys/key-1.xml 2>/dev/null' || true)"
[ "${VOL_KEY}" = "dpkey" ] && log "OK : trousseau Data Protection migré" || fail "clé DP migrée inattendue : '${VOL_KEY}'"

if [ "${FAILED}" -eq 0 ]; then
  log "RÉUSSITE : migration d'instance de bout en bout (toutes bases + coffre WORM + santé verte)."
  exit 0
else
  echo "[migrate-smoke] DES ASSERTIONS ONT ÉCHOUÉ." >&2
  exit 1
fi
