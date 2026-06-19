-- Inbox DURABLE des webhooks de signature (ADR-0029 §4 ; INV-YOUSIGN-4/5). L'événement AUTHENTIFIÉ (HMAC
-- vérifié) est persisté tenant-scopé AVANT la réponse 2xx ; le drain (TenantJobRunner) le traite ensuite en
-- asynchrone (download preuve + rapatriement WORM). processed_at NULL = pas encore drainé → un crash après
-- 2xx ne perd pas l'événement (INV-YOUSIGN-4). raw_body = octets EXACTS reçus (audit / retraitement).
--
-- IDEMPOTENCE (INV-YOUSIGN-5) : clé UNIQUE (company_id, provider_type, event_id) — JAMAIS event_id seul (deux
-- tenants/providers peuvent partager un identifiant). Un rejeu du MÊME événement est rejeté par l'unicité.
CREATE TABLE IF NOT EXISTS signature.signature_webhook_inbox (
    id                 uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id         uuid        NOT NULL,
    provider_type      text        NOT NULL,
    event_id           text        NOT NULL,
    provider_reference text        NOT NULL,
    raw_body           bytea       NOT NULL,
    received_at        timestamptz NOT NULL DEFAULT now(),
    processed_at       timestamptz,
    attempt_count      int         NOT NULL DEFAULT 0,
    last_error         text,

    CONSTRAINT pk_signature_webhook_inbox PRIMARY KEY (id),
    CONSTRAINT uq_signature_webhook_inbox_idempotency UNIQUE (company_id, provider_type, event_id)
);

-- Drain : les entrées non traitées du tenant, les plus anciennes d'abord.
CREATE INDEX IF NOT EXISTS ix_signature_webhook_inbox_pending
    ON signature.signature_webhook_inbox (company_id, received_at)
    WHERE processed_at IS NULL;
