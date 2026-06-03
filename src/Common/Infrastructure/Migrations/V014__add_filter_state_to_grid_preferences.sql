-- GFI14: Persist the serialized GridFilterState per user/grid so the page can
-- automatically restore the last filter state when the user navigates back.
ALTER TABLE grid.user_preferences
    ADD COLUMN IF NOT EXISTS filter_state jsonb;
