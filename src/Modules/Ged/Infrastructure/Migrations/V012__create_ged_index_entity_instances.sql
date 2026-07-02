-- Registre POLYMORPHE des instances d'entité du graphe GED (F19 §3.4.2, item GED03c). Comme entity_types
-- (V004) est un registre de TYPES, entity_instances est un registre d'INSTANCES : au lieu d'une table dédiée
-- PAR type d'entité, UN registre générique dont chaque ligne est une instance d'un type déclaré au catalogue.
-- `entity_type_id` est une référence LOGIQUE → ged_catalog.entity_types.id, validée en Application (§3.8) :
-- PAS de FK cross-« sous-domaine » (le registre survit à toute désactivation logique d'un type ; soft-link).
--
-- FRONTIÈRE (F19 §3.3.2, P1) : ce registre N'ABSORBE PAS les entités fiscales/socle (Mandats,
-- Stratum.Modules.Party) — elles restent dans leurs modules ; une instance GED qui correspond à un tiers
-- s'y RÉFÈRE par soft-link (identity_value / external_ref), jamais en le ré-hébergeant.
--
-- ANTI-EAV (INV-GED-04, F19 §3.4.2/§3.5) : `attributes jsonb` est PRÉSENTATION-ONLY — jamais recherché,
-- facetté ni traversé (aucun index sur cette colonne, volontairement). Toute donnée INTERROGEABLE est un axe
-- déclaré (document_axis_links) ; le seul canal de recherche d'entité est `search_vector` (index GIN).
--
-- FTS d'entité (F19 §3.4.2, asymétrie assumée vs document_search) : `search_vector` est maintenu EN PLACE
-- dans la même mutation (journalisée) que l'instance ; ce n'est PAS le modèle dérivé/reconstructible de
-- document_search (GED08). Migration vers une table dérivée `entity_search` = fast-follow si le volume l'exige.
--
-- MUTABLE (registre vivant) : entity_instances n'est PAS append-only ; ses mutations sont tracées dans
-- ged_index.entity_instance_change_log (V013, append-only). Base DU TENANT (isolation = la connexion, F19 §3.2).
CREATE TABLE IF NOT EXISTS ged_index.entity_instances (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    entity_type_id  uuid        NOT NULL,           -- référence LOGIQUE → ged_catalog.entity_types.id (validée en Application, §3.8)
    display_name    text        NOT NULL,           -- libellé opérateur (FR)
    identity_value  text,                            -- valeur normalisée de la clé de résolution (§4.4), ex. SIRET ; null si pas de clé
    canonical_id    uuid,                            -- identité canonique après fusion de doublons (§4.4) ; null = canonique
    external_ref    text,                            -- réf. source brute
    attributes      jsonb,                           -- PRÉSENTATION-ONLY, jamais recherché/facetté/traversé (INV-GED-04)
    search_vector   tsvector,                        -- FTS d'entité, maintenu inline (libellés courts, faible cardinalité)
    is_active       boolean     NOT NULL DEFAULT true,   -- désactivation logique (jamais DELETE d'une instance référencée)
    created_utc     timestamptz NOT NULL DEFAULT now(),
    updated_utc     timestamptz,
    CONSTRAINT pk_entity_instances PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_ei_type     ON ged_index.entity_instances (entity_type_id);
CREATE INDEX IF NOT EXISTS ix_ei_identity ON ged_index.entity_instances (entity_type_id, identity_value) WHERE identity_value IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_ei_search   ON ged_index.entity_instances USING gin (search_vector);
-- INV-GED-04 : AUCUN index sur `attributes` (présentation-only) — le seul canal interrogeable est search_vector.
