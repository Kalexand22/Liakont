-- Seuils d'alerte de supervision du tenant (F12-A §6). 1 ligne par company_id. Valeurs par défaut
-- produit posées à l'écriture par le domaine (F12 §5.2).
CREATE TABLE IF NOT EXISTS tenantsettings.alert_thresholds (
    id                       uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id               uuid        NOT NULL,
    agent_silent_hours       int         NOT NULL,
    missed_run_hours         int         NOT NULL,
    push_queue_max_items     int         NOT NULL,
    push_queue_max_age_hours int         NOT NULL,
    blocked_documents_days   int         NOT NULL,
    pa_rejections_days       int         NOT NULL,
    alert_tenant_contact     boolean     NOT NULL DEFAULT false,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz,

    CONSTRAINT pk_alert_thresholds PRIMARY KEY (id),
    CONSTRAINT uq_alert_thresholds_company UNIQUE (company_id)
);
