-- Registre POLYMORPHE des types d'entité GED (F19 §3.3.2, item GED03a). Au lieu d'une table codée en dur
-- PAR type d'entité (pattern Mandats.Mandant / Stratum.Modules.Party), UN registre générique dont chaque
-- ligne DÉCLARE un type d'entité métier (paramétrage tenant) : `code` est une clé machine libre, JAMAIS un
-- enum figé côté plateforme (INV-GED-12 / règle 7 : aucun vocabulaire métier en dur dans src/Modules/Ged/**).
--
-- FRONTIÈRE (F19 §3.3.2, P1) : ce registre NE remplace ni n'absorbe les entités fiscales/socle existantes
-- (Mandats, Stratum.Modules.Party) — elles restent dans leurs modules. Une entité GED qui correspond à un
-- tiers fiscal s'y RÉFÈRE par soft-link (id externe), jamais en le ré-hébergeant.
--
-- ORDRE DES MIGRATIONS (RL-07) : cette table est créée AVANT `axis_definitions` (V005) car la FK
-- `fk_axis_def_target_entity` de cette dernière la référence ; DbUp ordonne par nom de ressource, donc
-- V004 < V005 garantit que `entity_types` existe quand la FK est posée (base vierge, acceptance GED03a).
--
-- MUTABLE (config vivante) : `entity_types` n'est PAS append-only ; ses changements sont tracés dans
-- `ged_catalog.catalog_change_log` (V007, append-only). Base DU TENANT (isolation = la connexion, F19 §3.2).
CREATE TABLE IF NOT EXISTS ged_catalog.entity_types (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    code            text        NOT NULL,           -- clé machine libre du type d'entité (paramétrage tenant)
    label           text        NOT NULL,           -- libellé opérateur (FR)
    identity_key    text,                            -- clé de résolution d'identité (§4.4), ex. 'siret' ; null = pas de dédup auto
    is_confidential boolean     NOT NULL DEFAULT false,  -- §6.5 : entité/relation confidentielle non traversable sans droit
    is_active       boolean     NOT NULL DEFAULT true,   -- désactivation logique (jamais DELETE d'un type utilisé)
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz,
    CONSTRAINT pk_entity_types PRIMARY KEY (id),
    CONSTRAINT uq_entity_types_code UNIQUE (code)
);
