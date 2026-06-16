#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# deploy/provisioning/test-decommission-tenant.sh — Auto-test de la FIN DE VIE d'un tenant (OPS06c, D7).
#
# Exerce le VRAI decommission-tenant.ps1 contre une stack RÉDUITE (postgres seul) montée à la volée :
#   seed   : base système « liakont » (outbox.tenants avec le tenant t1 ACTIF → base « stratum_t1 ») +
#            base tenant « stratum_t1 » (tenantsettings.tenant_profiles statut=0 + un marqueur).
#   1) DÉSACTIVATION (sans -Delete) : assert is_active=false, statut=1 (Suspendu), base TOUJOURS présente.
#   2) SUPPRESSION refusée sur export ALTÉRÉ (-VerifiedExportPath tampered) : assert base TOUJOURS présente
#      (« bloquer plutôt qu'envoyer faux » — pas de suppression sans archive intègre).
#   3) SUPPRESSION (export VERT) : assert base « stratum_t1 » SUPPRIMÉE + tenant retiré de outbox.tenants +
#      AUDIT D'INSTANCE écrit ET SURVIVANT à la suppression de la base (qui, quand, référence d'export).
#
# La vérification d'intégrité de l'export est réalisée par le VRAI tools/verifier-integrite-archive.ps1
# (appelé par decommission-tenant.ps1) ; la santé du Host (OIDC, middleware de suspension) est revalidée à
# la recette humaine GATE_TOOLKIT, comme l'appliance (OPS01a) et la migration (OPS06b).
#
# Prérequis : docker (compose) + pwsh|powershell. Sortie 0 = OK ; toute assertion en échec = non nul.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DECOMMISSION_PS1="${SCRIPT_DIR}/decommission-tenant.ps1"
WORK="$(mktemp -d)"
NAME="eol-$$"
PROJECT="liakont-${NAME}"
INSTANCES_ROOT="${WORK}/instances"
INST_DIR="${INSTANCES_ROOT}/${NAME}"
COMPOSE_FILE="${INST_DIR}/docker-compose.yml"
AUDIT_FILE="${INST_DIR}/tenant-decommission-audit.jsonl"
FAILED=0

log()  { printf '[eol-smoke] %s\n' "$*"; }
fail() { printf '[eol-smoke] ÉCHEC : %s\n' "$*" >&2; FAILED=1; }

cleanup() {
  docker compose -p "${PROJECT}" --project-directory "${INST_DIR}" -f "${COMPOSE_FILE}" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "${WORK}"
}
trap cleanup EXIT

command -v docker >/dev/null 2>&1 || { echo "docker requis pour l'auto-test" >&2; exit 2; }

PS_EXE=""
if command -v pwsh >/dev/null 2>&1; then PS_EXE="pwsh"; elif command -v powershell >/dev/null 2>&1; then PS_EXE="powershell"; fi
[ -n "${PS_EXE}" ] || { echo "pwsh|powershell requis pour l'auto-test (decommission-tenant.ps1)" >&2; exit 2; }

# Convertit un chemin pour l'exécutable PowerShell (Git Bash sur Windows : « /c/… » → chemin Windows).
to_pspath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
DECOMMISSION_PS1_PS="$(to_pspath "${DECOMMISSION_PS1}")"
INSTANCES_ROOT_PS="$(to_pspath "${INSTANCES_ROOT}")"

run_ps() { "${PS_EXE}" -NoProfile -ExecutionPolicy Bypass -File "${DECOMMISSION_PS1_PS}" "$@"; }
compose() { docker compose -p "${PROJECT}" --project-directory "${INST_DIR}" -f "${COMPOSE_FILE}" "$@"; }

