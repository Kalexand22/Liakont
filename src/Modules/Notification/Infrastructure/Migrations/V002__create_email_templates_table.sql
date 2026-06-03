CREATE TABLE IF NOT EXISTS notification.email_templates (
    id               uuid    NOT NULL DEFAULT gen_random_uuid(),
    code             text    NOT NULL,
    subject_template text    NOT NULL,
    body_template    text    NOT NULL,
    language_code    char(2) NOT NULL DEFAULT 'en',
    company_id       uuid,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz,

    CONSTRAINT pk_email_templates PRIMARY KEY (id),
    CONSTRAINT uq_email_templates_code_lang_company UNIQUE (code, language_code, company_id)
);
