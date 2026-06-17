-- Journal APPEND-ONLY de la preuve de signature SUR PLACE (ADR-0030 §3/§4/§5 ; INV-ONSITE-6). Chaque capture
-- dont le binding est VÉRIFIÉ (re-hash == hash signé sur les octets exacts de l'artefact scellé) écrit ici une
-- ligne immuable. La métadonnée seule est en base (empreinte de binding, déposant, signataire vérifié, niveau,
-- référence WORM de l'artefact) ; l'artefact lui-même (PNG + FSS chiffrée) est rapatrié dans le coffre WORM via
-- Archive.Contracts. AUCUN GABARIT BIOMÉTRIQUE n'est stocké (ADR-0030 §8, INV-ONSITE-10).
--
-- Immuable : des triggers REJETTENT tout UPDATE/DELETE d'une entrée existante ET tout TRUNCATE de la table
-- (même discipline que document_approval_log / DocumentEvent, CLAUDE.md n°4 : aucune purge d'audit) — garantie
-- au niveau base (vérifiée par test), indépendante du code applicatif.
--
-- uploader_user_id = le DÉPOSANT (principal authentifié), JAMAIS le signataire. signer_identity NULL = aucune
-- liaison vérifiée résolue (signataire non prouvé ; le niveau reste SES). company_id NOT NULL (tenant-scopé).
-- PAS de FK vers documents.documents (frontière de module — le journal survit et n'appartient pas à Documents).
CREATE TABLE IF NOT EXISTS signature.onsite_signature_proofs (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    -- seq : ordre d'insertion MONOTONE — départage déterministe au même recorded_at (now() = ts de transaction).
    seq               bigint      GENERATED ALWAYS AS IDENTITY,
    company_id        uuid        NOT NULL,
    document_id       uuid        NOT NULL,
    binding_hash      text        NOT NULL,
    uploader_user_id  uuid        NOT NULL,
    signer_identity   text,
    signer_verified   boolean     NOT NULL,
    level             text        NOT NULL,
    proof_archive_ref text,
    captured_at       timestamptz NOT NULL,
    recorded_at       timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_onsite_signature_proofs PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_onsite_signature_proofs_document
    ON signature.onsite_signature_proofs (company_id, document_id, seq DESC);

-- Garde-fou append-only : aucune modification/suppression d'une entrée existante (tout rôle, superuser inclus).
CREATE OR REPLACE FUNCTION signature.reject_onsite_signature_proof_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des preuves de signature sur place (signature.onsite_signature_proofs) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_onsite_signature_proofs_append_only
    BEFORE UPDATE OR DELETE ON signature.onsite_signature_proofs
    FOR EACH ROW
    EXECUTE FUNCTION signature.reject_onsite_signature_proof_mutation();

-- TRUNCATE : trigger d'INSTRUCTION séparé (FOR EACH ROW ne couvre pas TRUNCATE) — ferme la purge en masse.
CREATE OR REPLACE TRIGGER trg_onsite_signature_proofs_no_truncate
    BEFORE TRUNCATE ON signature.onsite_signature_proofs
    FOR EACH STATEMENT
    EXECUTE FUNCTION signature.reject_onsite_signature_proof_mutation();
