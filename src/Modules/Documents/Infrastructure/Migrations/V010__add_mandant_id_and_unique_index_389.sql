-- Re-clé anti-doublon F06 §4 pour l'autofacturation (BT-3 = 389) — item MND06, ADR-0025 §6 (F06 §4 amendé),
-- F15 §3.2/§3.3. En 389 le « supplier » fiscal est le MANDANT (BT-30 = SIREN du mandant), pas l'émetteur
-- matériel : la clé fonctionnelle bascule de (supplier_siren, document_number) vers
-- (mandant_id, document_number), où document_number est le BT-1 FISCAL ALLOUÉ par mandant (MND05), pas le
-- numéro brut de la source (qui n'alimente que l'idempotence interne via source_reference — ADR-0025 §1).
--
-- mandant_id : NULL pour tout document NON auto-facturé (cas général → clé historique INCHANGÉE, aucune
-- régression non-389). Renseigné à l'émission d'un 389. ADDITIF : aucune contrainte NOT NULL, aucune valeur
-- par défaut implicite (blueprint §8). Aucune FK vers le schéma `mandats` : la frontière entre modules passe
-- par les Contracts (CLAUDE.md n°6/14), pas par un couplage de schéma — l'intégrité référentielle mandant↔document
-- est portée par l'allocation (mandats.mandat_number_allocations) et la garde d'émission, côté module Mandats.
ALTER TABLE documents.documents
    ADD COLUMN IF NOT EXISTS mandant_id uuid;

-- INDEX D'UNICITÉ 389 (INV-BT1-6, ADR-0025 §6) — la bascule anti-doublon est ATOMIQUE CÔTÉ BASE, jamais une
-- « neutralisation du SIREN » applicative (qui ouvrirait une fenêtre de doublon sur un 389 en attente
-- ré-extrait — alternative rejetée par ADR-0025). L'index est PARTIEL sur l'état `Issued` : il garantit qu'un
-- couple (mandant_id, document_number = BT-1 fiscal) ne peut être ÉMIS qu'UNE fois (INV-BT1-5 « même mandant +
-- ré-extraction → Blocked »), tout en PRÉSERVANT la mécanique de remplacement F06 §4.3 (un 389 RejectedByPa /
-- Superseded coexiste avec son remplaçant — comme l'index NON unique des non-389, INV-DOCUMENTS-006). Deux 389
-- de même numéro mais mandants DIFFÉRENTS restent émissibles (séquences distinctes) : la clé inclut mandant_id.
-- Cohérent avec le contrôle d'unicité bloquant de la plateforme (BT-1, BT-2, BT-30 = SIREN mandant ; Annexe 7
-- G1.42/G1.45, ADR-0025 §7). L'index historique (supplier_siren, document_number) de V002 reste pour les non-389.
CREATE UNIQUE INDEX IF NOT EXISTS uq_documents_mandant_number_issued_389
    ON documents.documents (mandant_id, document_number)
    WHERE mandant_id IS NOT NULL AND state = 'Issued';
