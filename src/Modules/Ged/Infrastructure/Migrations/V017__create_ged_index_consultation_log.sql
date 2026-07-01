-- Journal de CONSULTATION GED — trace des LECTURES du portail (F19 §6.6, ADR-0036, item GED13). DISTINCT de
-- l'audit socle de MUTATIONS : `documents.document_events` (base tenant) trace les ÉCRITURES, `audit.field_changes`
-- (base SYSTÈME partagée, via ISystemConnectionFactory) trace les mutations socle. Tracer QUI a consulté QUOI est un
-- besoin à valeur potentiellement probante (D8) que ces tables ne couvrent pas.
--
-- BASE DU TENANT, schéma `ged_index` (donnée métier tenant-scopée, comme `document_events`) : routée par
-- IConnectionFactory (la connexion résolue pour le tenant courant), JAMAIS ISystemConnectionFactory — écrire la
-- consultation d'un tenant dans l'audit socle PARTAGÉ mélangerait les lectures de TOUS les tenants (fuite
-- cross-tenant, CLAUDE.md n.9). Les cinq `action` sont fermées (CHECK en base + enum applicatif) ; `managed_document_id`
-- / `entity_id` réfèrent les managed_documents / entity_instances en SOFT-LINK logique (aucune FK cross-schéma —
-- CLAUDE.md n.9, la jointure SQL cross-schéma ged_* -> documents.* reste interdite). `correlation_id` relie une
-- recherche à ses ouvertures de documents subséquentes. `query_text` ET `detail` sont MASQUÉS/hachés côté serveur si
-- l'axe/entité ciblé est confidentiel (§6.5, anti-oracle : le journal n'est pas un canal de contournement) — le
-- masquage est porté par IConsultationAuditWriter (Ged.Infrastructure), pas par cette table.
CREATE TABLE IF NOT EXISTS ged_index.consultation_log (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    occurred_utc        timestamptz NOT NULL DEFAULT now(),
    actor_id            text        NOT NULL,                 -- identité de l'opérateur (server-side, IActorContext) ; minimisation à l'écriture (D8, RGPD)
    action              text        NOT NULL,                 -- 'search'|'view_document'|'explore_entity'|'export'|'open_archive'
    managed_document_id uuid,                                 -- -> ged_index.managed_documents.id (soft-link logique, sans FK)
    entity_id           uuid,                                 -- -> ged_index.entity_instances.id (soft-link logique, sans FK)
    query_text          text,                                 -- masqué/haché si axe confidentiel ciblé (§6.5)
    result_count        int,
    detail              jsonb,                                -- critères/facettes ; valeurs confidentielles masquées (§6.5)
    correlation_id      uuid,                                 -- relie une recherche à ses ouvertures subséquentes
    CONSTRAINT pk_consultation_log PRIMARY KEY (id),
    CONSTRAINT ck_consultation_action CHECK (action IN
        ('search', 'view_document', 'explore_entity', 'export', 'open_archive'))
);

CREATE INDEX IF NOT EXISTS ix_consultation_actor
    ON ged_index.consultation_log (actor_id, occurred_utc DESC);

-- APPEND-ONLY / WORM (CLAUDE.md n.4 : « Piste d'audit ... append-only »). COPIE EXACTE du garde-fou éprouvé en
-- production (documents.reject_archive_entry_mutation / ged_index.reject_axis_link_mutation) : une fonction de rejet
-- levée par DEUX triggers. Un TRIGGER s'oppose à TOUT rôle (y compris propriétaire / superuser), contrairement à un
-- REVOKE sans effet sur le propriétaire de la table. Aucune purge automatique (rétention/minimisation = D8 ouvert,
-- jamais un mécanisme de suppression gravé ici).
CREATE OR REPLACE FUNCTION ged_index.reject_consultation_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal de consultation GED (ged_index.consultation_log) est append-only (WORM) : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_consultation_log_append_only
    BEFORE UPDATE OR DELETE ON ged_index.consultation_log
    FOR EACH ROW
    EXECUTE FUNCTION ged_index.reject_consultation_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce vecteur
-- de purge en masse du journal (faux-vert classique — CLAUDE.md n.4).
CREATE OR REPLACE TRIGGER trg_consultation_log_no_truncate
    BEFORE TRUNCATE ON ged_index.consultation_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_index.reject_consultation_log_mutation();
