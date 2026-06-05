# PAA03 — Suite de tests de contrat IPaClient

Branche : `feat/pa-framework-PAA03` (segment `feat/pa-framework`, slot-1).
Spec : `orchestration/items/PAA.yaml` (PAA03), F05 §abstraction IPaClient, testing-strategy §6.

## Objectif
Créer `tests/Liakont.PaClients.Contract.Tests/` : une suite de contrat ABSTRAITE héritable par
tout plug-in PA, exécutée contre le plug-in Fake (PAA02), + une doc « ajouter un plug-in PA ».

## Plan
- [ ] Projet `tests/Liakont.PaClients.Contract.Tests/` (xUnit + FluentAssertions, dans src/Liakont.sln)
- [ ] `PaSendOutcome` (vocabulaire d'issue, découplé de FakePaScenario)
- [ ] `PaClientContractSetup` (issue + capacités déclarées + erreurs de rejet)
- [ ] `PaClientContractTests` (classe de base abstraite, contrat observable sur la surface IPaClient) :
      - envoi valide → Issued exploitable
      - avoir → suit la capacité déclarée (lien d'origine porté au plug-in)
      - rejet → erreurs remontées intactes, pas d'émission
      - erreur silencieuse (succès transport + errors[]) → RejectedByPa
      - timeout / erreur technique → TechnicalError re-tentable
      - idempotence : même numéro jamais émis deux fois
      - capacité absente → résultat typé, jamais d'exception
      - cohérence capacités déclarées ↔ comportement réel
- [ ] `FakePaClientContractTests : PaClientContractTests` (preuve d'exécutabilité contre Fake)
- [ ] Doc `docs/architecture/ajouter-un-plugin-pa.md`
- [ ] Ajouter le projet à `src/Liakont.sln` (vérifier le diff : pas de dossier de solution parasite)
- [ ] verify-fast vert
- [ ] run-tests vert (la suite passe contre Fake)
- [ ] codex-review clean / P2 acceptés

## Review
(à compléter)
