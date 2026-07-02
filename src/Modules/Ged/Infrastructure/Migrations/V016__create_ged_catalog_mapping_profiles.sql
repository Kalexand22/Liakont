-- Moteur de mapping déclaratif GED (F19 §4.5, item GED12 ; généralisation de MappingTable/MappingRule du
-- domaine TVA). Deux tables du schéma `ged_catalog` (config vivante, base DU TENANT — isolation = la connexion,
-- F19 §3.2 ; aucune colonne tenant_id, comme axis_definitions / catalog_change_log) :
--   1) ged_mapping_profiles : profil d'un documentType, MUTABLE, versionné, VALIDÉ humainement (validated_by /
--      validated_date). Le moteur (GedMapper) n'applique QUE le profil validé ; sinon il DÉFÈRE (INV-GED-05,
--      règle 3) — jamais mapper au hasard. Les règles (axe/entité/relation) sont portées en jsonb : un profil
--      EST un document déclaratif (cf. l'exemple F19 §4.5), pas une table normalisée éditée ligne à ligne. Ce
--      n'est PAS de l'EAV d'indexation (INV-GED-01 vise les LIENS d'axe, pas la config).
--   2) ged_mapping_change_log : audit APPEND-ONLY des profils (calqué catalog_change_log §3.6 / CLAUDE.md n°4) —
--      naissance / validation / mutation tracées de façon immuable ; correction = NOUVELLE ligne, jamais
--      UPDATE/DELETE (INV-GED-02). change_type reste un texte libre (vocabulaire non figé — règle 2).

CREATE TABLE IF NOT EXISTS ged_catalog.ged_mapping_profiles (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    document_type    text        NOT NULL,                       -- type de document source (brut) couvert
    profile_version  text        NOT NULL,                       -- versionné (traçabilité de la version appliquée)
    storage_policy   text,                                       -- politique de rangement déclarée (informatif)
    validated_by     text,                                       -- identité valideur (EC/opérateur) ; NULL = NON VALIDÉ
    validated_date   date,                                       -- date de validation ; NULL = NON VALIDÉ
    axis_rules       jsonb       NOT NULL DEFAULT '[]'::jsonb,   -- règles d'axe déclaratives (sélecteur JSONPath restreint)
    entity_rules     jsonb       NOT NULL DEFAULT '[]'::jsonb,   -- règles d'entité
    relation_rules   jsonb       NOT NULL DEFAULT '[]'::jsonb,   -- règles de relation
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz,
    CONSTRAINT pk_ged_mapping_profiles PRIMARY KEY (id),
    CONSTRAINT uq_ged_mapping_profile_type_version UNIQUE (document_type, profile_version),
    -- Validation cohérente : valideur et date tous deux renseignés, ou tous deux absents (miroir Domain).
    CONSTRAINT ck_ged_mapping_profile_validation CHECK ((validated_by IS NULL) = (validated_date IS NULL))
);

-- Au plus UN profil VALIDÉ par documentType : le moteur applique celui-là sans ambiguïté (les versions
-- non validées — brouillons / historisées — coexistent librement).
CREATE UNIQUE INDEX IF NOT EXISTS uq_ged_mapping_profile_validated_type
    ON ged_catalog.ged_mapping_profiles (document_type)
    WHERE validated_by IS NOT NULL;

CREATE TABLE IF NOT EXISTS ged_catalog.ged_mapping_change_log (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    change_type       text        NOT NULL,       -- ex. 'profile_created','profile_validated','profile_updated'…
    profile_id        uuid,                        -- soft-link → ged_catalog.ged_mapping_profiles.id (sans FK)
    document_type     text,
    profile_version   text,
    before_value      jsonb,
    after_value       jsonb,
    operator_identity text,
    operator_name     text,
    occurred_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_ged_mapping_change_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_ged_mapping_change_log_profile
    ON ged_catalog.ged_mapping_change_log (profile_id) WHERE profile_id IS NOT NULL;

-- Garde-fou append-only (motif §3.6, déjà en production : documents.reject_archive_entry_mutation). Un TRIGGER
-- s'oppose à TOUT rôle (y compris propriétaire / superuser), contrairement à un REVOKE sans effet sur le
-- propriétaire de la table (CLAUDE.md n°4). Un profil se corrige par une NOUVELLE entrée d'audit, jamais par
-- mutation d'une entrée existante.
CREATE OR REPLACE FUNCTION ged_catalog.reject_ged_mapping_change_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des profils de mapping GED (ged_catalog.ged_mapping_change_log) est append-only : une entrée d''audit existante ne peut être ni modifiée ni supprimée (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_ged_mapping_change_log_append_only
    BEFORE UPDATE OR DELETE ON ged_catalog.ged_mapping_change_log
    FOR EACH ROW
    EXECUTE FUNCTION ged_catalog.reject_ged_mapping_change_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse de la piste d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_ged_mapping_change_log_no_truncate
    BEFORE TRUNCATE ON ged_catalog.ged_mapping_change_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_catalog.reject_ged_mapping_change_log_mutation();
