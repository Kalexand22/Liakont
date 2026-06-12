-- Parc d'instances de la flotte (OPS04). Une ligne par instance opérée / self-hosted, mise à jour (upsert)
-- à chaque heartbeat reçu. Strictement TECHNIQUE : aucune donnée métier d'éditeur (pas de nom de tenant,
-- pas de SIREN, pas de compteur de documents) — tenant_count est un simple entier.
-- État opérationnel MUTABLE (dernière télémétrie connue), PAS une table d'audit append-only.
-- hosting_mode / *_health : libellés textuels des énumérations (robustes à un renumérotage).
-- first_seen_utc : préservé par l'upsert (premier heartbeat reçu). notified_version : anti-rebond de la
-- notification de mise à jour (dernière version pour laquelle l'instance self-hosted a été notifiée).
CREATE TABLE IF NOT EXISTS fleet.instances (
    instance_id       text        NOT NULL,
    display_name      text        NOT NULL,
    hosting_mode      text        NOT NULL,
    version           text        NOT NULL,
    host_health       text        NOT NULL,
    database_health   text        NOT NULL,
    keycloak_health   text        NOT NULL,
    tenant_count      integer     NOT NULL,
    disk_free_bytes   bigint      NOT NULL,
    disk_total_bytes  bigint      NOT NULL,
    last_backup_utc   timestamptz,
    contact_email     text,
    first_seen_utc    timestamptz NOT NULL,
    last_seen_utc     timestamptz NOT NULL,
    notified_version  text,

    CONSTRAINT pk_fleet_instances PRIMARY KEY (instance_id)
);

-- Parc trié par dernier signe de vie (dashboard : l'instance la plus silencieuse remonte au calcul d'alerte).
CREATE INDEX IF NOT EXISTS ix_fleet_instances_last_seen
    ON fleet.instances (last_seen_utc DESC);
