-- Alertes de supervision (F12 §5, item SUP01a). Une ligne par déclenchement d'une règle pour ce tenant.
-- tenant_id : slug du tenant. La supervision est l'EXCEPTION documentée à « aucune colonne de tenant »
-- (module-rules §6) : la base reste per-tenant (la connexion = le tenant), mais l'alerte se nomme son
-- tenant pour que le dashboard d'instance (SUP02, lecture cross-tenant) l'étiquette sans ambiguïté.
-- severity : libellé textuel (AlertSeverity : Warning / Critical) — robuste à un renumérotage.
-- resolved_utc : NULL tant que l'alerte est active ; renseigné par l'AUTO-RÉSOLUTION quand la condition
-- disparaît. acknowledged_by / acknowledged_utc : prise en charge opérateur (n'affecte pas la résolution).
-- Table MUTABLE (résolution + acquittement) : état opérationnel, PAS une table d'audit append-only.
CREATE TABLE IF NOT EXISTS supervision.alerts (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id        text        NOT NULL,
    rule_key         text        NOT NULL,
    severity         text        NOT NULL,
    detail           text,
    triggered_utc    timestamptz NOT NULL DEFAULT now(),
    resolved_utc     timestamptz,
    acknowledged_by  text,
    acknowledged_utc timestamptz,

    CONSTRAINT pk_supervision_alerts PRIMARY KEY (id)
);

-- Anti-bruit : AU PLUS UNE alerte active (non résolue) par règle. Le moteur vérifie avant d'insérer ;
-- cet index unique partiel ferme la course (deux cycles concurrents) au niveau base — l'insertion
-- concurrente échoue plutôt que de créer un doublon actif (l'INSERT applicatif est ON CONFLICT DO NOTHING).
CREATE UNIQUE INDEX IF NOT EXISTS ux_supervision_alerts_active_rule
    ON supervision.alerts (rule_key)
    WHERE resolved_utc IS NULL;

-- File des alertes actives (dashboard), déclenchement le plus récent d'abord.
CREATE INDEX IF NOT EXISTS ix_supervision_alerts_active
    ON supervision.alerts (triggered_utc DESC)
    WHERE resolved_utc IS NULL;

-- Historique récent (actives + résolues), déclenchement le plus récent d'abord.
CREATE INDEX IF NOT EXISTS ix_supervision_alerts_recent
    ON supervision.alerts (triggered_utc DESC);
