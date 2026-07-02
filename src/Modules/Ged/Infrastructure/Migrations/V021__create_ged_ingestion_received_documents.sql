-- Registre de réception du canal GED (F19 §2.4/§4.3.1, item GED05b). Vit dans la base SYSTÈME (schéma
-- ged_ingestion, V003), co-localisé avec l'outbox pour que l'INSERT du registre ET l'écriture de l'événement
-- ManagedDocumentReceivedV1 soient ATOMIQUES dans une même transaction (RL-03 ; calqué EXACTEMENT sur
-- ingestion.received_documents / PostgresReceivedDocumentUnitOfWork). Espace de hash STRICTEMENT SÉPARÉ du canal
-- fiscal (§4.3.1) : partager ingestion.received_documents créerait de faux Duplicate/AcceptedAltered entre deux
-- sérialiseurs canoniques disjoints. AUCUN Document fiscal n'est créé par ce canal (le handler GED n'appelle pas
-- IDocumentIntake) : ce registre est le SEUL journal d'anti-doublon (tenant, payload_hash) du canal GED.
--
-- APPEND-ONLY par DISCIPLINE APPLICATIVE (comme ingestion.received_documents) : le handler n'exécute qu'un INSERT,
-- jamais d'UPDATE/DELETE. Chaque ligne porte son tenant_id (slug résolu à l'ingestion via l'agent authentifié) —
-- toute lecture/écriture est scopée à ce tenant (anti-fuite cross-tenant, CLAUDE.md n°9). Écrit via
-- ISystemConnectionFactory (base système partagée), JAMAIS via IConnectionFactory (base tenant).
CREATE TABLE IF NOT EXISTS ged_ingestion.ged_received_documents (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id           text        NOT NULL,                  -- slug du tenant de l'agent authentifié (base système, RL-03)
    source_reference    text        NOT NULL,                  -- référence du document dans la source
    payload_hash        text        NOT NULL,                  -- empreinte SHA-256 du JSON canonique GED (anti-doublon local)
    managed_document_id uuid        NOT NULL,                  -- id attribué au ManagedDocument, porté dans ManagedDocumentReceivedV1 (idempotence RL-04)
    contract_version    text        NOT NULL,                  -- version du contrat Liakont.Agent.Contracts.Ged (§4.3.3) — jamais une version fiscale
    received_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_ged_received_documents PRIMARY KEY (id)
);

-- Anti-doublon GED strictement local (tenant, hash) : une ré-ingestion du même contenu est un Duplicate.
CREATE UNIQUE INDEX IF NOT EXISTS uq_ged_received_tenant_payload
    ON ged_ingestion.ged_received_documents (tenant_id, payload_hash);

-- Détection d'altération : dernière empreinte reçue pour une référence source d'un tenant (comme le canal fiscal).
CREATE INDEX IF NOT EXISTS ix_ged_received_tenant_source
    ON ged_ingestion.ged_received_documents (tenant_id, source_reference, received_at DESC);
