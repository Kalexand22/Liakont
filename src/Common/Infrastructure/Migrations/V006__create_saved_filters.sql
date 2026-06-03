-- GF02: Per-user saved filters for grids
-- GridKey convention: module.page.grid (e.g. "Sales.InvoiceList.Main")

CREATE TABLE IF NOT EXISTS grid.saved_filters
(
    id           uuid        NOT NULL DEFAULT gen_random_uuid(),
    user_id      uuid        NOT NULL,
    grid_key     text        NOT NULL,
    name         text        NOT NULL,
    filter_group jsonb       NOT NULL,
    is_default   boolean     NOT NULL DEFAULT false,
    shared_with  smallint    NOT NULL DEFAULT 0,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz,

    CONSTRAINT pk_grid_saved_filters PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_grid_saved_filters_user_grid
    ON grid.saved_filters (user_id, grid_key);

-- Partial unique index: at most one default per user per grid
CREATE UNIQUE INDEX IF NOT EXISTS uq_grid_saved_filters_user_grid_default
    ON grid.saved_filters (user_id, grid_key)
    WHERE is_default = true;
