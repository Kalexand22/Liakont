-- Index de RECHERCHE plein-texte du schéma `ged_index` (F19 §6.1/§6.3, item GED08, ADR-0035). Table DÉRIVÉE et
-- RECONSTRUCTIBLE : c'est le FOYER UNIQUE du `search_vector` d'un document géré (managed_documents ne porte PAS de
-- search_vector, V008/INV-GED-01 — sinon double-source non réconciliable). Étant un dérivé, `document_search` peut
-- être tronqué/reconstruit (DELETE + rebuild) sans violer la règle 4 « append-only/WORM » (INV-GED-07) : il ne
-- touche NI la chaîne de hashes fiscale, NI le coffre WORM, NI un change-log append-only. Documenté ici pour éviter
-- un faux-P1 en review (§6.1). Base DU TENANT (isolation = la connexion, F19 §3.2) ; aucune colonne tenant_id.
--
-- Peuplement : PROJECTION ASYNCHRONE (event handler consommant ManagedDocumentReceivedV1, §6.1) — jamais un
-- effet de bord synchrone de l'indexation. Le vecteur agrège le titre (poids A) et les valeurs d'axes SEARCHABLES
-- et NON CONFIDENTIELS (poids B) ; les axes confidentiels sont EXCLUS du vecteur partagé au BUILD (INV-GED-10 :
-- le droit `liakont.ged.confidential` n'ouvre PAS le plein-texte en V1, asymétrie assumée §6.5).

-- Provisionnement NEUF (RL-13) — l'extension `unaccent` n'existe PAS ailleurs dans le repo ; NE PAS la présenter
-- comme « réutilisée ». Requiert le droit de création d'extension au moment de la migration (superuser en dev/CI ;
-- en déploiement, provisionner l'extension avec la base — cf. §6.3). Idempotent.
CREATE EXTENSION IF NOT EXISTS unaccent;

-- Wrapper IMMUTABLE de `unaccent` (RL-13). Le `unaccent(text)` à UN argument est STABLE (il résout le dictionnaire
-- par défaut au runtime) : inutilisable en colonne générée / expression d'index. La forme à DEUX arguments
-- `unaccent(regdictionary, text)` est IMMUTABLE ; on la fige sur le dictionnaire `unaccent` par ce wrapper, réutilisé
-- IDENTIQUEMENT au build du vecteur ET à la normalisation de la requête (sinon requête et index divergeraient).
CREATE OR REPLACE FUNCTION ged_index.immutable_unaccent(text)
    RETURNS text
    LANGUAGE sql
    IMMUTABLE
    PARALLEL SAFE
    STRICT
AS $$
    SELECT unaccent('unaccent', $1)
$$;

CREATE TABLE IF NOT EXISTS ged_index.document_search (
    managed_document_id uuid        NOT NULL,   -- → ged_index.managed_documents.id (logique, sans FK ; 1 ligne par document)
    search_vector       tsvector    NOT NULL,   -- FTS config 'french' (D11) ; titre=A, axes searchables non-confidentiels=B
    refreshed_utc       timestamptz NOT NULL DEFAULT now(),   -- dernière (re)projection ; un rebuild total réécrit toutes les lignes
    CONSTRAINT pk_document_search PRIMARY KEY (managed_document_id)
);

-- Index GIN : accès plein-texte (search_vector @@ websearch_to_tsquery('french', immutable_unaccent(@q))).
CREATE INDEX IF NOT EXISTS ix_document_search_vector ON ged_index.document_search USING gin (search_vector);
