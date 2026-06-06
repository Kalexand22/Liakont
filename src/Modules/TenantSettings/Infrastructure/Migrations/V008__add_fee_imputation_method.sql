-- Méthode d'imputation de la part frais pour l'e-reporting de paiement (F09 §5.2, PIP03a / D-d).
-- Nullable : null = décision de l'expert-comptable en attente = e-reporting de paiement suspendu
-- (jamais de méthode par défaut — CLAUDE.md n°2). Persistée par NOM d'énumération est volontairement
-- évitée ici : on stocke l'entier de l'énumération (cohérent avec operation_category, V003).
ALTER TABLE tenantsettings.fiscal_settings
    ADD COLUMN IF NOT EXISTS fee_imputation_method int;
