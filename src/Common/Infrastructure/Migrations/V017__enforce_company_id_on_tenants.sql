-- RLM02 (ADR-0021 §2c) : rendre `outbox.tenants.company_id` STRUCTURELLEMENT fiable pour la
-- résolution du tenant pilotée par le jeton. V016 l'a ajouté nullable et non-UNIQUE (unicité
-- seulement probabiliste via Guid.NewGuid) ; ici on backfille le tenant historique `default`
-- (NULL depuis V016) vers le company_id canonique de dev, puis on impose NOT NULL + UNIQUE.
-- Sans NOT NULL, un tenant resté NULL casserait SILENCIEUSEMENT la résolution ; sans UNIQUE, deux
-- tenants partageant la valeur la rendraient ambiguë.
--
-- AUTO-GARDÉE par `IF EXISTS (outbox.tenants)` : la table de registre n'existe QUE dans la base
-- SYSTÈME (créée par V008, system-only). Sur une base TENANT — où ce script s'exécute aussi, n'étant
-- PAS dans TenantProvisioningService.SystemOnlyMigrationPrefixes — le bloc est un no-op. Ce
-- garde-fou évite de modifier ce fichier socle vendored épinglé (la dérive socle est le périmètre
-- de RLM04) tout en restant correct sur les deux types de base.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'outbox' AND table_name = 'tenants'
    ) THEN
        -- Backfill du SEUL tenant historique `default` (cf. ADR-0021 §2c). On ne devine aucun
        -- company_id pour un autre tenant : si un autre tenant est resté NULL, le SET NOT NULL
        -- ci-dessous échoue volontairement (fail-closed) plutôt que d'inventer une valeur.
        UPDATE outbox.tenants
           SET company_id = '00000000-0000-4000-a000-000000000001'
         WHERE id = 'default' AND company_id IS NULL;

        ALTER TABLE outbox.tenants ALTER COLUMN company_id SET NOT NULL;
        ALTER TABLE outbox.tenants ADD CONSTRAINT uq_tenants_company_id UNIQUE (company_id);
    END IF;
END $$;
