-- UIM02: Delegations — temporary signature delegations between agents
CREATE TABLE IF NOT EXISTS identity.delegations (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    delegator_id  UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    delegate_id   UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    scope         TEXT NOT NULL,
    valid_from    TIMESTAMPTZ NOT NULL,
    valid_until   TIMESTAMPTZ NOT NULL,
    reason        TEXT,
    is_active     BOOLEAN NOT NULL DEFAULT true,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_delegation_no_self CHECK (delegator_id <> delegate_id),
    CONSTRAINT chk_delegation_dates CHECK (valid_until > valid_from)
);

CREATE INDEX IF NOT EXISTS ix_delegations_delegator ON identity.delegations(delegator_id);
CREATE INDEX IF NOT EXISTS ix_delegations_delegate ON identity.delegations(delegate_id);
CREATE INDEX IF NOT EXISTS ix_delegations_active ON identity.delegations(is_active) WHERE is_active = true;
