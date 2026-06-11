# ─────────────────────────────────────────────────────────────────────────────
# deploy/docker/_backup-common.sh — fonctions partagées par backup.sh / restore.sh (OPS01b).
# Fichier SOURCÉ (pas exécuté). Aucune action au chargement hormis la résolution des défauts.
# ─────────────────────────────────────────────────────────────────────────────

# Emplacement de la lib (indépendant du script appelant) → défaut du fichier compose de l'appliance.
_BK_COMMON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

LIAKONT_PROJECT="${LIAKONT_PROJECT:-liakont-appliance}"
LIAKONT_COMPOSE_FILE="${LIAKONT_COMPOSE_FILE:-${_BK_COMMON_DIR}/appliance/docker-compose.yml}"
LIAKONT_DB_SERVICE="${LIAKONT_DB_SERVICE:-postgres}"
LIAKONT_DB_USER="${LIAKONT_DB_USER:-liakont}"
LIAKONT_SYSTEM_DB="${LIAKONT_SYSTEM_DB:-liakont}"
LIAKONT_KC_DB_SERVICE="${LIAKONT_KC_DB_SERVICE:-keycloak-db}"
LIAKONT_KC_DB_USER="${LIAKONT_KC_DB_USER:-keycloak}"
LIAKONT_KC_DB="${LIAKONT_KC_DB:-keycloak}"
LIAKONT_APP_VOLUME="${LIAKONT_APP_VOLUME:-${LIAKONT_PROJECT}_liakont-app-data}"
# Image utilitaire pour tar/copier les volumes (aucune dépendance hôte hormis docker).
LIAKONT_TAR_IMAGE="${LIAKONT_TAR_IMAGE:-alpine:3.20}"

bk_log()  { printf '[backup] %s\n' "$*" >&2; }
bk_warn() { printf '[backup] AVERTISSEMENT : %s\n' "$*" >&2; }
bk_die()  { printf '[backup] ERREUR : %s\n' "$*" >&2; exit 1; }

bk_require_cmds() {
  local c
  for c in "$@"; do
    command -v "${c}" >/dev/null 2>&1 || bk_die "Commande requise introuvable : ${c}"
  done
}

# docker compose ciblant le projet + le fichier de l'appliance.
bk_compose() {
  docker compose -p "${LIAKONT_PROJECT}" -f "${LIAKONT_COMPOSE_FILE}" "$@"
}

# docker run/cmd avec la conversion de chemins de Git-Bash NEUTRALISÉE : sans cela, sous Windows, les
# chemins INTERNES au conteneur (/data, /backup) sont réécrits en chemins Windows. Variables ignorées
# (no-op) sur un hôte Linux — l'environnement de production de l'appliance.
bk_docker() {
  MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' docker "$@"
}

# Vrai si le service compose a au moins un conteneur en cours d'exécution.
bk_service_running() {
  local svc="$1" id
  id="$(bk_compose ps -q "${svc}" 2>/dev/null || true)"
  [ -n "${id}" ]
}

bk_sha256() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    openssl dgst -sha256 "$1" | awk '{print $NF}'
  fi
}

bk_filesize() {
  wc -c < "$1" | tr -d '[:space:]'
}

# Liste des bases applicatives à sauvegarder : énumérées par EXISTENCE sur le cluster (pg_database),
# PAS par l'état métier. Sauvegarder toute base présente (système + tenants, y compris un tenant
# SUSPENDU dont la base survit — archives fiscales soumises à la rétention 10 ans) : filtrer sur
# outbox.tenants.is_active = true exclurait silencieusement ces bases (faux vert). Templates et base
# de maintenance « postgres » exclues. Un échec d'énumération ABANDONNE (jamais de sauvegarde partielle).
bk_list_app_databases() {
  local out
  if ! out="$(bk_compose exec -T "${LIAKONT_DB_SERVICE}" \
      psql -U "${LIAKONT_DB_USER}" -d "${LIAKONT_SYSTEM_DB}" -At \
      -c "SELECT datname FROM pg_database WHERE datistemplate = false AND datname <> 'postgres' ORDER BY datname" 2>&1)"; then
    bk_die "Énumération des bases du cluster en échec (omission silencieuse = faux vert) : ${out}"
  fi
  printf '%s\n' "${out}" | sed '/^[[:space:]]*$/d'
}

