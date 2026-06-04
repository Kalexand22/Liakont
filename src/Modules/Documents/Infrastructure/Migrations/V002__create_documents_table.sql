-- Document métier de la passerelle (F06 §3, item TRK01). Une ligne par document détecté dans le tenant.
-- RÈGLE MONTANTS (CLAUDE.md n°1) : total_net / total_tax / total_gross en NUMERIC(18,2) — JAMAIS de
-- type flottant. Le round-trip base <-> decimal est sans perte (vérifié par test avec des montants
-- piégeux : 0.1+0.2, 1162.80).
-- state : libellé textuel de l'état (DocumentState, F06 §3) — lisible pour un contrôle fiscal et
-- robuste à un renumérotage de l'énumération. Le document est créé en 'Detected' par l'ingestion (PIV04).
-- supplier_siren / customer_name : NULL quand la source ne les porte pas (B2C, donnée absente) — jamais
-- de défaut implicite masquant une absence (blueprint §8).
-- pa_document_id / mapping_version : NULL à la détection, renseignés en aval (Transmission / pipeline PIP).
CREATE TABLE IF NOT EXISTS documents.documents (
    id                        uuid           NOT NULL DEFAULT gen_random_uuid(),
    source_reference          text           NOT NULL,
    document_number           text           NOT NULL,
    document_type             text           NOT NULL,
    issue_date                date           NOT NULL,
    supplier_siren            text,
    customer_name             text,
    customer_is_company_hint  boolean        NOT NULL DEFAULT false,
    total_net                 numeric(18, 2) NOT NULL,
    total_tax                 numeric(18, 2) NOT NULL,
    total_gross               numeric(18, 2) NOT NULL,
    state                     text           NOT NULL,
    payload_hash              text           NOT NULL,
    pa_document_id            text,
    mapping_version           text,
    first_seen_utc            timestamptz    NOT NULL DEFAULT now(),
    last_update_utc           timestamptz    NOT NULL DEFAULT now(),

    CONSTRAINT pk_documents PRIMARY KEY (id)
);

-- Lecture par numéro (EN 16931 BT-1) : index NON unique. Le numéro n'est pas une clé d'unicité en base
-- car un document rejeté peut être remplacé sous un nouveau numéro et l'ancien passer Superseded
-- (F06 §4) ; l'anti-doublon (état Issued / empreinte de payload) est une règle APPLICATIVE (TRK03),
-- jamais une contrainte qui casserait la mécanique de remplacement.
CREATE INDEX IF NOT EXISTS ix_documents_number
    ON documents.documents (document_number);

-- File d'attente par état (vues paginées console), triée par dernière mise à jour décroissante.
CREATE INDEX IF NOT EXISTS ix_documents_state
    ON documents.documents (state, last_update_utc DESC);

-- Recherche anti-doublon par (SIREN fournisseur, numéro), exploitée par TRK03 (F06 §4).
CREATE INDEX IF NOT EXISTS ix_documents_supplier_number
    ON documents.documents (supplier_siren, document_number);
