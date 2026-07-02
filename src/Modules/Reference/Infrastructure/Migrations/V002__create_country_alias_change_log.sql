-- Journal APPEND-ONLY des mutations du référentiel de correspondance pays (ADR-0038, §5 / INV-REF-CTRY-03).
-- Chaque upsert / remove écrit une ligne dans la MÊME transaction que la mutation de reference.country_alias
-- (atomicité : « pas de mutation sans ligne de journal, jamais l'inverse »). Immuable : des triggers REJETTENT
-- tout UPDATE/DELETE d'une entrée existante ET tout TRUNCATE (même discipline que mapping_change_log et
-- DocumentEvent, CLAUDE.md n°4) — la garantie est au niveau BASE (vérifiée par test), indépendante du code
-- applicatif. Le pays pilote l'aiguillage fiscal (UE / hors UE) : un alias faux-mais-valide mis-route en
-- silence, d'où l'exigence de traçabilité de chaque changement.
--
-- change_type  : 0=Create, 1=Update, 2=Remove.
-- source_code  : code source de la correspondance (clé de reference.country_alias).
-- before_value : valeur avant en jsonb (NULL pour un ajout/Create).
-- after_value  : valeur après en jsonb (NULL pour une suppression/Remove).
-- operator_id  : identité Keycloak de l'opérateur auteur de la mutation.
--
-- PAS de clé étrangère vers country_alias : le journal SURVIT à la table qu'il décrit (un ON DELETE CASCADE
-- détruirait la piste — interdit pour un journal append-only).
CREATE TABLE IF NOT EXISTS reference.country_alias_change_log (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    source_code   text        NOT NULL,
    change_type   int         NOT NULL,
    before_value  jsonb,
    after_value   jsonb,
    operator_id   uuid        NOT NULL,
    operator_name text,
    occurred_at   timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_country_alias_change_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_country_alias_change_log_source
    ON reference.country_alias_change_log (source_code, occurred_at);

CREATE INDEX IF NOT EXISTS ix_country_alias_change_log_occurred
    ON reference.country_alias_change_log (occurred_at);

-- Garde-fou append-only : aucune modification ni suppression d'une entrée existante. Un trigger s'applique à
-- TOUT rôle (y compris le propriétaire / superuser), contrairement à un REVOKE qui n'a aucun effet sur le
-- propriétaire de la table.
CREATE OR REPLACE FUNCTION reference.reject_country_alias_change_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des modifications du référentiel de correspondance pays (reference.country_alias_change_log) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_country_alias_change_log_append_only
    BEFORE UPDATE OR DELETE ON reference.country_alias_change_log
    FOR EACH ROW
    EXECUTE FUNCTION reference.reject_country_alias_change_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse du journal d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_country_alias_change_log_no_truncate
    BEFORE TRUNCATE ON reference.country_alias_change_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION reference.reject_country_alias_change_log_mutation();
