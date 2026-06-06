-- Journal des rectificatifs d'e-reporting (flux RE annule-et-remplace, PIP04 — F07-F08 §B.1). Une correction
-- d'une donnée déjà transmise (avoir tardif, erreur, altération source) ne passe PAS par un montant négatif
-- isolé : elle annule-et-remplace l'ensemble des données agrégées de la période (par SIREN + période). Chaque
-- tentative (déclaration initiale puis chaque rectificatif) ajoute une entrée : l'HISTORIQUE COMPLET est
-- conservé, l'ancien état n'est JAMAIS effacé.
--
-- Base DU TENANT (isolation par la connexion — database-per-tenant, blueprint §7). DISTINCT de la projection
-- d'agrégation (pipeline.payment_aggregations, recalculée) ET de payments.payment_aggregate_events (audit de
-- transmission de PIP03b).
--
-- APPEND-ONLY : aucun chemin d'update/delete applicatif ET des triggers REJETTENT tout UPDATE/DELETE d'une
-- entrée existante ainsi que tout TRUNCATE (CLAUDE.md n°4 : aucune purge d'une table d'audit). La garantie est
-- au niveau base (vérifiée par test), indépendante du code applicatif.
--
-- RÈGLE MONTANTS : le contenu rectifié transmis est sérialisé dans payload_snapshot (jsonb) avec montants/taux
-- en CHAÎNES invariantes — JAMAIS de float (CLAUDE.md n°1). content_hash = empreinte SHA-256 du contenu
-- (clé d'idempotence). status / flux : libellés textuels (lisibles pour un contrôle fiscal). pa_response_snapshot
-- en texte : la réponse brute de la PA peut ne pas être du JSON (embarquée telle quelle, jamais réinterprétée).
CREATE TABLE IF NOT EXISTS pipeline.report_rectifications (
    id                   uuid        NOT NULL DEFAULT gen_random_uuid(),
    flux                 text        NOT NULL,
    period_start         date        NOT NULL,
    period_end           date        NOT NULL,
    content_hash         text        NOT NULL,
    status               text        NOT NULL,
    pa_report_id         text,
    payload_snapshot     jsonb,
    pa_response_snapshot text,
    detail               text,
    created_utc          timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_report_rectifications PRIMARY KEY (id)
);

-- Lectures par période (dernière entrée / historique) et par ordre chronologique.
CREATE INDEX IF NOT EXISTS ix_report_rectifications_period
    ON pipeline.report_rectifications (flux, period_start, period_end, created_utc);

-- Garde-fou append-only : un trigger s'applique à TOUT rôle (y compris propriétaire / superuser),
-- contrairement à un REVOKE sans effet sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION pipeline.reject_report_rectification_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des rectificatifs d''e-reporting (pipeline.report_rectifications) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_report_rectifications_append_only
    BEFORE UPDATE OR DELETE ON pipeline.report_rectifications
    FOR EACH ROW
    EXECUTE FUNCTION pipeline.reject_report_rectification_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du journal (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_report_rectifications_no_truncate
    BEFORE TRUNCATE ON pipeline.report_rectifications
    FOR EACH STATEMENT
    EXECUTE FUNCTION pipeline.reject_report_rectification_mutation();
