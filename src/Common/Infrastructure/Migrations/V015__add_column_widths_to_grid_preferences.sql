-- GUX09: Persist per-column widths so drag-to-resize survives page reloads.
-- Values are stored as a JSON object keyed by column key, each value being a
-- CSS width token (e.g. "240px"). An empty object means widths were cleared.
ALTER TABLE grid.user_preferences
    ADD COLUMN IF NOT EXISTS column_widths jsonb;
