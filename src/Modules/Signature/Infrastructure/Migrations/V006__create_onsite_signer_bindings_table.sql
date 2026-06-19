-- Liaison VÉRIFIÉE déposant→signataire de la signature sur place (ADR-0030 §5, INV-ONSITE-7). Un opérateur
-- SVV authentifié consigne ici l'identité du mandant identifié EN PERSONNE au guichet — DISTINCTE de la
-- capture (le déposant qui téléverse n'est pas le signataire). C'est la SEULE source d'un SignerIdentity
-- probant : la capture la résout côté serveur, jamais depuis son propre payload (test d'usurpation).
--
-- APPEND-ONLY (registre d'identité immuable, même discipline que document_approval_log / DocumentEvent,
-- CLAUDE.md n°4) : une re-vérification écrit une NOUVELLE ligne ; la résolution lit la plus récente (seq).
-- company_id NOT NULL (tenant-scopé). PAS de FK vers documents.documents : le module Signature ne possède
-- pas la table des documents (frontière de module) ; l'appartenance document↔tenant est re-vérifiée par le
-- proxy via Documents.Contracts (CLAUDE.md n°9/14).
CREATE TABLE IF NOT EXISTS signature.onsite_signer_bindings (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    -- seq : ordre d'insertion MONOTONE — départage de façon DÉTERMINISTE deux liaisons au même verified_at
    -- (now() = timestamp de transaction, identique pour plusieurs lignes d'une même transaction).
    seq                 bigint      GENERATED ALWAYS AS IDENTITY,
    company_id          uuid        NOT NULL,
    document_id         uuid        NOT NULL,
    signer_identity     text        NOT NULL,
    verification_method text        NOT NULL,
    registered_by       uuid        NOT NULL,
    verified_at         timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_onsite_signer_bindings PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_onsite_signer_bindings_document
    ON signature.onsite_signer_bindings (company_id, document_id, seq DESC);

-- Garde-fou append-only : aucune modification ni suppression d'une entrée existante. Un trigger s'applique à
-- TOUT rôle (y compris le propriétaire / superuser), contrairement à un REVOKE sans effet sur le propriétaire.
CREATE OR REPLACE FUNCTION signature.reject_onsite_signer_binding_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le registre des signataires vérifiés (signature.onsite_signer_bindings) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_onsite_signer_bindings_append_only
    BEFORE UPDATE OR DELETE ON signature.onsite_signer_bindings
    FOR EACH ROW
    EXECUTE FUNCTION signature.reject_onsite_signer_binding_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION ferme ce vecteur
-- de purge en masse (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_onsite_signer_bindings_no_truncate
    BEFORE TRUNCATE ON signature.onsite_signer_bindings
    FOR EACH STATEMENT
    EXECUTE FUNCTION signature.reject_onsite_signer_binding_mutation();
