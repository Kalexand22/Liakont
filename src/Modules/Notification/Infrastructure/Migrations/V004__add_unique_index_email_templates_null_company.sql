-- PostgreSQL UNIQUE constraints treat NULLs as distinct values,
-- so (code, language_code, company_id) does not enforce uniqueness when company_id IS NULL.
-- Add a partial unique index to cover the global (NULL company) case.
CREATE UNIQUE INDEX IF NOT EXISTS uq_email_templates_code_lang_global
    ON notification.email_templates (code, language_code)
    WHERE company_id IS NULL;
