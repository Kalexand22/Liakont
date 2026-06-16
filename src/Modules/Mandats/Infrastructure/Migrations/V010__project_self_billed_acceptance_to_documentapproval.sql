-- SIG05 (ADR-0024 amendé par ADR-0028, F17 §4) : BASCULE (switchover) de l'acceptation 389 vers le module
-- générique DocumentApproval. L'ÉTAT (machine fermée) et le JOURNAL append-only deviennent ceux de
-- DocumentApproval (purpose 0 = SelfBilledAcceptance) ; le module Mandats ne conserve que la COMPANION fiscale
-- (mandats.self_billed_acceptances : allocated_number/BT-1 par MND05 + pending_since).
--
-- ⚠️ Ce script ÉCRIT dans le schéma documentapproval : il EXIGE que ses tables existent (V001-V004 du module
-- DocumentApproval). DbUp applique les scripts par ordre de PROVIDER (= ordre d'enregistrement DI) ;
-- AppBootstrap enregistre DocumentApproval AVANT Mandats, et les fixtures de test appliquent les migrations
-- DocumentApproval avant Mandats — l'ordre est donc garanti.
--
-- ⚠️ Ce n'est PAS une PURGE de journal d'audit (CLAUDE.md n°4) : l'historique d'audit est INTÉGRALEMENT recopié
-- dans documentapproval.document_approval_log (append-only, mêmes garanties : double trigger UPDATE/DELETE/TRUNCATE)
-- AVANT le DROP du journal source. C'est une RELOCALISATION sans perte ; le journal canonique UNIQUE devient
-- document_approval_log (pas de double journalisation).
--
-- Correspondance d'états (SelfBilledAcceptanceState -> ValidationState) :
--   0 PendingAcceptance -> 0 PendingValidation ; 1 Accepted -> 2 Validated ;
--   2 TacitlyAccepted   -> 3 TacitlyValidated  ; 3 Contested -> 5 Contested.

-- 1. Reprise de l'ÉTAT courant : self_billed_acceptances -> document_validations (purpose 0, attempt 1).
--    proof_level : Recorded (1) pour une acceptation (expresse/tacite), None (0) sinon.
--    express_acceptance_recorded : true UNIQUEMENT pour une acceptation EXPRESSE (Accepted).
--    created_at conservé (genèse de la tentative) ; deadline_utc/updated_at conservés. ON CONFLICT DO NOTHING
--    rend la reprise idempotente vis-à-vis d'éventuelles tentatives déjà présentes.
INSERT INTO documentapproval.document_validations
    (company_id, document_id, validation_purpose, attempt, state, proof_level,
     express_acceptance_recorded, deadline_utc, created_at, updated_at)
SELECT
    a.company_id,
    a.document_id,
    0                                                            AS validation_purpose,
    1                                                            AS attempt,
    CASE a.state WHEN 0 THEN 0 WHEN 1 THEN 2 WHEN 2 THEN 3 WHEN 3 THEN 5 END AS state,
    CASE WHEN a.state IN (1, 2) THEN 1 ELSE 0 END               AS proof_level,
    (a.state = 1)                                                AS express_acceptance_recorded,
    a.deadline_utc,
    a.created_at,
    a.updated_at
FROM mandats.self_billed_acceptances a
ON CONFLICT (company_id, document_id, validation_purpose, attempt) DO NOTHING;

-- 2. Reprise de l'HISTORIQUE d'audit : self_billed_acceptance_log -> document_approval_log (purpose 0, attempt 1).
--    ORDER BY occurred_at : la colonne seq (GENERATED ALWAYS AS IDENTITY) est attribuée dans l'ordre chronologique,
--    préservant le départage déterministe d'origine. from_state NULL conservé (genèse). signer_id NULL (mono-partie).
--    occurred_at explicite (préserve l'horodatage d'origine, sans repli sur le DEFAULT now()).
INSERT INTO documentapproval.document_approval_log
    (company_id, document_id, validation_purpose, attempt, from_state, to_state, signer_id,
     operator_id, operator_name, occurred_at)
SELECT
    l.company_id,
    l.document_id,
    0                                                            AS validation_purpose,
    1                                                            AS attempt,
    CASE l.from_state WHEN 0 THEN 0 WHEN 1 THEN 2 WHEN 2 THEN 3 WHEN 3 THEN 5 ELSE NULL END AS from_state,
    CASE l.to_state   WHEN 0 THEN 0 WHEN 1 THEN 2 WHEN 2 THEN 3 WHEN 3 THEN 5 END           AS to_state,
    NULL                                                         AS signer_id,
    l.operator_id,
    l.operator_name,
    l.occurred_at
FROM mandats.self_billed_acceptance_log l
ORDER BY l.occurred_at;

-- 3. Retrait du journal source (historique relocalisé en 2). DROP TABLE retire aussi ses triggers ; la fonction
--    de garde devient orpheline -> DROP. CASCADE non requis (aucune FK n'y pointe).
DROP TABLE IF EXISTS mandats.self_billed_acceptance_log;
DROP FUNCTION IF EXISTS mandats.reject_acceptance_log_mutation();

-- 4. La companion ne porte plus l'état : DROP de l'index partiel de bascule tacite (basé sur state) PUIS des
--    colonnes d'état. L'état et l'échéance vivent désormais dans documentapproval.document_validations. Restent
--    sur self_billed_acceptances : (company_id, document_id, allocated_number, pending_since, created_at, updated_at).
DROP INDEX IF EXISTS mandats.ix_self_billed_acceptances_due_tacit;
ALTER TABLE mandats.self_billed_acceptances DROP COLUMN IF EXISTS state;
ALTER TABLE mandats.self_billed_acceptances DROP COLUMN IF EXISTS deadline_utc;
