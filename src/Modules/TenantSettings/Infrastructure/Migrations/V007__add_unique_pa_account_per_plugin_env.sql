-- Unicité d'un compte PA par (tenant, type de plug-in, environnement) : rend l'import idempotent
-- et empêche les doublons (F12-A §4). Un tenant a au plus un compte Staging et un compte Production
-- par type de plug-in.
CREATE UNIQUE INDEX uq_pa_accounts_company_plugin_env
    ON tenantsettings.pa_accounts (company_id, plugin_type, environment);
