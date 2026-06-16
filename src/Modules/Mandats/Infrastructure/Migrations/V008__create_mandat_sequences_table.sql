-- Séquence de numérotation fiscale BT-1 PAR MANDANT (F15 §1.4/§3, ADR-0025 §5 — livré par MND05). La loi
-- impose une séquence chronologique DISTINCTE par mandant (BOI-TVA-DECLA-30-20-20-10 §120/§130 ; Annexe 7
-- G1.42/G1.45 « racine propre au mandataire »). Clé (company_id, mandant_id) tenant-scopée (CLAUDE.md n°9,
-- INV-BT1-4). next_value en BIGINT (jamais float — CLAUDE.md n°1 ; le mandat ne porte aucun montant). prefix
-- = paramétrage tenant (seedé depuis mandants.numbering_prefix, figé sur la séquence). company_id dénormalisé
-- pour le tenant-scoping défensif (n°9). Table MUTABLE (next_value avance à chaque allocation, sous verrou
-- FOR UPDATE par mandant — l'allocateur sérialise les allocations d'un même mandant).
CREATE TABLE IF NOT EXISTS mandats.mandat_sequences (
    company_id uuid        NOT NULL,
    mandant_id uuid        NOT NULL,
    prefix     text        NOT NULL,
    next_value bigint      NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,

    CONSTRAINT pk_mandat_sequences PRIMARY KEY (company_id, mandant_id),
    -- FK COMPOSITE (company_id, mandant_id) : une séquence ne peut référencer qu'un mandant DU MÊME tenant
    -- (défense en profondeur du tenant-scoping, comme V003 fk_mandats_mandant).
    CONSTRAINT fk_mandat_sequences_mandant FOREIGN KEY (company_id, mandant_id)
        REFERENCES mandats.mandants (company_id, id),
    -- Une séquence ne démarre jamais en-dessous de 1 (numérotation positive, continuité §1.4).
    CONSTRAINT ck_mandat_sequences_next_value CHECK (next_value >= 1)
);
