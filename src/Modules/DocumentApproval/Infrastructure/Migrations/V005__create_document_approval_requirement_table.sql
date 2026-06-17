-- Paramétrage TENANT du niveau de preuve eIDAS requis PAR purpose (ADR-0028 §5 cond. 2, F17 §7 ; SIG06,
-- INV-APPROVAL-4). Le niveau exigé est un CHOIX du tenant, jamais un défaut produit ni une obligation codée
-- (CLAUDE.md n°2/3). En l'ABSENCE de ligne, le code applique le défaut Recorded (aucune preuve externe requise) :
-- la table ne porte donc QUE les surcharges explicites du tenant.
--
-- Table de CONFIGURATION MUTABLE (PAS une table d'audit) : l'upsert (INSERT ... ON CONFLICT DO UPDATE) est
-- autorisé — aucun trigger append-only ici (les triggers WORM ne valent que pour document_approval_log, V004).
-- Tenant-scopée (company_id NOT NULL, CLAUDE.md n°9).
--
-- validation_purpose : 0=SelfBilledAcceptance, 1=MandateSignature, 2=CreditNoteAcceptance,
--   3=ProgressStatementApproval, 4=MultiTierAccountingApproval, 5=MultiPartySignature (ValidationPurpose).
-- required_level : 1=Recorded, 2=SES, 4=AES, 8=QES (SignatureLevel). None(0) interdit (le défaut est Recorded,
--   pas « aucune exigence ») — garde applicative au SetRequiredLevel, doublée par le CHECK ci-dessous.
CREATE TABLE IF NOT EXISTS documentapproval.document_approval_requirement (
    company_id          uuid        NOT NULL,
    validation_purpose  int         NOT NULL,
    required_level      int         NOT NULL,
    updated_at          timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_document_approval_requirement PRIMARY KEY (company_id, validation_purpose),
    CONSTRAINT ck_document_approval_requirement_level CHECK (required_level IN (1, 2, 4, 8))
);
