# CP04 — SendDocumentAsync (dépôt du Factur-X scellé, deposerFluxFacture)

Segment : chorus-pro / branche feat/pa-chorus-pro-CP04 (slot-1).
Spec : F18 §3 + tasks/plan-chorus-pro.md CP3 + §2bis D8/D9. Plug-in = transport PUR (aucun montant).

## Décisions de conception (sourcées)
- **Garde artefact AVANT tout HTTP** (patron GeneriqueClient) : `PaSendContext.PreBuiltArtifact` vide
  → Bloqué `CPRO_ARTEFACT_REQUIS` (TechnicalError, message FR), jamais régénéré (CLAUDE.md n°6).
  Pas de garde de capacité dans le plug-in : la génération de l'artefact est gatée EN AMONT par
  `SupportsFacturXTransmission` (plateforme/CP08) — patron SuperPdp/Generique. Capacité encore false
  jusqu'à CP08 → sans artefact, dépôt toujours bloqué (fail-closed, sûr).
- **idUtilisateurCourant OMIS** : cardinalité + endpoint de résolution non confirmés (F18 §3.2 « À
  VERROUILLER »). CLAUDE.md n°2 (ne pas inventer un endpoint/champ) → branche documentée « SINON
  l'omettre ». Résolution `consulterCompteUtilisateur` différée au raccordement (CP suivant si confirmé).
- **Payload** (F18 §3.1, camelCase EXACT via [JsonPropertyName]) : fichierFlux=base64(artefact),
  nomFichier, syntaxeFlux=IN_DP_E2_CII_FACTURX (sourcé), avecSignature=false (D9).
- **Base path REST** = relatif sous `BaseUrl` (config, environnement) ; la ressource versionnée
  (`factures/v1/deposer`) = constante contrat dans Defaults, marquée « à verrouiller au Swagger PISTE »
  (F18 §3.3). Le « NE PAS HARDCODER » de §3.3 vise la BASE API (host+/cpro/, déjà sur config.BaseUrl).
- **Mapping réponse** (ChorusProResponseMapper, F18 §3.4/§5) : 2xx + numeroFluxDepot présent → **Sending**
  (PaDocumentId=numeroFluxDepot, JAMAIS Issued au dépôt, A1/D5) ; 2xx sans numeroFluxDepot → Rejected
  (rejet métier silencieux) ; 4xx métier → Rejected (PaError intacts) ; 5xx/401/403 → Technical ;
  timeout/réseau → Technical SANS re-POST (A3/D8). RawResponse conservée (corps réponse = sans credential).

## Tâches
- [ ] ChorusProDefaults : + DepositPath, SyntaxeFluxFacturX, DepositWithSignature=false, FileNameFor
- [ ] Wire/ChorusProDepositRequest.cs (internal record, [JsonPropertyName] camelCase)
- [ ] ChorusProPayloadBuilder.cs (static : artefact+numéro → JSON)
- [ ] ChorusProResponseMapper.cs (MapDeposit + IsRetryableStatus, parse numeroFluxDepot fail-safe)
- [ ] ChorusProClient.SendDocumentAsync : garde artefact → POST authentifié → mapper ; timeout/réseau → Technical
- [ ] Tests : ChorusProClientTests (remplacer test squelette obsolète) + ChorusProSendTests
- [ ] verify-fast (2 solutions) + run-tests + codex-review

## Review
(à compléter)
