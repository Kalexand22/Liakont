-- Index du point de statut agent (PIP01a — FindStatusBySourceReferenceAndPayloadHash, sollicité EN BOUCLE
-- par l'agent : ADR-0012/0014). Couvre le filtre (source_reference, payload_hash) ET le tri sur la dernière
-- mise à jour, évitant un seq scan sur documents.documents à chaque interrogation.
CREATE INDEX IF NOT EXISTS ix_documents_source_ref_payload_hash
    ON documents.documents (source_reference, payload_hash, last_update_utc DESC);
