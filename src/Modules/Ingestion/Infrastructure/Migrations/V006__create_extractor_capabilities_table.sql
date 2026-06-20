-- Capacités déclarées de la source d'un agent (ADR-0004 D2, RD401). Métadonnée de push, base SYSTÈME
-- (schéma ingestion), scopée (tenant_id, agent_id). La plateforme S'ADAPTE à ces capacités déclarées
-- (Validation, Ingestion, Transmission) — JAMAIS par if (source is NAV) ; symétrique de PaCapabilities.
-- Upsert par (tenant_id, agent_id) : la DERNIÈRE déclaration remplace la précédente (jamais cumulée → un
-- retry ne corrompt rien), horodatage rafraîchi. Les formes énumérées (régime, identité émetteur, unicité
-- du numéro) sont conservées en valeur BRUTE (nom de l'énumération source) : aucune interprétation ici
-- (CLAUDE.md n°6/n°2). Consommé par RD403 (ExposesPayments → F09, IsMutableAfterIssue → alerte) et RD409.
CREATE TABLE IF NOT EXISTS ingestion.extractor_capabilities (
    tenant_id                       text        NOT NULL,
    agent_id                        uuid        NOT NULL,
    provides_source_documents       boolean     NOT NULL,
    provides_unlinked_document_pool boolean     NOT NULL,
    has_detailed_lines              boolean     NOT NULL,
    has_credit_note_link            boolean     NOT NULL,
    exposes_payments                boolean     NOT NULL,
    regime_key_shape                text,
    emitter_identity_source         text,
    has_stored_header_total         boolean     NOT NULL,
    is_mutable_after_issue          boolean     NOT NULL,
    number_uniqueness_scope         text,
    last_seen_at                    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_extractor_capabilities PRIMARY KEY (tenant_id, agent_id)
);
