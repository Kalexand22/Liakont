-- Matrice de routage des alertes du tenant (F12 §5.3.1, FIX212). TABLE DE PARAMÉTRAGE MUTABLE
-- (≠ piste d'audit) : 0..N lignes par company_id, remplacées en bloc à l'enregistrement (le modèle
-- simple — opérateur = toutes, contact = critiques opt-in — reste le défaut zéro-configuration quand
-- la table est vide pour le tenant). Une entrée route le destinataire CÔTÉ TENANT par règle (F12 §5.2)
-- et/ou par gravité ; `recipients` est la liste d'e-mails. Aucune règle fiscale, aucune donnée client.
CREATE TABLE IF NOT EXISTS tenantsettings.alert_routing_rules (
    id          uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id  uuid        NOT NULL,
    rule_key    text,
    severity    text,
    recipients  text[]      NOT NULL,
    ordinal     int         NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_alert_routing_rules PRIMARY KEY (id),
    -- Au moins un sélecteur (par règle OU par gravité) : une entrée « attrape-tout » silencieuse est interdite.
    CONSTRAINT ck_alert_routing_rules_selector CHECK (rule_key IS NOT NULL OR severity IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_alert_routing_rules_company
    ON tenantsettings.alert_routing_rules (company_id, ordinal);
