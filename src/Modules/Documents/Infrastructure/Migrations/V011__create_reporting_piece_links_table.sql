-- Traçabilité reporting↔pièces (item B2C03, F06 §3 / F09 §10.3). Lien IMMUABLE entre une transmission
-- d'e-reporting B2C (déclaration 10.3, identifiée par son document) et ses PIÈCES source (référence de
-- pièce du pivot, ADR-0007 — p. ex. bordereau acheteur). Le lien est FIGÉ à la transmission.
--
-- DOUBLE SENS : deux index dédiés permettent d'interroger transmission → pièces ET pièce → transmissions.
-- TENANT-SCOPÉ : company_id NOT NULL (défense en profondeur — la base est déjà par tenant, blueprint §7 ;
--   toutes les requêtes filtrent en plus sur company_id, comme tvamapping.mapping_change_log V004).
-- APPEND-ONLY (CLAUDE.md n°4) : aucun chemin d'update/delete applicatif ET des triggers REJETTENT tout
--   UPDATE/DELETE d'une entrée ainsi que tout TRUNCATE de la table (même discipline que document_events V003).
--   La garantie est au niveau base (vérifiée par test), indépendante du code applicatif.
CREATE TABLE IF NOT EXISTS documents.reporting_piece_links (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id        uuid        NOT NULL,
    document_id       uuid        NOT NULL,   -- la déclaration 10.3 transmise (la « transmission »)
    source_reference  text        NOT NULL,   -- la pièce source (PivotDocumentDto.SourceReference, ADR-0007)
    linked_at_utc     timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_reporting_piece_links PRIMARY KEY (id),
    -- Idempotence du gel : un même (tenant, transmission, pièce) ne se lie qu'une fois. Un rejeu d'envoi
    -- (ON CONFLICT DO NOTHING côté store) est ainsi sûr — c'est un no-op, jamais un UPDATE (append-only).
    -- PAS de clé étrangère vers documents.documents : le lien est un fait d'audit autonome, gelé à la
    -- transmission, qui ne dépend pas du cycle de vie de la ligne document.
    CONSTRAINT uq_reporting_piece_links UNIQUE (company_id, document_id, source_reference)
);

-- Double sens : transmission → pièces.
CREATE INDEX IF NOT EXISTS ix_reporting_piece_links_by_document
    ON documents.reporting_piece_links (company_id, document_id);
-- Double sens : pièce → transmissions.
CREATE INDEX IF NOT EXISTS ix_reporting_piece_links_by_source
    ON documents.reporting_piece_links (company_id, source_reference);

-- Garde-fou append-only : un trigger s'applique à TOUT rôle (y compris propriétaire / superuser),
-- contrairement à un REVOKE sans effet sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION documents.reject_reporting_piece_link_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le lien reporting↔pièces (documents.reporting_piece_links) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4 ; B2C03).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_reporting_piece_links_append_only
    BEFORE UPDATE OR DELETE ON documents.reporting_piece_links
    FOR EACH ROW
    EXECUTE FUNCTION documents.reject_reporting_piece_link_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du journal de traçabilité (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_reporting_piece_links_no_truncate
    BEFORE TRUNCATE ON documents.reporting_piece_links
    FOR EACH STATEMENT
    EXECUTE FUNCTION documents.reject_reporting_piece_link_mutation();
