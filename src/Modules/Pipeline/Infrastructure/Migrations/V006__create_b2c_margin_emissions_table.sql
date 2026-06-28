-- Journal d'émission de l'e-reporting B2C de la MARGE (flux 10.3, enchères — B4/B2C09c). Porte l'anti-doublon
-- CÔTÉ PRODUIT : l'API SuperPDP n'expose AUCUNE clé d'idempotence (pas d'external_id ; 2 POST identiques = 2
-- lignes serveur — constaté sandbox 2026-06-22). Le grain est le DOCUMENT marge (décision Karl D3 :
-- attempt-once). Une entrée Pending est écrite AVANT le POST puis l'issue après : un document portant DÉJÀ une
-- entrée n'est jamais re-tenté en auto (crash-safe — un crash après POST mais avant l'issue laisse l'entrée
-- Pending, qui exclut le document du run suivant : jamais 2 POST).
--
-- Base DU TENANT (isolation par la connexion — database-per-tenant, blueprint §7 ; pas de colonne tenant).
-- DISTINCT de la projection d'agrégation paiement (pipeline.payment_aggregations, recalculée) : ce journal est
-- une PISTE D'AUDIT IMMUABLE des transmissions B2C de la marge.
--
-- APPEND-ONLY : aucun chemin d'update/delete applicatif ET des triggers REJETTENT tout UPDATE/DELETE d'une
-- entrée existante ainsi que tout TRUNCATE (CLAUDE.md n°4 : aucune purge d'une table d'audit). La garantie est
-- au niveau base (vérifiée par test), indépendante du code applicatif.
--
-- RÈGLE MONTANTS : aucun montant n'est stocké ici (le journal trace l'AGRÉGAT, pas la forme fiscale) ;
-- content_hash = empreinte SHA-256 du contenu transmis (audit). status / category / role : libellés textuels
-- (lisibles pour un contrôle fiscal). pa_response_snapshot en texte : la réponse brute de la PA peut ne pas
-- être du JSON (embarquée telle quelle, jamais réinterprétée).
CREATE TABLE IF NOT EXISTS pipeline.b2c_margin_emissions (
    id                   uuid        NOT NULL DEFAULT gen_random_uuid(),
    document_id          uuid        NOT NULL,
    source_reference     text        NOT NULL,
    aggregate_date       date        NOT NULL,
    currency             text        NOT NULL,
    category             text        NOT NULL,
    role                 text        NOT NULL,
    content_hash         text        NOT NULL,
    status               text        NOT NULL,
    pa_emission_id       text,
    pa_response_snapshot text,
    detail               text,
    created_utc          timestamptz NOT NULL DEFAULT now(),
    -- Séquence MONOTONE d'insertion : départage déterministe de « la dernière entrée » d'un document quand deux
    -- created_utc coïncident (l'idempotence fiscale ne doit pas dépendre d'un tie-break sur l'UUID aléatoire).
    seq                  bigint      GENERATED ALWAYS AS IDENTITY,

    CONSTRAINT pk_b2c_margin_emissions PRIMARY KEY (id)
);

-- Anti-doublon attempt-once : énumération des documents DÉJÀ TENTÉS (GetHandledDocumentIds) par document_id.
CREATE INDEX IF NOT EXISTS ix_b2c_margin_emissions_document
    ON pipeline.b2c_margin_emissions (document_id);

-- Lectures par agrégat (historique d'un jour×devise) dans l'ordre d'insertion réel (created_utc puis seq).
CREATE INDEX IF NOT EXISTS ix_b2c_margin_emissions_aggregate
    ON pipeline.b2c_margin_emissions (aggregate_date, currency, created_utc, seq);

-- Garde-fou append-only : un trigger s'applique à TOUT rôle (y compris propriétaire / superuser),
-- contrairement à un REVOKE sans effet sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION pipeline.reject_b2c_margin_emission_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal d''émission e-reporting B2C de la marge (pipeline.b2c_margin_emissions) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_b2c_margin_emissions_append_only
    BEFORE UPDATE OR DELETE ON pipeline.b2c_margin_emissions
    FOR EACH ROW
    EXECUTE FUNCTION pipeline.reject_b2c_margin_emission_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du journal (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_b2c_margin_emissions_no_truncate
    BEFORE TRUNCATE ON pipeline.b2c_margin_emissions
    FOR EACH STATEMENT
    EXECUTE FUNCTION pipeline.reject_b2c_margin_emission_mutation();
