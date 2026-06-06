# Pipeline Module — Scenarios

Scénarios de test couverts, par niveau, référençant les invariants (`INV-PIPELINE-NNN`). PIP01a a posé les
fondations ; **PIP01b** ajoute le CHECK ; **PIP01c** ajoute le SEND (scénarios ci-dessous). SYNC arrive
avec PIP01d.

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

## Unit — `CheckTvaMappingTests`

- **Part = Autre** : chaque requête construite porte `TvaMappingPart.Autre` ; aucune dérivation
  adjudication/frais (INV-PIPELINE-003).
- **Forme 1↔1** : une ligne avec exactement 1 code régime et 1 ventilation produit une requête ; une ligne
  multi-codes ou multi-ventilations produit un motif de blocage, aucune requête (INV-PIPELINE-008).
- **Enrichissement** : quand toutes les lignes sont mappées, la catégorie et le VATEX du mapping sont posés
  sur l'unique ventilation de chaque ligne ; montants et taux source inchangés (INV-PIPELINE-003).
- **Blocage agrégé** : une ligne non mappée ⇒ `Evaluate` retourne un blocage portant le motif moteur ; le
  pivot n'est pas enrichi (INV-PIPELINE-008).

## Unit — `DocumentReceivedConsumerTests`

- **Chemin nominal → ReadyToSend** : staging relu → mapping (table validée) → validation OK ⇒
  `MarkReadyToSendAsync(id, version)`, `RunLog` Check écrit (succès=1) (INV-PIPELINE-008/012).
- **Régime non mappé → Blocked** : un régime absent de la table ⇒ `BlockAsync` avec motif, `RunLog`
  (échec=1) (INV-PIPELINE-008/011/012).
- **Validation bloquante → Blocked** : mapping OK mais anomalie bloquante ⇒ `BlockAsync` (motifs agrégés)
  (INV-PIPELINE-008/011).
- **Garde-fou production** : compte PA actif « Production » + table non validée ⇒ tous `Blocked` avec le
  motif production ; en staging/démo (pas de compte production) la même table non validée mappe
  normalement (INV-PIPELINE-009).
- **Table absente → Blocked** : aucune table de mapping ⇒ `Blocked` avec le motif « créez la table »
  (action corrective correcte, distincte de la garde-fou « validez la table »), validation non appelée
  (INV-PIPELINE-008).
- **Staging absent = transitoire** : `StagedPayloadNotFoundException` est propagée (re-livraison), aucune
  transition, aucun `RunLog` (INV-PIPELINE-010).
- **Staging corrompu → Blocked** : `StagedPayloadIntegrityException` (empreinte ≠ attendue) ⇒ document
  `Blocked` (motif intégrité) + `RunLog` (échec), jamais dead-letter silencieux (INV-PIPELINE-013).
- **Idempotence** : un document déjà hors `Detected` ⇒ no-op (aucune transition, aucun `RunLog`)
  (INV-PIPELINE-010).
- **Tenant non configuré** : `GetCurrentCompanyId` nul ⇒ exception transitoire (retry), aucune transition
  (INV-PIPELINE-010).

## Integration — `DocumentReceivedConsumerIntegrationTests` (Testcontainers PostgreSQL)

- **CHECK bout en bout → ReadyToSend** : sur une base tenant réelle (migrations Documents + TvaMapping +
  Staging + TenantSettings + Pipeline), un document `Detected` + un pivot stagé + une table validée
  (règle `Autre`) ⇒ le consommateur fait passer le document `ReadyToSend` (version de table consignée) et
  écrit une ligne `pipeline.run_logs` (INV-PIPELINE-008/011/012).
- **Régime non mappé → Blocked** : un régime source absent de la table validée ⇒ document `Blocked`, motif
  persisté dans la piste d'audit (`DocumentEvent`), `RunLog` écrit (INV-PIPELINE-008/011).

> La garde-fou production (INV-PIPELINE-009), le staging absent transitoire et l'idempotence sont couverts
> au niveau **unitaire** (`DocumentReceivedConsumerTests`, fakes), pas en intégration : ils ne dépendent pas
> de la base PostgreSQL.

## Unit — `SendArchiveComposerTests`

