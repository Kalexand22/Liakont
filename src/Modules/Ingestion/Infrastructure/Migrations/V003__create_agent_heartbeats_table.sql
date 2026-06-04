-- Historique APPEND-ONLY des heartbeats (F12 §4.2, §5). Conservé 90 jours pour la supervision
-- (dead-man's switch) — ce n'est PAS une piste d'audit légale : la purge par rétention est légitime
-- (assurée hors de ce module). Vit dans la base SYSTÈME (supervision cross-tenant en lecture seule).
CREATE TABLE IF NOT EXISTS ingestion.agent_heartbeats (
    id                       uuid        NOT NULL DEFAULT gen_random_uuid(),
    agent_id                 uuid        NOT NULL,
    tenant_id                text        NOT NULL,
    contract_version         text        NOT NULL,
    agent_version            text        NOT NULL,
    sent_at_utc              timestamptz NOT NULL,
    last_successful_sync_utc timestamptz,
    received_at_utc          timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_agent_heartbeats PRIMARY KEY (id),
    CONSTRAINT fk_agent_heartbeats_agent FOREIGN KEY (agent_id) REFERENCES ingestion.agents (id)
);

CREATE INDEX ix_agent_heartbeats_agent_received ON ingestion.agent_heartbeats (agent_id, received_at_utc DESC);
