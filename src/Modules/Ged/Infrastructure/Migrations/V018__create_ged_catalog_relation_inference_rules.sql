-- Règles d'inférence/héritage du graphe entité↔entité GED (F19 §10, item GED24 — fast-follow HORS gate).
-- Config VIVANTE (mutable, base DU TENANT — isolation = la connexion, F19 §3.2 ; aucune colonne tenant_id, comme
-- axis_definitions / ged_mapping_profiles). Déclare, par `relation_kind` métier (paramétrage tenant, JAMAIS un
-- vocabulaire en dur côté plateforme — règle 7 / INV-GED-12), le MODE de dérivation et sa BORNE DE PROFONDEUR.
--
-- Deux modes GÉNÉRIQUES (agnostiques du métier) :
--   • 'transitive'   → fermeture transitive d'un genre : A─k─▶B ─k─▶C  ⇒  A─k─▶C (relation_type='inferred').
--   • 'hierarchical' → héritage le long d'un genre parent-enfant : A─h─▶P (P = parent de A) et P─k─▶C (k≠h)
--                       ⇒ A hérite  A─k─▶C (relation_type='inherited').
--
-- BORNE ANTI-DoS (F19 §6.4 « borne de profondeur obligatoire, paramètre tenant, jamais infinie ») : `max_depth`
-- est un PARAMÈTRE TENANT, plafonné par une borne dure PRODUIT [1..8] (anti-DoS, PAS une règle métier ; miroir de
-- la constante Domain RelationInferenceRule.MaxAllowedDepth). Le SUBSTRAT dérivé est toujours les relations
-- ASSERTÉES ('direct'/'extracted'), jamais les dérivées → la fermeture CONVERGE (idempotente, bornée).
--
-- MUTABLE (config opérationnelle, PAS une piste d'audit) : pas de trigger append-only ici — la valeur PROBANTE
-- est le graphe entity_relations lui-même (append-only WORM, INV-GED-02) où atterrissent les relations dérivées.

CREATE TABLE IF NOT EXISTS ged_catalog.relation_inference_rules (
    id             uuid        NOT NULL DEFAULT gen_random_uuid(),
    relation_kind  text        NOT NULL,                        -- genre métier ciblé (paramétrage tenant, jamais en dur)
    mode           text        NOT NULL,                        -- 'transitive' (inférence) | 'hierarchical' (héritage)
    max_depth      integer     NOT NULL,                        -- borne de profondeur (paramètre tenant, plafond dur [1..8])
    is_active      boolean     NOT NULL DEFAULT true,           -- règle active (une règle inactive n'est jamais appliquée)
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz,
    CONSTRAINT pk_rir PRIMARY KEY (id),
    -- Au plus UNE règle par (genre, mode) : le moteur applique sans ambiguïté.
    CONSTRAINT uq_rir_kind_mode UNIQUE (relation_kind, mode),
    -- Vocabulaire TECHNIQUE fermé (générique — aucun métier), miroir de RelationInferenceMode côté Domain.
    CONSTRAINT ck_rir_mode CHECK (mode IN ('transitive', 'hierarchical')),
    -- Borne dure anti-DoS (F19 §6.4) — miroir de RelationInferenceRule.MaxAllowedDepth.
    CONSTRAINT ck_rir_max_depth CHECK (max_depth BETWEEN 1 AND 8)
);

CREATE INDEX IF NOT EXISTS ix_rir_active_kind
    ON ged_catalog.relation_inference_rules (relation_kind) WHERE is_active = true;
