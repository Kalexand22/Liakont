-- RTE02: Enrich email templates with category, entity_type, and template_links
ALTER TABLE notification.email_templates
    ADD COLUMN IF NOT EXISTS category       smallint    NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS entity_type    varchar(100),
    ADD COLUMN IF NOT EXISTS template_links jsonb       NOT NULL DEFAULT '[]';

-- Index for looking up templates by entity type
CREATE INDEX IF NOT EXISTS ix_email_templates_entity_type
    ON notification.email_templates (entity_type)
    WHERE entity_type IS NOT NULL;
