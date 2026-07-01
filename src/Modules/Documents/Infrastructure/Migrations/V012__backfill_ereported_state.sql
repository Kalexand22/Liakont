-- BUG-24 / ADR-0037 — Backfill de l'état « E-reporté » (voie e-reporting B2C AGRÉGÉE).
-- Avant ADR-0037, un document inclus dans une déclaration d'e-reporting B2C ACCEPTÉE restait figé en
-- « ReadyToSend » (l'émetteur ne transitionnait pas l'état ; le « reporté » ne vivait que dans le journal
-- d'émission pipeline.b2c_margin_emissions). Cette migration RÉTRO-CORRIGE ces documents vers « EReported »
-- afin que la LISTE, les COMPTEURS et la FICHE lisent le même état persisté (fin de l'overlay read-time).
--
-- documents.documents N'EST PAS une table WORM append-only (contrairement à document_events / archive_entries /
-- reporting_piece_links) : un UPDATE de « state » y est le mécanisme NORMAL de la machine à états — donc légitime.
-- L'audit de l'émission de ces documents existe déjà dans pipeline.b2c_margin_emissions (status='Issued') ; cette
-- migration est une réconciliation d'état one-shot, sa trace est la migration elle-même. Les documents e-reportés
-- APRÈS ADR-0037 reçoivent, eux, un événement d'audit DocumentEReported via IDocumentLifecycle.MarkEReportedAsync.
--
-- Même base (database-per-tenant), schémas distincts documents.* / pipeline.*. La garde to_regclass rend le
-- backfill SÛR quel que soit l'ordre d'exécution inter-modules : si le schéma pipeline n'est pas encore migré,
-- le backfill ne s'applique simplement pas (aucun document reporté ne peut alors exister).
DO $$
BEGIN
    IF to_regclass('pipeline.b2c_margin_emissions') IS NOT NULL THEN
        UPDATE documents.documents d
           SET state = 'EReported'
         WHERE d.state = 'ReadyToSend'
           AND EXISTS (
               SELECT 1
                 FROM pipeline.b2c_margin_emissions e
                WHERE e.document_id = d.id
                  AND e.status = 'Issued');
    END IF;
END $$;
