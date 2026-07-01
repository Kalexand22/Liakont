-- Démo GED « ventes aux enchères » — PARAMÉTRAGE TENANT FICTIF (GED10, F19 §10/§11 D12).
--
-- Règle 7 (produit générique) : AUCUN vocabulaire métier (lot, vente, acheteur, bordereau…) ne vit dans le code
-- (src/Modules/Ged/**) — il vit ICI, en déploiement. Ce fichier prouve la GÉNÉRICITÉ PAR CONFIGURATION : les mêmes
-- tables ged_catalog / ged_index portent ce métier « enchères » sans UN SEUL ALTER TABLE. La démo est
-- INTÉGRALEMENT reconstructible depuis ce fichier (psql -f …/ged-catalog.sql) — jamais en touchant le code.
--
-- Base DU TENANT (schémas ged_catalog / ged_index déjà migrés — isolation = la connexion, F19 §3.2). Idempotent.
-- Données FICTIVES (aucun SIREN/tenant réel — CLAUDE.md n°7).

-- Type d'entité « acheteur » (registre polymorphe) : dédup best-effort par la clé d'identité acheteur_ref (§4.4).
INSERT INTO ged_catalog.entity_types (code, label, identity_key, is_confidential, is_active)
VALUES ('acheteur', 'Acheteur', 'acheteur_ref', false, true)
ON CONFLICT (code) DO NOTHING;

-- Axes déclarés : numero_lot (MULTI-valeur — un bordereau couvre plusieurs lots) + numero_vente (mono-valeur).
INSERT INTO ged_catalog.axis_definitions
    (code, label, data_type, value_scale, is_multi_value, is_required, is_searchable, is_facetable, unit, ordinal)
VALUES
    ('numero_lot',   'Numéro de lot',   'string', NULL, true,  true,  true, true, NULL, 1),
    ('numero_vente', 'Numéro de vente', 'string', NULL, false, false, true, true, NULL, 2)
ON CONFLICT (code) DO NOTHING;

-- Profil de mapping VALIDÉ du type de document « bordereau_acheteur » : sélecteurs JSONPath restreints désignant
-- OÙ lire la donnée dans le pivot BRUT ingéré (jamais une valeur inventée — règle 2). numero_lot est lu depuis les
-- indices d'axes multi-valeurs ($.axes[…]) ; numero_vente et l'entité acheteur depuis les champs plats ($.fields…).
INSERT INTO ged_catalog.ged_mapping_profiles
    (document_type, profile_version, storage_policy, validated_by, validated_date, axis_rules, entity_rules, relation_rules)
VALUES (
    'bordereau_acheteur', 'v1', 'WormPlusIndex', 'seed-demo', DATE '2026-07-01',
    $json$[{"AxisCode":"numero_lot","Source":"$.axes[?name=='numero_lot'].values[*]","IsRequired":true,"IsMulti":true},{"AxisCode":"numero_vente","Source":"$.fields.numero_vente","IsRequired":false,"IsMulti":false}]$json$::jsonb,
    $json$[{"EntityType":"acheteur","ExternalIdSource":"$.fields.acheteur_ref","DisplaySource":"$.fields.acheteur_nom"}]$json$::jsonb,
    $json$[]$json$::jsonb)
ON CONFLICT (document_type, profile_version) DO NOTHING;

-- Audit append-only de la naissance du profil (calqué catalog_change_log ; correction = nouvelle ligne, jamais UPDATE).
INSERT INTO ged_catalog.ged_mapping_change_log (change_type, profile_id, document_type, profile_version, after_value, operator_identity, operator_name)
SELECT 'profile_created', p.id, p.document_type, p.profile_version, to_jsonb(p.*), 'seed-demo', 'Amorce démo enchères'
FROM ged_catalog.ged_mapping_profiles p
WHERE p.document_type = 'bordereau_acheteur' AND p.profile_version = 'v1'
  AND NOT EXISTS (
      SELECT 1 FROM ged_catalog.ged_mapping_change_log l
      WHERE l.document_type = 'bordereau_acheteur' AND l.profile_version = 'v1' AND l.change_type = 'profile_created');
