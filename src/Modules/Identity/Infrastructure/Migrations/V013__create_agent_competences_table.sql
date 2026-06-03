-- UIM02: Agent competences — certifications and skills per agent
CREATE TABLE IF NOT EXISTS identity.agent_competences (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    name        TEXT NOT NULL,
    category    TEXT,
    valid_until DATE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_agent_competences_user_id ON identity.agent_competences(user_id);
