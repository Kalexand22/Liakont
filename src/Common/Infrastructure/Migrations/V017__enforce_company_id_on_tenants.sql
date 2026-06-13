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
DECLARE
    tenants_sans_societe text;
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'outbox' AND table_name = 'tenants'
    ) THEN
        -- Backfill du SEUL tenant historique `default` (cf. ADR-0021 §2c). On ne devine aucun
        -- company_id pour un autre tenant.
        UPDATE outbox.tenants
           SET company_id = '00000000-0000-4000-a000-000000000001'
         WHERE id = 'default' AND company_id IS NULL;

        -- Fail-closed EXPLICITE : si un AUTRE tenant reste sans company_id (provisionné avant V016,
        -- ou `default` réel d'un appliance déjà migré), échouer avec un message opérateur FRANÇAIS
        -- nommant les tenants + l'action corrective (CLAUDE.md n°12), plutôt qu'une not_null_violation
        -- brute illisible. Aucune société n'est inventée.
        SELECT string_agg(id, ', ' ORDER BY id) INTO tenants_sans_societe
          FROM outbox.tenants WHERE company_id IS NULL;
        IF tenants_sans_societe IS NOT NULL THEN
            RAISE EXCEPTION 'RLM02/V017 : mise à niveau impossible — tenant(s) sans company_id : %. Action corrective : renseigner leur société (company_id) via le provisioning ou la console avant de rejouer la migration (aucune valeur ne doit être inventée).', tenants_sans_societe;
        END IF;

        ALTER TABLE outbox.tenants ALTER COLUMN company_id SET NOT NULL;
        ALTER TABLE outbox.tenants ADD CONSTRAINT uq_tenants_company_id UNIQUE (company_id);
    END IF;
END $$;
