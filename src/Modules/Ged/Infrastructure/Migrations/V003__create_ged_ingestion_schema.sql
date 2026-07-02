-- Schéma d'INGESTION GED (F19 §2.1/§3.2/§4.3.1, item GED02). Registre de réception du canal GED
-- (ged_received_documents, anti-doublon par payload_hash) + son outbox. Vit dans la base SYSTÈME
-- (partagée), EXCEPTION documentée (F19 §3.2 (a)) : c'est la SEULE façon d'écrire ATOMIQUEMENT le registre
-- ET l'événement ManagedDocumentReceivedV1 dans une même transaction (il n'existe pas de 2PC entre deux
-- bases PG), exactement comme ingestion.received_documents. Chaque ligne portera son tenant_id (slug résolu
-- à l'ingestion) ; toute lecture/écriture est scopée au tenant de l'agent authentifié (CLAUDE.md n°9).
-- Écrit via ISystemConnectionFactory à partir de GED05b. GED02 crée le schéma VIDE : AUCUNE table ici.
CREATE SCHEMA IF NOT EXISTS ged_ingestion;
