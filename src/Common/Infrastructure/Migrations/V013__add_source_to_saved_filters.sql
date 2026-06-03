-- GFI10: add source column to grid.saved_filters.
-- Identifies which builder produced the filter so it can be restored
-- into its original source at reload time (DF-02 exception persistance).
-- 0 = Simple (mono-criterion from the simple builder)
-- 1 = Advanced (default: matches all pre-GFI10 rows, which were created by StratumFilterBuilder)

ALTER TABLE grid.saved_filters
    ADD COLUMN IF NOT EXISTS source smallint NOT NULL DEFAULT 1;
