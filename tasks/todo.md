# SIG07 — Plug-in Yousign (signature à distance) — ADR-0029 / F17 §5

Session orchestration : `orch-20260616-225623-slot2`, slot-2, branche `feat/signature-SIG07`.
Deps : SIG03 (abstraction Signature) + SIG04 (module DocumentApproval) — tous deux mergés sur feat/signature.
SIG05/SIG06 PAS sur cette branche → SIG07 reste self-contained (pas de transition DocumentApproval ;
c'est le territoire de SIG06). Scope = plug-in + ingestion webhook + drain WORM.

## Architecture (frontières P1)

- **Plug-in** `src/SignatureProviders/Liakont.SignatureProviders.Yousign` : ne référence QUE
  `Signature.Contracts` (+ Microsoft.Extensions.Http/DI). HMAC interne (System.Security.Cryptography),
  jamais `WebhookSignature.Compute` vendored. Allowlist anti-SSRF (origines https exactes par Environment,
  AllowAutoRedirect=false). Capacités DÉCLARÉES (Mode=Remote, Webhook|Polling, SupportedLevels=SES ;
  QES → NotSupported).
- **Persistance Signature** (`Signature.Infrastructure`, Dapper, schéma `signature`) :
  - `signature_provider_accounts` (secrets chiffrés par tenant — patron PaAccount/DataProtectionSecretProtector)
  - `signature_webhook_inbox` (durable, tenant-scopée, idempotence UNIQUE (company_id, provider_type, event_id))
  - `signature_webhook_routes` (catalogue SYSTÈME opaque_ref→tenant, UNIQUE, interrogé sur la base système)
  - `signature_requests` (linkage provider_reference→document pour le rapatriement WORM)
- **Host** : endpoint `POST /webhooks/signature/{providerType}/{opaqueRef}` (raw body, route→scope→HMAC→inbox
  AVANT 2xx) ; `YousignAccountResolver` (déchiffre via ISecretProtector) ; drain `ITenantJob` +
  fan-out `IJobHandler` + trigger ; rapatriement WORM via `Archive.Contracts` (jamais Archive.Domain).
- **Contracts additif** : `SignatureWebhookResult.EventId` (clé d'idempotence) — additif, sûr.

## Plan

- [ ] D1. Plug-in Yousign (provider, factory, capabilities, allowlist SSRF, HMAC interne, retry 429, wire, registration)
- [ ] D2. Contracts additif : EventId sur SignatureWebhookResult
- [ ] D3. Persistance Signature : domaine + ports + impls Dapper + migrations V001-V005 + registration
- [ ] D4. Host : endpoint webhook + resolver + route catalog lookup + DI
- [ ] D5. Drain : service de drain (inbox→download proof→WORM via Archive.Contracts) + ITenantJob + fan-out + trigger
- [ ] D6. Tests unitaires : capacités/NotSupported, HMAC falsifié rejeté, allowlist SSRF, drain WORM, frontières NetArchTest
- [ ] D7. Tests d'intégration : ingestion webhook routée+vérifiée+durable, tenant-scoping ≥2 bases, idempotence, crash-après-2xx
- [ ] D8. Solution + AppBootstrap : ajouter projets, câbler AddYousignSignatureProvider + endpoint + drain
- [ ] D9. verify-fast + run-tests + codex-review

## Review
(à compléter)
