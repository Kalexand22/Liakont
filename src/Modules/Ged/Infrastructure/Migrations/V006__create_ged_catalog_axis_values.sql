-- Vocabulaire d'un axe `enum` du catalogue GED (F19 §3.3.3, item GED03a). Chaque ligne déclare une valeur
-- autorisée (code machine + libellé) d'un axe de `data_type='enum'` (paramétrage tenant) : la validation
-- « la valeur d'axe appartient au vocabulaire » est faite en Application (GED04, IAxisCatalog) — jamais
-- deviner un code hors vocabulaire (règle 2). Aucun code métier n'est codé ici (INV-GED-12 / règle 7).
--
-- FK vers `axis_definitions` (V005) : DbUp ordonne par nom → V006 s'applique APRÈS V005 (base vierge OK).
-- ON DELETE CASCADE : le vocabulaire suit la définition de l'axe (F19 §3.3.3). MUTABLE (config vivante) ;
-- les changements de config sont tracés dans `ged_catalog.catalog_change_log` (V007, append-only).
CREATE TABLE IF NOT EXISTS ged_catalog.axis_values (
    id        uuid    NOT NULL DEFAULT gen_random_uuid(),
    axis_id   uuid    NOT NULL,
    code      text    NOT NULL,       -- code machine de la valeur enum (paramétrage tenant)
    label     text    NOT NULL,       -- libellé opérateur (FR)
    ordinal   int     NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    CONSTRAINT pk_axis_values PRIMARY KEY (id),
    CONSTRAINT fk_axis_values_axis FOREIGN KEY (axis_id)
        REFERENCES ged_catalog.axis_definitions (id) ON DELETE CASCADE,
    CONSTRAINT uq_axis_values_axis_code UNIQUE (axis_id, code)
);
