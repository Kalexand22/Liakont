-- UIM02: Agent profiles — extends users with agent-specific metadata
CREATE TABLE IF NOT EXISTS identity.agent_profiles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    service_code TEXT,
    title       TEXT,
    phone       TEXT,
    office_location TEXT,
    hire_date   DATE,
    notes       TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ,
    CONSTRAINT uq_agent_profiles_user_id UNIQUE (user_id)
);

CREATE INDEX IF NOT EXISTS ix_agent_profiles_service_code ON identity.agent_profiles(service_code);
