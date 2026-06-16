-- Agrégat de validation de document (ADR-0028 §2/§3). État MUTABLE (écrasé à chaque transition) ; la
-- traçabilité vient du journal append-only document_approval_log (V004). Clé
-- (company_id, document_id, validation_purpose, attempt) tenant-scopée (CLAUDE.md n°9). PAS de FK vers un
-- schéma documents : le document vit dans un autre module (frontière par Contracts, jamais un couplage de
-- schéma cross-module).
--
-- validation_purpose : 0=SelfBilledAcceptance, 1=MandateSignature, 2=CreditNoteAcceptance,
--   3=ProgressStatementApproval, 4=MultiTierAccountingApproval, 5=MultiPartySignature (ValidationPurpose).
-- state : 0=PendingValidation, 1=ValidationInProgress, 2=Validated, 3=TacitlyValidated, 4=Rejected,
--   5=Contested, 6=Expired (machine fermée, INV-APPROVAL-2). NON terminaux = 0,1.
-- proof_level : 0=None, 1=Recorded, 2=SES, 4=AES, 8=QES (SignatureLevel, niveau de la preuve mono-partie).
-- express_acceptance_recorded : acceptation expresse explicite enregistrée (condition 3 du gate self-billing).
-- deadline_utc : échéance de bascule tacite / timeout — NULL = bascule tacite impossible.
CREATE TABLE IF NOT EXISTS documentapproval.document_validations (
    company_id                  uuid        NOT NULL,
    document_id                 uuid        NOT NULL,
    validation_purpose          int         NOT NULL,
    attempt                     int         NOT NULL,
    state                       int         NOT NULL,
    proof_level                 int         NOT NULL,
    express_acceptance_recorded boolean     NOT NULL DEFAULT false,
    deadline_utc                timestamptz,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz,

    CONSTRAINT pk_document_validations PRIMARY KEY (company_id, document_id, validation_purpose, attempt)
);

CREATE INDEX IF NOT EXISTS ix_document_validations_company
    ON documentapproval.document_validations (company_id);

-- INV-APPROVAL-5 : au plus UNE tentative NON terminale par (company_id, document_id, validation_purpose).
-- Index unique PARTIEL filtré sur les états non terminaux (0=PendingValidation, 1=ValidationInProgress) ;
-- tenant-scopé comme la clé d'agrégat. C'est la garde de base qui ferme la course « deux tentatives actives
-- en parallèle » (la garde anti-race applicative — l'attempt N doit être un échec terminal sous verrou —
-- s'appuie dessus).
CREATE UNIQUE INDEX IF NOT EXISTS ux_document_validations_active_attempt
    ON documentapproval.document_validations (company_id, document_id, validation_purpose)
    WHERE state IN (0, 1);
