-- Mémoire d'idempotence de l'allocation BT-1 fiscale (F15 §3.2, ADR-0025 §1/§2 — livré par MND05). L'ALLOCATION
-- EST IDEMPOTENTE SUR LA CLÉ SOURCE : un document source (source_reference, déjà au pivot et déjà hashé) se voit
-- allouer son BT-1 UNE fois et mémorisé ; toute ré-extraction RELIT le même numéro, jamais ré-alloué (INV-BT1-2).
-- L'idempotence remplace l'atomicité transverse mandats/documents (ADR-0025 §2) : un crash entre l'allocation et
-- l'écriture document est rattrapé par relecture get-or-create, sans double allocation.
--
-- allocated_value : la valeur brute consommée dans la séquence (BIGINT, jamais float — CLAUDE.md n°1 ; ordre/audit).
-- allocated_number : le BT-1 fiscal RENDU (préfixe + valeur), assigné à l'émission HORS du payload hashé (INV-BT1-1).
-- Clé d'unicité (company_id, mandant_id, source_reference) tenant-scopée (CLAUDE.md n°9, INV-BT1-4).
--
-- IMMUABLE : un BT-1 fiscal alloué pour un document source ne doit JAMAIS changer (re-numéroter un 389 émis =
-- incohérence fiscale). Des triggers REJETTENT tout UPDATE/DELETE d'une ligne ET tout TRUNCATE de la table —
-- même discipline que les journaux d'audit (CLAUDE.md n°4) ; la garantie est au niveau base, indépendante du code.
-- L'allocateur n'écrit QUE par INSERT (aucun chemin update/delete côté repository).
CREATE TABLE IF NOT EXISTS mandats.mandat_number_allocations (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id       uuid        NOT NULL,
    mandant_id       uuid        NOT NULL,
    source_reference text        NOT NULL,
    allocated_value  bigint      NOT NULL,
    allocated_number text        NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_mandat_number_allocations PRIMARY KEY (id),
    CONSTRAINT uq_mandat_number_allocations_source
        UNIQUE (company_id, mandant_id, source_reference),
    CONSTRAINT fk_mandat_number_allocations_mandant FOREIGN KEY (company_id, mandant_id)
        REFERENCES mandats.mandants (company_id, id),
    CONSTRAINT ck_mandat_number_allocations_value CHECK (allocated_value >= 1)
);

CREATE INDEX IF NOT EXISTS ix_mandat_number_allocations_company
    ON mandats.mandat_number_allocations (company_id);

-- Garde-fou d'immuabilité : aucune modification ni suppression d'un numéro fiscal alloué. Un trigger s'applique
-- à TOUT rôle (y compris le propriétaire / superuser), contrairement à un REVOKE sans effet sur le propriétaire.
CREATE OR REPLACE FUNCTION mandats.reject_number_allocation_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le registre d''allocation des numéros fiscaux (mandats.mandat_number_allocations) est immuable : un BT-1 fiscal alloué ne peut être ni modifié ni supprimé (re-numéroter un 389 émis serait une incohérence fiscale — CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_mandat_number_allocations_immutable
    BEFORE UPDATE OR DELETE ON mandats.mandat_number_allocations
    FOR EACH ROW
    EXECUTE FUNCTION mandats.reject_number_allocation_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse du registre fiscal (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_mandat_number_allocations_no_truncate
    BEFORE TRUNCATE ON mandats.mandat_number_allocations
    FOR EACH STATEMENT
    EXECUTE FUNCTION mandats.reject_number_allocation_mutation();
