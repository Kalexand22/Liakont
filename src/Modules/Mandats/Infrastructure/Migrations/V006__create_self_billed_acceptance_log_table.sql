-- Journal APPEND-ONLY des transitions du workflow d'acceptation 389 (ADR-0024 §6, INV-ACCEPT-5). CHAQUE
-- transition d'état (création incluse) écrit une ligne dans la MÊME transaction que la mutation d'état
-- (atomicité, « pas de transition sans ligne de journal »). Immuable : des triggers REJETTENT tout
-- UPDATE/DELETE d'une entrée existante ET tout TRUNCATE de la table (même discipline que mandat_change_log
-- et DocumentEvent, CLAUDE.md n°4 : aucune purge d'une table d'audit) — la garantie est au niveau base
-- (vérifiée par test), indépendante du code applicatif.
--
-- from_state/to_state : 0=PendingAcceptance, 1=Accepted, 2=TacitlyAccepted, 3=Contested. from_state NULL =
-- genèse (création de l'agrégat). operator_id NULL = transition SYSTÈME (bascule tacite par job, MND04 —
-- sans opérateur humain). company_id NOT NULL (tenant-scopé). PAS de clé étrangère vers
-- self_billed_acceptances ni vers un schéma documents : le journal SURVIT à l'entité qu'il décrit (un
-- ON DELETE CASCADE détruirait la piste — interdit pour un journal append-only).
CREATE TABLE IF NOT EXISTS mandats.self_billed_acceptance_log (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id    uuid        NOT NULL,
    document_id   uuid        NOT NULL,
    from_state    int,
    to_state      int         NOT NULL,
    operator_id   uuid,
    operator_name text,
    occurred_at   timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_self_billed_acceptance_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_self_billed_acceptance_log_company
    ON mandats.self_billed_acceptance_log (company_id, occurred_at);

CREATE INDEX IF NOT EXISTS ix_self_billed_acceptance_log_document
    ON mandats.self_billed_acceptance_log (company_id, document_id, occurred_at);

-- Garde-fou append-only : aucune modification ni suppression d'une entrée existante. Un trigger s'applique à
-- TOUT rôle (y compris le propriétaire / superuser), contrairement à un REVOKE qui n'a aucun effet sur le
-- propriétaire de la table.
CREATE OR REPLACE FUNCTION mandats.reject_acceptance_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des acceptations d''autofacturation (mandats.self_billed_acceptance_log) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_self_billed_acceptance_log_append_only
    BEFORE UPDATE OR DELETE ON mandats.self_billed_acceptance_log
    FOR EACH ROW
    EXECUTE FUNCTION mandats.reject_acceptance_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse du journal d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_self_billed_acceptance_log_no_truncate
    BEFORE TRUNCATE ON mandats.self_billed_acceptance_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION mandats.reject_acceptance_log_mutation();