wait_pg() {
  local i
  for i in $(seq 1 30); do
    if compose exec -T postgres pg_isready -U liakont >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

# ── Stack réduite (postgres seul ; le service est nommé « postgres » comme l'appliance) ──
mkdir -p "${INST_DIR}"
cat > "${COMPOSE_FILE}" <<'YAML'
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: liakont
      POSTGRES_PASSWORD: testpw
      POSTGRES_DB: liakont
YAML

log "Démarrage de la stack (${PROJECT})"
compose up -d >/dev/null
wait_pg || { echo "postgres indisponible" >&2; exit 1; }

log "Seed : tenant t1 ACTIF (base stratum_t1) + profil (statut=0) + marqueur"
compose exec -T postgres createdb -U liakont stratum_t1
compose exec -T postgres psql -U liakont -d liakont -v ON_ERROR_STOP=1 <<'SQL'
CREATE SCHEMA IF NOT EXISTS outbox;
CREATE TABLE outbox.tenants (
  id text PRIMARY KEY, display_name text, admin_email text, database_name text,
  realm_name text, is_active boolean, provisioned_at timestamptz DEFAULT now(), company_id uuid);
INSERT INTO outbox.tenants (id, display_name, database_name, is_active, company_id)
  VALUES ('t1','ACME SAS','stratum_t1', true, '11111111-1111-1111-1111-111111111111');
SQL
compose exec -T postgres psql -U liakont -d stratum_t1 -v ON_ERROR_STOP=1 <<'SQL'
CREATE SCHEMA IF NOT EXISTS tenantsettings;
CREATE TABLE tenantsettings.tenant_profiles (company_id uuid, statut int NOT NULL DEFAULT 0);
INSERT INTO tenantsettings.tenant_profiles (company_id, statut)
  VALUES ('11111111-1111-1111-1111-111111111111', 0);
CREATE TABLE marker (v text); INSERT INTO marker VALUES ('t1-data');
SQL

q() { compose exec -T postgres psql -U liakont -d "$1" -At -c "$2" 2>/dev/null | tr -d '[:space:]' || true; }
db_exists() { compose exec -T postgres psql -U liakont -d liakont -At -c "SELECT 1 FROM pg_database WHERE datname='$1'" 2>/dev/null | tr -d '[:space:]' || true; }

# ── Fixtures d'export (sur l'hôte) ──
mkdir -p "${WORK}/export-ok/archive"                 # coffre vide → VERDICT=EMPTY (vert)
mkdir -p "${WORK}/export-bad/archive/pkg"
printf x > "${WORK}/export-bad/archive/pkg/piece.txt"
HX="$(printf 'de%.0s' $(seq 1 32))"                  # 64 hex
ZERO="$(printf '0%.0s' $(seq 1 64))"                 # 64 zéros (empreinte FAUSSE → TAMPERED)
printf '{ "entryKind":"package","packageHash":"%s","chainHash":"%s","files":[{"name":"piece.txt","sha256":"%s"}] }\n' \
  "${HX}" "${HX}" "${ZERO}" > "${WORK}/export-bad/archive/pkg/manifest.json"

# ── 1) DÉSACTIVATION (sans -Delete) ──
log "1) Désactivation logique (sans -Delete)"
run_ps -InstanceName "${NAME}" -Tenant t1 -InstancesRoot "${INSTANCES_ROOT_PS}"
[ "$(q liakont "SELECT is_active FROM outbox.tenants WHERE id='t1'")" = "f" ] \
  && log "OK : is_active=false" || fail "désactivation : is_active attendu 'f'"
[ "$(q stratum_t1 'SELECT statut FROM tenantsettings.tenant_profiles')" = "1" ] \
  && log "OK : statut live=1 (Suspendu)" || fail "statut live attendu '1'"
[ "$(db_exists stratum_t1)" = "1" ] && log "OK : base tenant intacte après désactivation" || fail "base supprimée à tort par la désactivation"

# ── 2) SUPPRESSION refusée sur export ALTÉRÉ ──
log "2) Suppression sur export ALTÉRÉ (doit être REFUSÉE)"
if run_ps -InstanceName "${NAME}" -Tenant t1 -Delete -VerifiedExportPath "$(to_pspath "${WORK}/export-bad")" \
     -InstancesRoot "${INSTANCES_ROOT_PS}" -Operator ops@itinnov.test -Yes -ConfirmTenantName t1 >/dev/null 2>&1; then
  fail "suppression ACCEPTÉE sur export altéré (faux vert)"
else
  log "OK : suppression refusée sur export altéré"
fi
[ "$(db_exists stratum_t1)" = "1" ] && log "OK : base tenant TOUJOURS présente après refus" || fail "base supprimée malgré le refus"
[ ! -f "${AUDIT_FILE}" ] && log "OK : aucun audit écrit sur un refus" || fail "audit écrit alors que la suppression a été refusée"

# ── 3) SUPPRESSION (export VERT) ──
log "3) Suppression complète (export VERT)"
run_ps -InstanceName "${NAME}" -Tenant t1 -Delete -VerifiedExportPath "$(to_pspath "${WORK}/export-ok")" \
  -InstancesRoot "${INSTANCES_ROOT_PS}" -Operator ops@itinnov.test -Recipient dpo@acme.test -Yes -ConfirmTenantName t1

[ "$(db_exists stratum_t1)" = "" ] && log "OK : base tenant SUPPRIMÉE" || fail "base tenant non supprimée"
[ "$(q liakont "SELECT count(*) FROM outbox.tenants WHERE id='t1'")" = "0" ] \
  && log "OK : tenant retiré du catalogue" || fail "tenant toujours dans outbox.tenants"

# ── AUDIT D'INSTANCE : présent ET survivant à la suppression de la base ──
if [ -f "${AUDIT_FILE}" ]; then
  log "OK : fichier d'audit d'instance présent (survit à la base supprimée)"
  grep -q '"event":"tenant-decommissioned"' "${AUDIT_FILE}" && log "OK : événement de suppression journalisé" || fail "événement de suppression absent de l'audit"
  grep -q '"operator":"ops@itinnov.test"' "${AUDIT_FILE}" && log "OK : opérateur consigné (qui)" || fail "opérateur absent de l'audit"
  grep -q '"recipient":"dpo@acme.test"' "${AUDIT_FILE}" && log "OK : destinataire consigné" || fail "destinataire absent de l'audit"
  grep -q '"tenant_id":"t1"' "${AUDIT_FILE}" && log "OK : tenant consigné" || fail "tenant absent de l'audit"
  grep -q '"export_verdict":"EMPTY"' "${AUDIT_FILE}" && log "OK : référence d'export vérifié consignée" || fail "verdict d'export absent de l'audit"
else
  fail "fichier d'audit d'instance ABSENT après la suppression (preuve de fin de conservation perdue)"
fi

if [ "${FAILED}" -eq 0 ]; then
  log "RÉUSSITE : fin de vie de tenant de bout en bout (désactivation + refus sur altéré + suppression + audit survivant)."
  exit 0
else
  echo "[eol-smoke] DES ASSERTIONS ONT ÉCHOUÉ." >&2
  exit 1
fi
