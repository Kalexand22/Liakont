-- Journal append-only des modifications du registre des mandants ET du cycle de vie des mandats
-- (ADR-0022 §3, INV-MANDATS-3). Écrit EN BASE dans la MÊME transaction que la mutation décrite
-- (atomicité, « pas de mutation sans ligne de journal »). Immuable : des triggers REJETTENT tout
-- UPDATE/DELETE d'une entrée existante ET tout TRUNCATE de la table (même discipline que DocumentEvent
-- et mapping_change_log, CLAUDE.md n°4 : aucune purge d'une table d'audit) — la garantie est au niveau
-- base (vérifiée par test), indépendante du code applicatif.
--
-- change_type : 0=CreateMandant, 1=UpdateMandant, 2=CreateMandat, 3=UpdateMandat, 4=ValidateMandat, 5=RevokeMandat.
-- mandant_id : mandant concerné (toujours présent). mandat_id : mandat concerné (NULL pour une modification de mandant).
-- reference : référence métier de l'entité touchée (aide à la lecture). before_value/after_value : jsonb (NULL pour création/révocation).
-- operator_id : identité de l'opérateur. PAS de clé étrangère vers mandants/mandats : le journal SURVIT
-- à l'entité qu'il décrit (un ON DELETE CASCADE détruirait la piste — interdit pour un journal append-only).
CREATE TABLE IF NOT EXISTS mandats.mandat_change_log (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id    uuid        NOT NULL,
    mandant_id    uuid        NOT NULL,
    mandat_id     uuid,
    reference     text        NOT NULL,
    change_type   int         NOT NULL,
    before_value  jsonb,
    after_value   jsonb,
    operator_id   uuid        NOT NULL,
    operator_name text,
    occurred_at   timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_mandat_change_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_mandat_change_log_company
    ON mandats.mandat_change_log (company_id, occurred_at);

CREATE INDEX IF NOT EXISTS ix_mandat_change_log_mandant
    ON mandats.mandat_change_log (mandant_id, occurred_at);

-- Garde-fou append-only : aucune modification ni suppression d'une entrée existante. Un trigger
-- s'applique à TOUT rôle (y compris le propriétaire / superuser), contrairement à un REVOKE qui
-- n'a aucun effet sur le propriétaire de la table.
CREATE OR REPLACE FUNCTION mandats.reject_change_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des modifications de mandats (mandats.mandat_change_log) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_mandat_change_log_append_only
    BEFORE UPDATE OR DELETE ON mandats.mandat_change_log
    FOR EACH ROW
    EXECUTE FUNCTION mandats.reject_change_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du journal d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_mandat_change_log_no_truncate
    BEFORE TRUNCATE ON mandats.mandat_change_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION mandats.reject_change_log_mutation();