# pg_dump custom format (-Fc) d'UNE base via le service compose, écrit sur l'hôte.
bk_dump_database() {
  local svc="$1" user="$2" db="$3" out="$4"
  bk_log "Dump base ${db} (service ${svc}) → $(basename "${out}")"
  bk_compose exec -T "${svc}" pg_dump -U "${user}" -Fc -d "${db}" > "${out}"
}

# tar gzip d'un volume nommé via un conteneur jetable (inclut les fichiers cachés). L'archive est
# STREAMÉE sur stdout puis redirigée sur l'hôte (comme pg_dump) : aucun montage de dossier hôte, donc
# aucun aléa de traduction de chemins (Docker Desktop/Windows) — robuste sur Linux comme sur Git-Bash.
# GARDE anti-faux-vert : « docker run -v <nom>:/data » CRÉE un volume vide si <nom> n'existe pas (projet
# mal nommé) → on sauvegarderait un coffre VIDE en sortant 0. On exige donc que le volume EXISTE.
bk_archive_volume() {
  local vol="$1" out="$2"
  if ! bk_docker volume inspect "${vol}" >/dev/null 2>&1; then
    bk_die "Volume introuvable : ${vol}. Le projet/volume ne correspond pas à l'appliance — refus de produire une sauvegarde vide. Préciser LIAKONT_PROJECT / LIAKONT_APP_VOLUME."
  fi
  bk_log "Archivage du volume ${vol} → $(basename "${out}")"
  bk_docker run --rm -v "${vol}":/data:ro "${LIAKONT_TAR_IMAGE}" tar czf - -C /data . > "${out}"
}

# Vrai si le volume nommé est vide (aucune entrée) — garde anti-écrasement du coffre WORM au restore.
# Échoue FERMÉ : un volume inexistant = cible vierge (vide légitime) ; mais une SONDE en échec (Docker
# indispo) ne doit JAMAIS être interprétée comme « vide » (sinon la garde autoriserait l'écrasement).
bk_volume_is_empty() {
  local vol="$1" out
  if ! bk_docker volume inspect "${vol}" >/dev/null 2>&1; then
    return 0
  fi
  if out="$(bk_docker run --rm -v "${vol}":/data:ro "${LIAKONT_TAR_IMAGE}" \
      sh -c 'find /data -mindepth 1 -maxdepth 1 | head -n 1')"; then
    [ -z "${out}" ]
  else
    bk_die "Sonde du volume ${vol} en échec : la garde anti-écrasement échoue FERMÉE (restauration interrompue)."
  fi
}

