-- Journal APPEND-ONLY des transitions du workflow de validation (ADR-0028 §7, INV-APPROVAL-6). CHAQUE
-- transition (création incluse) écrit une ligne dans la MÊME transaction que la mutation d'état (atomicité,
-- « pas de transition sans ligne de journal »). Immuable : des triggers REJETTENT tout UPDATE/DELETE d'une
-- entrée existante ET tout TRUNCATE de la table (même discipline que self_billed_acceptance_log,
-- mapping_change_log et DocumentEvent, CLAUDE.md n°4 : aucune purge d'une table d'audit) — la garantie est au
-- niveau base (vérifiée par test), indépendante du code applicatif.
--
-- validation_purpose : ValidationPurpose (0..5, cf. V002). from_state/to_state : ValidationState (0..6).
-- from_state NULL = genèse (création de la tentative). signer_id NON NULL = transition d'un SLOT (N-parties).
-- operator_id NULL = transition SYSTÈME (bascule tacite / timeout par job). company_id NOT NULL (tenant-scopé).
-- PAS de FK vers document_validations : le journal SURVIT à l'entité qu'il décrit (un ON DELETE CASCADE
-- détruirait la piste — interdit pour un journal append-only).
CREATE TABLE IF NOT EXISTS documentapproval.document_approval_log (
    id                 uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id         uuid        NOT NULL,
    document_id        uuid        NOT NULL,
    validation_purpose int         NOT NULL,
    attempt            int         NOT NULL,
    from_state         int,
    to_state           int         NOT NULL,
    signer_id          text,
    operator_id        uuid,
    operator_name      text,
    occurred_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_document_approval_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_document_approval_log_company
    ON documentapproval.document_approval_log (company_id, occurred_at);

CREATE INDEX IF NOT EXISTS ix_document_approval_log_document
    ON documentapproval.document_approval_log (company_id, document_id, validation_purpose, occurred_at);

-- Garde-fou append-only : aucune modification ni suppression d'une entrée existante. Un trigger s'applique à
-- TOUT rôle (y compris le propriétaire / superuser), contrairement à un REVOKE qui n'a aucun effet sur le
-- propriétaire de la table.
CREATE OR REPLACE FUNCTION documentapproval.reject_approval_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des validations de document (documentapproval.document_approval_log) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_document_approval_log_append_only
    BEFORE UPDATE OR DELETE ON documentapproval.document_approval_log
    FOR EACH ROW
    EXECUTE FUNCTION documentapproval.reject_approval_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse du journal d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_document_approval_log_no_truncate
    BEFORE TRUNCATE ON documentapproval.document_approval_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION documentapproval.reject_approval_log_mutation();
