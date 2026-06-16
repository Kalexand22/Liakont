-- Agrégat d'acceptation de l'autofacturation 389 (ADR-0024 §1/§2, F15 §2.2/§2.3). Orthogonal à l'émission :
-- on n'étend PAS DocumentState (INV-ACCEPT-1). État MUTABLE (écrasé à chaque transition) ; la traçabilité
-- vient du journal append-only self_billed_acceptance_log (V006). Clé (company_id, document_id) tenant-scopée
-- (CLAUDE.md n°9, INV-MANDATS-1). PAS de FK vers un schéma documents : le document vit dans un autre module
-- (frontière par Contracts, jamais un couplage de schéma cross-module — INV-MANDATS-2).
--
-- state : 0=PendingAcceptance, 1=Accepted, 2=TacitlyAccepted, 3=Contested (machine fermée, INV-ACCEPT-4).
-- allocated_number : BT-1 fiscal alloué par mandant (MND05/ADR-0025) — NULL tant que non alloué (jamais
-- alloué ici en MND02). pending_since : entrée en attente. deadline_utc : pending_since + délai de
-- contestation (calculé par l'appelant) — NULL = bascule tacite impossible (mandat tacite ou délai null).
CREATE TABLE IF NOT EXISTS mandats.self_billed_acceptances (
    company_id       uuid        NOT NULL,
    document_id      uuid        NOT NULL,
    state            int         NOT NULL,
    allocated_number text,
    pending_since    timestamptz NOT NULL,
    deadline_utc     timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz,

    CONSTRAINT pk_self_billed_acceptances PRIMARY KEY (company_id, document_id)
);

CREATE INDEX IF NOT EXISTS ix_self_billed_acceptances_company
    ON mandats.self_billed_acceptances (company_id);
