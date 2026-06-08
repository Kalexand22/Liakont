# API01c — Endpoint de lecture : Paramétrage du tenant (GET /api/v1/settings)

Session orchestration : `orch-20260608-094713-s1` · slot-1 · sous-branche `feat/console-web-API01c`.

## Périmètre (v15 — capacités agent RETIRÉES → API01d gelé)
`GET /api/v1/settings` (permission `liakont.read`, tenant-scopé) exposant :
- paramétrage tenant visible (profil + fiscal + comptes PA, secrets TOUJOURS masqués → `HasApiKey` seul) ;
- table TVA (version, validateur, état de validation) — résumé, pas les règles ;
- capacités du/des PA configurés (`PaCapabilities` via `IPaClientRegistry`).
AUCUNE capacité agent/adaptateur (reportée à API01d).

## Décision d'architecture
Pattern maison (précédent Archive/API03) : `.Web` ne référence QUE ses propres Contracts ; la
composition cross-module se fait en **Infrastructure**, exposée par un service de ses propres Contracts.
Aucun Contracts de module ne référence un autre Contracts de module → l'overview utilise des
**projections locales** (TvaMappingSummaryDto, PaCapabilitiesSummaryDto) peuplées par le service.

## Plan
- [ ] Contracts : `ITenantSettingsConsoleQueries` + DTOs overview (overview / tva summary / pa+caps / caps summary)
- [ ] Infrastructure : `TenantSettingsConsoleQueries` (compose TenantSettings + TvaMapping + Transmission via Contracts + ITenantContext), garde `IsRegistered` (robustesse type PA non chargé), DI
- [ ] Infrastructure csproj : + TvaMapping.Contracts + Transmission.Contracts
- [ ] Web : nouveau projet `Liakont.Modules.TenantSettings.Web` + `MapTenantSettingsEndpoints` (GET /settings)
- [ ] Host : référence projet + `v1.MapTenantSettingsEndpoints()`
- [ ] Solution : ajouter le projet Web
- [ ] Tests d'intégration : seed (profil/fiscal/pa_accounts/TVA) + enregistrer le plug-in Fake dans le harness ; 401/403/200, secrets masqués, état TVA, capacités PA, type PA non enregistré (PluginAvailable=false), isolation tenant
- [ ] Tests unitaires : projection capacités + garde IsRegistered + overview vide (companyId null)
- [ ] Docs module : INVARIANTS.md / SCENARIOS.md
- [ ] verify-fast + run-tests + codex-review propres

## Review
(à compléter)
