-- Définitions d'AXES typés du catalogue GED (F19 §3.3.1, item GED03a). Un axe = une dimension déclarée
-- (paramétrage tenant) qui porte une valeur TYPÉE sur un document (pièce maîtresse anti-EAV, INV-GED-01) :
-- `data_type` fixe la colonne de valeur utilisée (§3.4.3) ; `value_scale` porte l'échelle décimale d'un axe
-- `number` (2 = EUR, 0 = entier ; null = brut), appliquée en `decimal` half-up par `ValueNormalizer` (Domain
-- pur) AVANT insert — JAMAIS de double/float (CLAUDE.md n°1, F19 §7 règle 1).
--
-- `data_type` est un vocabulaire TECHNIQUE fermé de la plateforme (contrainte ck_axis_def_data_type), pas du
-- vocabulaire métier : aucun code de valeur d'axe métier n'est codé ici (paramétrage tenant, INV-GED-12 / règle 7).
--
-- ORDRE DES MIGRATIONS (RL-07) : la FK `fk_axis_def_target_entity` référence `ged_catalog.entity_types`
-- (V004). DbUp ordonne par nom de ressource → V005 s'applique APRÈS V004, donc `entity_types` existe et la FK
-- est satisfiable sur base vierge (acceptance GED03a « migrations sur base vierge, FK satisfaite »).
--
-- MUTABLE (config vivante) : `axis_definitions` n'est PAS append-only ; ses changements sont tracés dans
-- `ged_catalog.catalog_change_log` (V007, append-only). Un axe utilisé ne se DELETE pas — `is_active=false`.
CREATE TABLE IF NOT EXISTS ged_catalog.axis_definitions (
    id                    uuid        NOT NULL DEFAULT gen_random_uuid(),
    code                  text        NOT NULL,          -- clé machine stable de l'axe (paramétrage tenant)
    label                 text        NOT NULL,          -- libellé opérateur (FR)
    data_type             text        NOT NULL,          -- 'string'|'date'|'number'|'boolean'|'enum'|'entity'|'json'
    target_entity_type_id uuid,                           -- requis SSI data_type='entity' (soft graph target)
    value_scale           int,                            -- échelle décimale d'un axe number (2=EUR, 0=entier) ; null=brut
    is_multi_value        boolean     NOT NULL DEFAULT false,
    is_required           boolean     NOT NULL DEFAULT false,
    is_searchable         boolean     NOT NULL DEFAULT true,   -- alimente le tsvector (GED08)
    is_facetable          boolean     NOT NULL DEFAULT false,
    is_confidential       boolean     NOT NULL DEFAULT false,  -- masquage console/export/log/index/graphe (§6.5)
    retention_class       text,                            -- 'legal_hold'|'tenant_bounded'|'erasable' (§7, RGPD) ; null = hérité du tenant
    unit                  text,                            -- 'EUR','m2'… (informatif)
    ordinal               int         NOT NULL DEFAULT 0,
    is_active             boolean     NOT NULL DEFAULT true,   -- désactivation logique (jamais DELETE d'un axe utilisé)
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz,
    CONSTRAINT pk_axis_definitions PRIMARY KEY (id),
    CONSTRAINT uq_axis_definitions_code UNIQUE (code),
    CONSTRAINT fk_axis_def_target_entity FOREIGN KEY (target_entity_type_id)
        REFERENCES ged_catalog.entity_types (id),
    CONSTRAINT ck_axis_def_data_type CHECK (data_type IN
        ('string','date','number','boolean','enum','entity','json')),
    CONSTRAINT ck_axis_def_entity_target CHECK ((data_type = 'entity') = (target_entity_type_id IS NOT NULL)),
    CONSTRAINT ck_axis_def_scale CHECK (value_scale IS NULL OR (value_scale >= 0 AND value_scale <= 9))
);
