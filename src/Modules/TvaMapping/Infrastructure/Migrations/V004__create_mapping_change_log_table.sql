-- Journal append-only des modifications de la table de mapping TVA (item TVA05 §3). Écrit EN BASE
-- dans la MÊME transaction que la mutation décrite (atomicité, item TVA05 §5). Immuable : des triggers
-- REJETTENT tout UPDATE/DELETE d'une entrée existante ET tout TRUNCATE de la table (même discipline que
-- DocumentEvent, CLAUDE.md n°4 : aucune purge d'une table d'audit) — la garantie est au niveau base
-- (vérifiée par test), indépendante du code applicatif.
--
-- change_type : 0=AddRule, 1=UpdateRule, 2=RemoveRule, 3=Validate.
-- source_regime_code / part : règle concernée (NULL pour une validation de table).
-- before_value / after_value : valeur avant/après en jsonb (NULL pour un ajout / une suppression).
-- operator_id : identité Keycloak de l'opérateur (item TVA05 §3).
-- PAS de clé étrangère vers mapping_tables : le journal d'audit SURVIT à la table qu'il décrit
-- (un ON DELETE CASCADE détruirait la piste — interdit pour un journal append-only).
CREATE TABLE IF NOT EXISTS tvamapping.mapping_change_log (
    id                 uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id         uuid        NOT NULL,
    table_id           uuid        NOT NULL,
    mapping_version    text        NOT NULL,
    change_type        int         NOT NULL,
    source_regime_code text,
    part               int,
    before_value       jsonb,
    after_value        jsonb,
    operator_id        uuid        NOT NULL,
    operator_name      text,
    occurred_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_mapping_change_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_mapping_change_log_company
    ON tvamapping.mapping_change_log (company_id, occurred_at);

CREATE INDEX IF NOT EXISTS ix_mapping_change_log_table
    ON tvamapping.mapping_change_log (table_id, occurred_at);

-- Garde-fou append-only : aucune modification ni suppression d'une entrée existante. Un trigger
-- s'applique à TOUT rôle (y compris le propriétaire / superuser), contrairement à un REVOKE qui
-- n'a aucun effet sur le propriétaire de la table.
CREATE OR REPLACE FUNCTION tvamapping.reject_change_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des modifications de mapping TVA (tvamapping.mapping_change_log) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_mapping_change_log_append_only
    BEFORE UPDATE OR DELETE ON tvamapping.mapping_change_log
    FOR EACH ROW
    EXECUTE FUNCTION tvamapping.reject_change_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du journal d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_mapping_change_log_no_truncate
    BEFORE TRUNCATE ON tvamapping.mapping_change_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION tvamapping.reject_change_log_mutation();
