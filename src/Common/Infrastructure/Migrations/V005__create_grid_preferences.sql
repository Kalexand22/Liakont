-- GC02: Per-user grid column preferences
-- GridKey convention: module.page.grid (e.g. "Sales.InvoiceList.Main")

CREATE SCHEMA IF NOT EXISTS grid;

CREATE TABLE IF NOT EXISTS grid.user_preferences
(
    id           uuid        NOT NULL DEFAULT gen_random_uuid(),
    user_id      uuid        NOT NULL,
    grid_key     text        NOT NULL,
    column_keys  jsonb       NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz,

    CONSTRAINT pk_grid_user_preferences PRIMARY KEY (id),
    CONSTRAINT uq_grid_user_preferences_user_grid UNIQUE (user_id, grid_key)
);

CREATE INDEX IF NOT EXISTS ix_grid_user_preferences_user_id
    ON grid.user_preferences (user_id);
