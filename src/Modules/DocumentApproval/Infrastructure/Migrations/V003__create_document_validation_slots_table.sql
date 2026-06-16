-- Slots d'approbation N-parties (ADR-0028 §8). Ensemble FIXE de slots identifiés, défini à la création de la
-- tentative ; chaque slot idempotent par signer_id (la clé primaire inclut signer_id → une 2ᵉ preuve du même
-- signataire écrase la même ligne, jamais un compteur). Complétude = tous les slots distincts approuvés. Le
-- niveau de preuve est porté PAR slot (Règle de gate §5 cond. 2 évaluée par slot). Tenant-scopé.
--
-- slot_state : 0=Pending, 1=Approved, 2=Rejected (ApprovalSlotState).
-- proof_level : 0=None, 1=Recorded, 2=SES, 4=AES, 8=QES (SignatureLevel).
CREATE TABLE IF NOT EXISTS documentapproval.document_validation_slots (
    company_id         uuid        NOT NULL,
    document_id        uuid        NOT NULL,
    validation_purpose int         NOT NULL,
    attempt            int         NOT NULL,
    signer_id          text        NOT NULL,
    slot_state         int         NOT NULL,
    proof_level        int         NOT NULL,
    proof_id           text,
    updated_at         timestamptz,

    CONSTRAINT pk_document_validation_slots
        PRIMARY KEY (company_id, document_id, validation_purpose, attempt, signer_id)
);

CREATE INDEX IF NOT EXISTS ix_document_validation_slots_attempt
    ON documentapproval.document_validation_slots (company_id, document_id, validation_purpose, attempt);
