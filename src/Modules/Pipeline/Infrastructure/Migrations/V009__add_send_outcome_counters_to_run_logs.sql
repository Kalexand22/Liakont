-- RBF07 — affiner les compteurs d'issue d'un run SEND pour un signalement opérateur exact (CLAUDE.md n°3/12).
-- Le journal ne portait que processed/succeeded/failed : impossible côté lecture (PipelineRunLogDto) de savoir
-- pourquoi un run « 0 émis » n'a rien envoyé. On distingue désormais TROIS sous-cas des non-émis/non-échoués :
--   * documents_deferred = transitoire VRAI (contenu pas encore stagé, dépôt asynchrone accepté en cours
--     d'émission) : repris au prochain cycle SANS action opérateur → présentable « en cours d'émission ».
--   * documents_held     = HOLD exigeant une ACTION OPÉRATEUR (SIREN émetteur non publié, table TVA non reposée) :
--     JAMAIS « en cours d'émission » (succès silencieux) → résultat signalé avec action corrective.
--   * (les « ignorés » restent dérivés : processed - succeeded - failed - deferred - held).
-- Sans documents_held, un run tout-HOLD passait à tort pour « en cours d'émission » (faux-vert opérateur).
-- Colonnes ADDITIVES (NOT NULL DEFAULT 0) : les lignes existantes reçoivent 0, aucune réécriture rétroactive.
ALTER TABLE pipeline.run_logs
    ADD COLUMN IF NOT EXISTS documents_deferred int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS documents_held int NOT NULL DEFAULT 0;
