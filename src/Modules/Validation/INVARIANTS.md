# Validation Module — Invariants

| ID | Rule | Enforcement |
|---|---|---|
| INV-VALIDATION-001 | Une règle qui lève une exception ne fait JAMAIS passer le document : le pipeline produit une anomalie BLOQUANTE `RULE_CRASHED` (jamais de règle silencieuse — CLAUDE.md n°3, F04) | `ValidationPipeline.ValidateAsync` (try/catch → `CreateRuleCrashedIssue`, sévérité `Blocking`) ; testé `ValidationPipelineTests` |
| INV-VALIDATION-002 | Une règle en échec n'interrompt pas les autres : toutes les règles enregistrées s'exécutent, les anomalies sont agrégées | `ValidationPipeline.ValidateAsync` (boucle `foreach` avec catch par règle) ; testé `ValidationPipelineTests` |
| INV-VALIDATION-003 | Un document est `IsValid` si et seulement s'il ne porte AUCUNE anomalie bloquante ; une alerte (Warning) n'invalide pas (F04 §3.3) | `ValidationResult.IsValid` / `HasBlockingIssue` ; testé `ValidationResultTests`, `ValidationPipelineTests` |
| INV-VALIDATION-004 | Toute anomalie a un code stable non vide et un message opérateur en français non vide (CLAUDE.md n°12) | `ValidationIssue` (constructeur, `ArgumentException` sur code/message vide) ; testé `ValidationResultTests` |
| INV-VALIDATION-005 | Une annulation RÉELLE (token demandé) est propagée, jamais avalée ni convertie en `RULE_CRASHED` ; une `OperationCanceledException` PARASITE (timeout aval, token non annulé) est traitée comme un crash de règle (`RULE_CRASHED`), jamais laissée échapper | `ValidationPipeline.ValidateAsync` (`ThrowIfCancellationRequested` + `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) → throw`, sinon `RULE_CRASHED`) ; testé `ValidationPipelineTests` |
| INV-VALIDATION-006 | Tout document validé est scopé à un tenant résolu (clé d'isolation, CLAUDE.md n°9) | `DocumentValidationContext` (constructeur rejette `Guid.Empty`) ; testé `DocumentValidationContextTests` |
| INV-VALIDATION-007 | Le module ne corrige ni ne normalise jamais les données : il détecte seulement (frontière `Validation`, `module-rules.md` §2) | Aucune écriture sur le document pivot (entrée en lecture seule) ; règle `IDocumentRule` retourne des anomalies, jamais un document modifié |
