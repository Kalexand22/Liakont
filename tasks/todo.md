# SIG07 — Plug-in Yousign (signature à distance) — ADR-0029 / F17 §5

Session orchestration : `orch-20260616-225623-slot2`, slot-2, branche `feat/signature-SIG07`.
Deps : SIG03 (abstraction Signature) + SIG04 (module DocumentApproval) — tous deux mergés sur feat/signature.
SIG05/SIG06 PAS sur cette branche au démarrage → SIG07 self-contained (pas de transition DocumentApproval ;
c'est le territoire de SIG06). Scope = plug-in + ingestion webhook + drain WORM.

## Architecture (frontières P1)

- Plug-in `Liakont.SignatureProviders.Yousign` : ne référence QUE `Signature.Contracts`. HMAC interne
  (jamais WebhookSignature.Compute vendored). Allowlist anti-SSRF (origines https exactes, AllowAutoRedirect=false).
  Capacités déclarées (Remote, Webhook|Polling, SES ; QES → NotSupported). Clé API optionnelle (inbound = secret webhook seul).
- Persistance Signature (schéma `signature`, Dapper) : comptes chiffrés par tenant, inbox webhook durable
  (idempotence (company_id, provider_type, event_id) + plafond de drain), liaison demande→document,
  catalogue système de routage par handle opaque.
- Host : endpoint `POST /webhooks/signature/{providerType}/{opaqueRef}` (routage handle opaque → scope tenant
  → HMAC → inbox avant 2xx), resolver de secrets (DataProtection), rate-limit, drain WORM (ITenantJob fan-out)
  via `Archive.Contracts` (jamais Archive.Domain).
- Contrat additif : `SignatureWebhookResult.EventId` (clé d'idempotence).

## Plan — TOUT LIVRÉ

- [x] D1. Plug-in Yousign (provider, factory, capabilities, allowlist SSRF, HMAC interne, retry 429, wire, registration)
- [x] D2. Contracts additif : EventId sur SignatureWebhookResult
- [x] D3. Persistance Signature : domaine + ports + impls Dapper + migrations V001-V005 + registration
- [x] D4. Host : endpoint webhook + resolver + route catalog lookup + DI + rate-limit
- [x] D5. Drain : service de drain (inbox→download proof→WORM via Archive.Contracts) + ITenantJob + fan-out + trigger
- [x] D6. Tests unitaires : capacités/QES NotSupported, HMAC falsifié rejeté, allowlist SSRF, drain WORM, frontières NetArchTest
- [x] D7. Tests d'intégration : ingestion routée+vérifiée+durable, tenant-scoping ≥2 bases, idempotence, crash-après-2xx
- [x] D8. Solution + AppBootstrap : projets ajoutés, AddYousignSignatureProvider + endpoint + drain câblés
- [x] D9. verify-fast PASS ; run-tests PASS (6124) ; codex-review propre (round 1 : 3 P2 corrigés ; round 2 : 1 P2 accepté)

## Review

- **verify-fast** : PASS (2 solutions + analyzers + tests unitaires).
- **run-tests** : PASS (6124 tests, 0 échec, Testcontainers).
- **codex-review round 1** : 0 P1, 3 P2 — tous corrigés (filtre événement `.done`, plafond de drain anti head-of-line, clé API optionnelle pour l'inbound).
- **codex-review round 2** : 0 P1, 1 P2 — ACCEPTÉ avec justification : raffinement conditionnel (sémantique Yousign non vérifiable), aucune perte de données (dead-letter inspectable + re-traitable), 10 cycles = budget généreux ; correctif propre (état typé RetryableUnavailable) = extension du contrat SIG03, mieux informé par la recette GATE_SIGNATURE.
- Merge-back : conflits triviaux résolus (AppBootstrap : DocumentApproval déplacé par SIG05 avant Mandats → doublon retiré ; todo.md).
