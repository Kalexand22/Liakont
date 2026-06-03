-- GV05: Persist preferred view mode per grid
ALTER TABLE grid.user_preferences
    ADD COLUMN IF NOT EXISTS preferred_view text;
