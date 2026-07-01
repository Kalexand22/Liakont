-- Configuration d'envoi d'emails d'INSTANCE (ADR-0039). Ligne SINGLETON en base système (le store cible
-- exclusivement ISystemConnectionFactory) : un opérateur d'instance renseigne l'envoi d'emails (SMTP basic,
-- Gmail ou Office 365 XOAUTH2), secrets CHIFFRÉS au repos (Data Protection, jamais en clair — CLAUDE.md n°10).
--
-- NOTE mécanique de migration (miroir de V001__create_fleet_schema) : le runner applique les scripts d'un
-- module À LA FOIS sur la base système (MigrationRunner au démarrage) ET sur chaque base tenant
-- (TenantProvisioningService). Cette table est donc aussi créée, VIDE et inutilisée, dans les bases tenant —
-- aucune donnée tenant n'y est jamais écrite (le store cible exclusivement la base système). Sans effet
-- fonctionnel ni fuite cross-tenant. L'instance-level vient du STORE, pas de la migration.
--
-- singleton_id : booléen à PK + CHECK = true → une seule ligne possible (upsert ON CONFLICT (singleton_id)).
-- encrypted_* : ciphertext Data Protection (mot de passe SMTP, client_secret, refresh_token OAuth2). NULL = non saisi.
-- oauth_client_id / oauth_tenant_id : identifiants d'application/annuaire EN CLAIR (non-secrets, ADR-0039 §3 —
--   surtout pas de colonne « encrypted_* » sans chiffrement réel, trompeuse).
CREATE TABLE IF NOT EXISTS fleet.instance_email_config (
    singleton_id                    boolean     NOT NULL DEFAULT true,
    kind                            text        NOT NULL,
    host                            text        NOT NULL,
    port                            integer     NOT NULL,
    use_starttls                    boolean     NOT NULL,
    from_address                    text        NOT NULL,
    from_name                       text        NOT NULL,
    username                        text        NOT NULL,
    encrypted_smtp_password         text,
    oauth_client_id                 text,
    oauth_tenant_id                 text,
    encrypted_oauth_client_secret   text,
    encrypted_oauth_refresh_token   text,
    enabled                         boolean     NOT NULL,
    updated_at_utc                  timestamptz NOT NULL,

    CONSTRAINT pk_instance_email_config PRIMARY KEY (singleton_id),
    CONSTRAINT ck_instance_email_config_singleton CHECK (singleton_id = true)
);
