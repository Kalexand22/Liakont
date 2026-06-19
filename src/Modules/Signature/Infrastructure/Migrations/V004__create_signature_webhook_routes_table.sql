-- Catalogue SYSTÈME de routage des webhooks par handle de tenant OPAQUE (ADR-0029 §2 ; INV-YOUSIGN-3).
-- Modèle outbox.tenants / ICompanyTenantLookup : pur aiguillage d'infra {opaque_ref → tenant}, AUCUNE donnée
-- métier, interrogé sur la base SYSTÈME AVANT toute ouverture de scope tenant (jamais un lookup cross-tenant
-- pré-scope). La table est définie dans le schéma signature (créé sur la base système comme sur chaque base
-- tenant) ; seules les LIGNES sur la base système sont peuplées/lues. L'opaque_ref n'est PAS un secret (le
-- HMAC reste exigé) mais il est non devinable et UNIQUE (un handle ne route que vers un seul tenant).
CREATE TABLE IF NOT EXISTS signature.signature_webhook_routes (
    opaque_ref    text        NOT NULL,
    tenant_id     text        NOT NULL,
    company_id    uuid        NOT NULL,
    provider_type text        NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_signature_webhook_routes PRIMARY KEY (opaque_ref)
);
