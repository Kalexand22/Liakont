-- Référentiel de correspondance des codes pays (ADR-0038). Table cross-instance UNIVERSELLE : aucun
-- tenant_id — un alias « BEL → BE » vaut pour toute l'instance —, en base SYSTÈME (schéma reference),
-- lue via ISystemConnectionFactory. Elle normalise un code pays SOURCE non-ISO vers ISO 3166-1 alpha-2 au
-- READ-TIME plateforme (CHECK / SEND / affichage) — JAMAIS à l'ingestion : l'empreinte anti-doublon F06
-- hashe le pivot SOURCE, donc normaliser avant le hash ferait diverger l'empreinte à chaque édition du
-- référentiel (fausse « altération source ») — INV-REF-CTRY-02.
--
-- source_code : clé normalisée (MAJUSCULES, sans espaces).
-- iso_code    : cible ISO 3166-1 alpha-2 VALIDÉE à l'écriture côté application (un code non-ISO est refusé,
--               jamais stocké puis laissé bloquer en aval par BT-55) — INV-REF-CTRY-03.
-- updated_at  : dernière modification de l'état COURANT (métadonnée de paramétrage) ; la piste d'audit
--               complète (auteur, avant/après) vit dans le journal append-only (V002).
--
-- Éditable en console (gate liakont.settings). Le SEED ci-dessous = faits ISO 3166 universels constatés dans
-- les sources legacy (nations du Royaume-Uni ENG/SCO/WAL/NIR → GB, BUG-9 ; Japon JAP → JP, BUG-18) — pas de
-- donnée client, restent éditables.
CREATE SCHEMA IF NOT EXISTS reference;

CREATE TABLE IF NOT EXISTS reference.country_alias (
    source_code text        NOT NULL,
    iso_code    text        NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_country_alias PRIMARY KEY (source_code)
);

-- ON CONFLICT DO NOTHING : idempotent ET NON destructif — une valeur éditée en console n'est jamais
-- réécrasée par le seed à un redéploiement (le référentiel appartient à l'opérateur d'instance).
INSERT INTO reference.country_alias (source_code, iso_code) VALUES
    ('ENG', 'GB'),
    ('SCO', 'GB'),
    ('WAL', 'GB'),
    ('NIR', 'GB'),
    ('JAP', 'JP')
ON CONFLICT (source_code) DO NOTHING;