# Empreintes SHA-256 vérifiables par sha256sum -c (format « <hash>  <fichier> », chemins relatifs).
bk_write_sha256sums() {
  local dest="$1" f
  : > "${dest}/SHA256SUMS"
  for f in "${dest}"/*.dump "${dest}"/*.tar.gz; do
    [ -e "${f}" ] || continue
    printf '%s  %s\n' "$(bk_sha256 "${f}")" "$(basename "${f}")" >> "${dest}/SHA256SUMS"
  done
}

# Manifeste informatif (l'intégrité machine est portée par SHA256SUMS).
bk_write_manifest() {
  local dest="$1" stamp="$2"; shift 2
  local dbs=("$@") manifest="${dest}/manifest.json" f first=1
  bk_write_sha256sums "${dest}"
  {
    printf '{\n'
    printf '  "created_at": "%s",\n' "${stamp}"
    printf '  "project": "%s",\n' "${LIAKONT_PROJECT}"
    printf '  "system_db": "%s",\n' "${LIAKONT_SYSTEM_DB}"
    printf '  "databases": [%s],\n' "$(bk_json_array "${dbs[@]}")"
    printf '  "files": [\n'
    for f in "${dest}"/*.dump "${dest}"/*.tar.gz; do
      [ -e "${f}" ] || continue
      [ "${first}" -eq 1 ] || printf ',\n'
      first=0
      printf '    { "name": "%s", "sha256": "%s", "bytes": %s }' \
        "$(basename "${f}")" "$(bk_sha256 "${f}")" "$(bk_filesize "${f}")"
    done
    printf '\n  ]\n}\n'
  } > "${manifest}"
}

# Construit un tableau JSON de chaînes : "a","b","c"
bk_json_array() {
  local first=1 item out=""
  for item in "$@"; do
    [ "${first}" -eq 1 ] || out+=", "
    first=0
    out+="\"${item}\""
  done
  printf '%s' "${out}"
}

# Rotation : conserver les N dossiers de sauvegarde horodatés les plus récents.
bk_rotate() {
  local dir="$1" keep="$2" d i=0
  while IFS= read -r d; do
    i=$((i + 1))
    if [ "${i}" -gt "${keep}" ]; then
      bk_log "Rotation : suppression de $(basename "${d}")"
      rm -rf "${d}"
    fi
  done < <(find "${dir}" -mindepth 1 -maxdepth 1 -type d -name '*Z' | sort -r)
}

# Vérifie l'intégrité d'une sauvegarde par SHA256SUMS (refuse de restaurer un artefact altéré).
bk_verify_sha256sums() {
  local dest="$1"
  [ -f "${dest}/SHA256SUMS" ] || bk_die "SHA256SUMS absent de ${dest} : sauvegarde incomplète, restauration refusée."
  bk_log "Vérification des empreintes SHA-256 de ${dest}"
  (
    cd "${dest}"
    if command -v sha256sum >/dev/null 2>&1; then
      sha256sum -c SHA256SUMS
    elif command -v shasum >/dev/null 2>&1; then
      shasum -a 256 -c SHA256SUMS
    else
      bk_die "Ni sha256sum ni shasum disponibles : impossible de vérifier l'intégrité."
    fi
  ) || bk_die "Empreinte SHA-256 invalide : la sauvegarde est altérée, restauration refusée."
}

# Crée la base si absente puis pg_restore (cible vierge attendue ; --clean --if-exists pour rejouabilité).
bk_restore_database() {
  local svc="$1" user="$2" db="$3" dump="$4"
  bk_log "Restauration base ${db} (service ${svc}) depuis $(basename "${dump}")"
  bk_compose exec -T "${svc}" createdb -U "${user}" "${db}" 2>/dev/null || true
  # --exit-on-error : un échec SQL interrompt et propage un code non nul (pas de restauration partielle « réussie »).
  bk_compose exec -T "${svc}" pg_restore -U "${user}" --clean --if-exists --exit-on-error -d "${db}" < "${dump}"
}

# Restaure un volume depuis un tar.gz. Garde : refuse d'écraser un volume non vide sauf force=1.
bk_restore_volume() {
  local vol="$1" tarfile="$2" appsvc="$3" force="${4:-0}"

  if ! bk_volume_is_empty "${vol}" && [ "${force}" != "1" ]; then
    bk_die "Le volume ${vol} n'est pas vide (coffre WORM) : restauration refusée. Utiliser -f pour forcer (migration/PRA sur cible neuve uniquement)."
  fi

  # Arrêt du Host pendant la restitution du volume (cohérence du coffre), si le service existe.
  if bk_service_running "${appsvc}"; then
    bk_log "Arrêt du service ${appsvc} pendant la restitution du volume"
    bk_compose stop "${appsvc}" >/dev/null
  fi

  bk_log "Restitution du volume ${vol} depuis $(basename "${tarfile}")"
  # Archive lue sur stdin (redirigée depuis l'hôte) : pas de montage de dossier hôte (cf. bk_archive_volume).
  bk_docker run --rm -i -v "${vol}":/data "${LIAKONT_TAR_IMAGE}" \
    sh -c 'find /data -mindepth 1 -maxdepth 1 -exec rm -rf {} + ; tar xzf - -C /data' < "${tarfile}"

  if bk_service_running "${appsvc}" 2>/dev/null || bk_compose ps --all -q "${appsvc}" >/dev/null 2>&1; then
    bk_log "Redémarrage du service ${appsvc}"
    bk_compose start "${appsvc}" >/dev/null 2>&1 || true
  fi
}
