-- Liaison tenant-scopée référence fournisseur → document (ADR-0029 §5). Écrite à l'émission d'une demande de
-- signature ; relue par le drain pour rapatrier la preuve dans le coffre WORM (le webhook ne porte que la
-- référence fournisseur, pas le numéro/date d'émission du document). PAS de FK vers un schéma documents : le
-- document vit dans un autre module (frontière par Contracts — le numéro et la date sont recopiés ici à
-- l'émission). purpose = chaîne opaque (traçabilité). requested_level = SignatureLevel (paramétrage tenant).
-- Tenant-scopé par company_id (CLAUDE.md n°9).
CREATE TABLE IF NOT EXISTS signature.signature_requests (
    company_id         uuid        NOT NULL,
    provider_type      text        NOT NULL,
    provider_reference text        NOT NULL,
    document_id        uuid        NOT NULL,
    document_number    text        NOT NULL,
    issue_date         date        NOT NULL,
    purpose            text,
    requested_level    int         NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_signature_requests PRIMARY KEY (company_id, provider_type, provider_reference)
);

CREATE INDEX IF NOT EXISTS ix_signature_requests_document
    ON signature.signature_requests (company_id, document_id);