- **Montants en decimal** : les lignes et totaux du rendu lisible reprennent les montants `decimal` du pivot
  à l'identique (aucun float), libellé de taux « 20 % » (INV-PIPELINE-017).
- **Ventilation TVA agrégée par taux** : deux lignes à 20 % regroupées (base + TVA sommées), une ligne à
  10 % distincte (INV-PIPELINE-017).
- **Motifs d'absence explicites** : facture PA et bordereau source absents à l'émission portent chacun un
  motif d'absence non vide (jamais une absence silencieuse).

## Unit — `SendAllFanOutHandlerTests`

- **Fan-out via le runner** : le handler du déclencheur `SendAllTrigger` exécute un `SendTenantJob` via
  `ITenantJobRunner.RunForAllTenantsAsync` — aucune boucle multi-tenant locale (INV-PIPELINE-014).

## Unit — `SendTenantJobTests`

- **Aucun compte PA actif** : aucune interrogation de la PA, aucun envoi, un `RunLog` SEND est écrit
  (INV-PIPELINE-014).
- **Diagnostic inactif → aucun envoi** : `tax_report_setting` non publié ⇒ aucune transition, Warning +
  `RunLog`, documents maintenus `ReadyToSend` (INV-PIPELINE-015).
- **Dry-run** : dénombre les `ReadyToSend`, n'appelle aucune écriture PA, ne fait avancer aucun document
  (INV-PIPELINE-020).
- **Émission → archive puis purge** : un `ReadyToSend` envoyé avec succès ⇒ `BeginSending` + `MarkIssued`,
  archive WORM appelée, purge subordonnée au WORM appelée (INV-PIPELINE-017).
- **Rejet PA → staging conservé** : un rejet ⇒ `MarkRejectedByPa`, AUCUNE purge, aucune archive
  (INV-PIPELINE-018).
- **Anti-doublon par statut** : un `Sending` portant une référence PA déjà `Issued` ⇒ `MarkIssued` SANS
  renvoyer (`GetDocumentStatusAsync` appelé, aucun nouvel envoi du numéro) (INV-PIPELINE-016).

## Integration — `SendTenantJobIntegrationTests` (Testcontainers PostgreSQL)

- **ReadyToSend → Issued, archivé, staging purgé** : sur une base tenant réelle (migrations Documents +
  TenantSettings + Staging + Pipeline + Archive), un document `ReadyToSend` + pivot stagé + compte PA actif
  publié ⇒ `Issued`, archive WORM écrite, staging purgé (paquet WORM présent) (INV-PIPELINE-017).
- **Rejet PA → staging conservé** : scénario `Rejected` ⇒ `RejectedByPa`, staging CONSERVÉ (la réponse de
  rejet en TEXTE BRUT est archivée en JSON valide) (INV-PIPELINE-018/019).
- **Issued mais WORM absent → staging conservé** : sonde WORM forcée « absente » ⇒ document `Issued` mais
  staging CONSERVÉ (purge subordonnée au paquet WORM, jamais à l'étiquette `Issued`) (INV-PIPELINE-017).
- **Anti-doublon par statut** : un `Sending` portant une référence PA, déjà émis côté PA ⇒ `Issued` SANS
  renvoi (`GetDocumentStatusAsync` interrogé, un seul envoi — celui du cycle N) (INV-PIPELINE-016).
- **Reprise d'un Sending sans référence** : un `Sending` après crash (sans référence PA) ⇒ renvoyé, la PA
  déduplique par numéro (F05) → `Issued` sans double émission (INV-PIPELINE-016).
- **SIREN non publié → aucun envoi** : `tax_report_setting` inactif ⇒ document maintenu `ReadyToSend`,
  staging conservé, aucune émission (INV-PIPELINE-015).

## Integration — `PipelineRunLogQueriesIntegrationTests`

- **Migration appliquée + relecture** : la migration `pipeline.run_logs` s'applique sur PostgreSQL réel
  (Testcontainers) ; une ligne insérée est relue fidèlement par `PostgresPipelineRunQueries`, énumérations
  par nom, horodatages et compteurs préservés (INV-PIPELINE-005/007).
- **Tri et borne** : les exécutions sont retournées les plus récentes d'abord, bornées par `limit`
  (INV-PIPELINE-005).
- **Journal vide** : aucune exécution → liste vide (jamais d'erreur) (INV-PIPELINE-005).
