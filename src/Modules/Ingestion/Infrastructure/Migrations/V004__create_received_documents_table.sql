-- Registre de RÉCEPTION des documents (anti-doublon, F12 §3-4, PIV04). Vit dans la base SYSTÈME
-- (schéma ingestion), comme le registre d'agents : chaque ligne porte son tenant_id (slug), et toute
-- lecture/écriture est scopée au tenant de l'agent authentifié (CLAUDE.md n°9). Append-only : un
-- document reçu est un FAIT (aucun chemin d'update/delete applicatif).
-- payload_hash = empreinte canonique SHA-256 du payload (PIV02). Unique PAR TENANT = anti-doublon ;
-- une même source_reference peut réapparaître avec une autre empreinte (altération de la source, F06).
CREATE TABLE IF NOT EXISTS ingestion.received_documents (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id        text        NOT NULL,
    source_reference text        NOT NULL,
    payload_hash     text        NOT NULL,
    document_id      uuid        NOT NULL,
    contract_version text        NOT NULL,
    received_at      timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_received_documents PRIMARY KEY (id)
);

-- Anti-doublon : une empreinte de payload est unique PAR TENANT (protège du re-push complet d'un
-- agent réinstallé). Sert aussi de garde-fou contre les courses entre lots concurrents.
CREATE UNIQUE INDEX uq_received_documents_tenant_payload
    ON ingestion.received_documents (tenant_id, payload_hash);

-- Détection d'altération + lecture du dernier hash connu pour une référence source (scopé tenant).
CREATE INDEX ix_received_documents_tenant_source_ref
    ON ingestion.received_documents (tenant_id, source_reference, received_at DESC);
