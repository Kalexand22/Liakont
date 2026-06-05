# Pipeline Module — Scenarios

Scénarios de test couverts, par niveau, référençant les invariants (`INV-PIPELINE-NNN`). PIP01a ne livre
aucun comportement de pipeline : les scénarios CHECK/SEND/SYNC arrivent avec PIP01b-d.

## Unit — `PivotCanonicalJsonReaderTests`

- **Round-trip sans perte** : pour chacun des 8 golden de DOCUMENT UNIQUE de `tests/fixtures/contrat-v1/`
  (factures + avoirs ; PAS `batch-mixte.json` ni `heartbeat.json`, qui ne sont pas des `PivotDocument`),
  `Serialize(Read(json)) == json` octet par octet (INV-PIPELINE-001).
- **Échelle décimale préservée** : un montant `120.00` relu puis re-sérialisé reste `120.00` (jamais `120`
  ni `120.0`) (INV-PIPELINE-001).
- **Optionnels et énumérations** : `Customer`/`PrepaidAmount`/`CategoryCode` absents restent absents ;
  `OperationCategory`/`CategoryCode` sont relus par nom (INV-PIPELINE-001).
- **Argument nul** : `Read(null)` lève `ArgumentNullException`.

## Unit — `RunLogTests`

- **Ouverture** : `Start` crée une exécution non clôturée, compteurs à zéro (INV-PIPELINE-006).
- **Clôture** : `Complete` renseigne la fin et les compteurs ; `IsCompleted` devient vrai (INV-PIPELINE-006).
- **Garde temporelle** : `Complete` avec une fin antérieure au début lève `ArgumentException` (INV-PIPELINE-006).
- **Garde compteurs** : un compteur négatif lève `ArgumentOutOfRangeException` (INV-PIPELINE-006).

## Integration — `PipelineRunLogQueriesIntegrationTests`

- **Migration appliquée + relecture** : la migration `pipeline.run_logs` s'applique sur PostgreSQL réel
  (Testcontainers) ; une ligne insérée est relue fidèlement par `PostgresPipelineRunQueries`, énumérations
  par nom, horodatages et compteurs préservés (INV-PIPELINE-005/007).
- **Tri et borne** : les exécutions sont retournées les plus récentes d'abord, bornées par `limit`
  (INV-PIPELINE-005).
- **Journal vide** : aucune exécution → liste vide (jamais d'erreur) (INV-PIPELINE-005).
