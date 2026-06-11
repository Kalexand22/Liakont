-- FIX305 — Piste d'audit lisible : restituer l'acteur par son NOM, pas par un GUID brut.
-- Ajoute le nom d'affichage de l'opérateur CAPTURÉ AU MOMENT de l'événement (même motif que
-- mapping_change_log.operator_name du journal TVA) : la trace reste lisible même si le compte est
-- renommé/supprimé plus tard (jamais de résolution différée). operator_identity (GUID) reste l'identité
-- d'audit stable, gardée comme détail technique. NULL pour un événement système, ou pour un événement
-- ANTÉRIEUR à FIX305 sans nom persisté (la restitution retombe alors sur le GUID — repli propre).
--
-- APPEND-ONLY préservé (CLAUDE.md n°4) : ajouter une colonne (DDL) ne modifie ni ne supprime aucune
-- entrée existante ; le trigger trg_document_events_append_only (ligne) ne se déclenche pas sur ALTER
-- TABLE. Les lignes antérieures gardent operator_name = NULL (aucune migration de données, aucun update).
ALTER TABLE documents.document_events
    ADD COLUMN operator_name text;
