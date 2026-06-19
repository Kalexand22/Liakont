-- Index partiel au service du balayage de bascule tacite (MND04, ADR-0024 §4) : le job énumère, par tenant,
-- les acceptations EN ATTENTE dont l'échéance de bascule tacite est échue (state = 0 = PendingAcceptance,
-- deadline_utc NON NULL ≡ mandat écrit ET délai non null, deadline_utc <= now). L'index ne couvre que ces
-- lignes (PendingAcceptance avec échéance) : il reste petit (seules les acceptations en attente d'une
-- éventuelle bascule), conforme au dimensionnement attendu (F01-F02 R8). Additif, sans impact sur les
-- lectures (company_id, document_id) existantes.
CREATE INDEX IF NOT EXISTS ix_self_billed_acceptances_due_tacit
    ON mandats.self_billed_acceptances (deadline_utc)
    WHERE state = 0 AND deadline_utc IS NOT NULL;
