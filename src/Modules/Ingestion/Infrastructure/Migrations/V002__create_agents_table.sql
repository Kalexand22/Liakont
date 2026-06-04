-- Registre des agents (F12 §3.2, §4.2). Vit dans la base SYSTÈME (partagée) : c'est ce qui permet
-- de résoudre une clé API vers son tenant AVANT d'ouvrir la base du tenant (l'authentification
-- précède tout contexte tenant). tenant_id = slug du tenant propriétaire (route vers sa base).
-- key_hash = empreinte SHA-256 de la clé complète ; le clair n'est JAMAIS stocké (CLAUDE.md n°10).
CREATE TABLE IF NOT EXISTS ingestion.agents (
    id                 uuid        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id          text        NOT NULL,
    name               text        NOT NULL,
    key_prefix         text        NOT NULL,
    key_hash           text        NOT NULL,
    is_revoked         boolean     NOT NULL DEFAULT false,
    created_at         timestamptz NOT NULL DEFAULT now(),
    revoked_at         timestamptz,
    last_seen_at       timestamptz,
    last_agent_version text,

    CONSTRAINT pk_agents PRIMARY KEY (id)
);

-- Le préfixe identifie la clé pour la résolution : unique sur toute l'instance (résolution cross-tenant).
CREATE UNIQUE INDEX uq_agents_key_prefix ON ingestion.agents (key_prefix);

CREATE INDEX ix_agents_tenant_id ON ingestion.agents (tenant_id);
