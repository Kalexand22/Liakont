-- UIM02: Teams — functional groups linked to services
CREATE TABLE IF NOT EXISTS identity.teams (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code         TEXT NOT NULL,
    name         TEXT NOT NULL,
    description  TEXT,
    service_code TEXT,
    is_active    BOOLEAN NOT NULL DEFAULT true,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ,
    CONSTRAINT uq_teams_code UNIQUE (code)
);

CREATE TABLE IF NOT EXISTS identity.team_members (
    id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    team_id   UUID NOT NULL REFERENCES identity.teams(id) ON DELETE CASCADE,
    user_id   UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    role      TEXT,
    joined_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_team_members_team_user UNIQUE (team_id, user_id)
);

CREATE INDEX IF NOT EXISTS ix_team_members_user_id ON identity.team_members(user_id);
