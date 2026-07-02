-- Démo GED « situations de travaux BTP » — PARAMÉTRAGE TENANT FICTIF (GED10, F19 §10/§11 D12).
--
-- 2ᵉ métier sur le MÊME schéma que la démo enchères, SANS un seul ALTER TABLE : le MÊME ged_index.document_axis_links
-- porte ici un montant en EUR (value_number, échelle 2) ET un avancement en % (value_number, échelle 0). C'est la
-- preuve de généricité PAR CONFIGURATION (F19 §11 D12) : le produit reste générique, le métier est du paramétrage.
--
-- Base DU TENANT (schémas ged_catalog / ged_index déjà migrés). Idempotent. Données FICTIVES (CLAUDE.md n°7).

-- Type d'entité « chantier » : dédup best-effort par la clé d'identité chantier_ref (§4.4).
INSERT INTO ged_catalog.entity_types (code, label, identity_key, is_confidential, is_active)
VALUES ('chantier', 'Chantier', 'chantier_ref', false, true)
ON CONFLICT (code) DO NOTHING;

-- Axes déclarés : numero_situation + mois (string) ; montant_ht_cumule (number, échelle 2, EUR) ; avancement_pct
-- (number, échelle 0). Les montants sont en decimal (numeric), arrondi half-up par ValueNormalizer — jamais float
-- (CLAUDE.md n°1). EUR et % coexistent sur la MÊME colonne value_number.
INSERT INTO ged_catalog.axis_definitions
    (code, label, data_type, value_scale, is_multi_value, is_required, is_searchable, is_facetable, unit, ordinal)
VALUES
    ('numero_situation',  'Numéro de situation',        'string', NULL, false, true,  true, true, NULL,  1),
    ('mois',              'Mois',                        'string', NULL, false, false, true, true, NULL,  2),
    ('montant_ht_cumule', 'Montant HT cumulé',          'number', 2,    false, false, true, true, 'EUR', 3),
    ('avancement_pct',    'Avancement (%)',             'number', 0,    false, false, true, true, '%',   4)
ON CONFLICT (code) DO NOTHING;

-- Profil de mapping VALIDÉ du type « situation_travaux » : tous les axes lus depuis les champs plats du pivot BRUT.
INSERT INTO ged_catalog.ged_mapping_profiles
    (document_type, profile_version, storage_policy, validated_by, validated_date, axis_rules, entity_rules, relation_rules)
VALUES (
    'situation_travaux', 'v1', 'WormPlusIndex', 'seed-demo', DATE '2026-07-01',
    $json$[{"AxisCode":"numero_situation","Source":"$.fields.numero_situation","IsRequired":true,"IsMulti":false},{"AxisCode":"mois","Source":"$.fields.mois","IsRequired":false,"IsMulti":false},{"AxisCode":"montant_ht_cumule","Source":"$.fields.montant_ht_cumule","IsRequired":false,"IsMulti":false},{"AxisCode":"avancement_pct","Source":"$.fields.avancement_pct","IsRequired":false,"IsMulti":false}]$json$::jsonb,
    $json$[{"EntityType":"chantier","ExternalIdSource":"$.fields.chantier_ref","DisplaySource":"$.fields.chantier_nom"}]$json$::jsonb,
    $json$[]$json$::jsonb)
ON CONFLICT (document_type, profile_version) DO NOTHING;

-- Audit append-only de la naissance du profil.
INSERT INTO ged_catalog.ged_mapping_change_log (change_type, profile_id, document_type, profile_version, after_value, operator_identity, operator_name)
SELECT 'profile_created', p.id, p.document_type, p.profile_version, to_jsonb(p.*), 'seed-demo', 'Amorce démo BTP'
FROM ged_catalog.ged_mapping_profiles p
WHERE p.document_type = 'situation_travaux' AND p.profile_version = 'v1'
  AND NOT EXISTS (
      SELECT 1 FROM ged_catalog.ged_mapping_change_log l
      WHERE l.document_type = 'situation_travaux' AND l.profile_version = 'v1' AND l.change_type = 'profile_created');
