# État d'orchestration — NE PAS UTILISER CE RÉPERTOIRE

L'état runtime de l'orchestration ne vit **pas** ici.

Il vit dans le dépôt d'orchestration séparé pointé par la variable d'environnement
`$ORCH_REPO` (définie dans `.claude/settings.json`) :

- État des items : `$ORCH_REPO/state.yaml`
- Journal d'événements : `$ORCH_REPO/events.jsonl`
- Leases de slots : `$ORCH_REPO/leases/`
- Logs de sessions : `$ORCH_REPO/session-log/`

Ce répertoire existe uniquement pour des raisons historiques de structure.
Toutes les mutations d'état passent par `tools/orch-state.ps1`.
