# Réponses aux reviews indépendantes v6 & v8 — Corrections du backlog v6

Date : 2026-06-03
Reviews traitées : `tasks/review-independante-v6.md` (13 P1 + 16 P2) et
`tasks/review-independante-v8.md` (11 P1 + 39 P2).
Décisions utilisateur D1–D8 : voir `tasks/decisions.md` (2026-06-03).
Résultat : manifest meta.version 6 → 7, 7 nouveaux items (SOL05, SOL06, API05, WEB09, OPS07,
DOC02, DOC03), 37 fichiers corrigés + state.yaml du dépôt d'état.

Légende : ✅ corrigé · 🔶 corrigé via décision utilisateur · ❌ infirmé (pas de correction) ·
⏸️ accepté sans correction (justifié)

## Review v6 — P1

| # | Finding | Verdict | Correction |
|---|---------|---------|------------|
| 1 | SOL01 : Identity→Party.Contracts incompilable | 🔶 D1 | SOL.yaml : Party.Contracts ajouté au périmètre de vendoring (copie minime, dépend uniquement de Common.Abstractions) |
| 2 | PIV04 exige l'auth créée par PIV05 (ordre inversé) | ✅ | manifest : PIV05 (2030) précède désormais PIV04 (2040, depends_on [PIV05]) ; PIV.yaml réécrit en cohérence ; GATE_CORE_FOUNDATION mise à jour |
| 3 | VAL02 dépend du profil tenant (CFG02) non déclaré | ✅ | manifest : VAL02 depends_on += CFG02 ; bloc CFG repositionné avant VAL (priorités 2050/2060) |
| 4 | CreditNoteRefs en List\<string\> perd la date des avoirs | ✅ | PIV.yaml : PivotDocumentRefDto { Number, IssueDate, SourceReference? } (source F07-F08 §B.3) + golden files avoir simple/partiel/groupé (PIV03) |
| 5 | Pas de régime/cadence TVA pour l'e-reporting | 🔶 D4 | CFG.yaml : champ reportingFrequency nullable (source F09 §2) ; null = transmission suspendue + message |
| 6 | PIP03 : prorata = règle fiscale inventée | ✅ | PIP.yaml : méthode d'imputation = paramètre de tenant validé, JAMAIS de défaut ; F09 amendée |
| 7 | OPS06 : suppression de tenant vs WORM/conservation | 🔶 D7 | OPS.yaml : séquence désactivation → export vérifié (ArchiveVerifier vert) → transfert documenté → suppression journalisée niveau instance |
| 8 | AGT04 : hash seul ne protège pas d'une compromission plateforme | 🔶 D6 | AGT.yaml + PIV.yaml + F12 §7 décision n°4 : manifeste de version signé par clé applicative dédiée (hors plateforme) en V1 + ADR modèle de confiance |
| 9 | Auto-update : aucun item côté plateforme (registre, publication) | ✅ | Nouvel item OPS07 (registre de versions, publication signée, endpoint téléchargement, politique de flotte) ; PIV05 câblé dessus |
| 10 | Permissions/rôles Keycloak : aucun item ne les seed | ✅ | SOL.yaml (SOL01) : seed realm dev + rôles standard + matrice permissions→rôles ; OPS.yaml (OPS03) : mapping utilisateur→tenant + gestion continue via Identity |
| 11 | verify-fast : state.yaml absent traité comme « SOL pending » | ✅ | verify-fast.ps1 : Test-SolItemPending échoue (throw) si le dépôt d'état est absent ; steps bootstrap-check explicites. Testé : ORCH_REPO invalide → exit 1 |
| 12 | run-tests : PASS possible avec zéro suite exécutée | ✅ | run-tests.ps1 : compteur de suites + verdict BOOTSTRAP explicite (état vérifié) ou FAIL si zéro suite hors bootstrap. Testé |
| 13 | run-tests : E2E déclarés mais jamais exécutables (pas d'infra) | 🔶 D3 | Nouvel item SOL05 (harness E2E Testcontainers + Playwright + run-e2e.ps1) ; run-tests exclut Category=E2E ; blueprint blazor-page-item : nœud e2e_run dédié |

## Review v6 — P2

| # | Finding | Verdict | Correction |
|---|---------|---------|------------|
| 1 | VAL03 SourceTotalsRule Blocking vs F04 (Warning) | 🔶 D5 | VAL.yaml : Warning, aligné F04 §3.3 |
| 2 | PIV02 : utilitaires dans Agent.Contracts vs « DTOs purs » | ✅ | PIV.yaml : clarifié — « aucune logique » = aucune logique MÉTIER ; exception documentée et testée |
| 3 | SOL01 : pas de contrôle mécanique de provenance | ✅ | SOL.yaml (SOL03) : verify-fast échoue sur modification Stratum.* non consignée (testé en acceptance) |
| 4 | OPS03 ne dépend pas d'OPS05 (package agent) | ✅ | manifest : OPS03 depends_on [OPS05] + priorités OPS réordonnées |
| 5 | OPS06 ne dépend pas d'OPS03 (écran Clients) | ✅ | manifest : OPS06 depends_on [OPS03] |
| 6 | Catalogue système/instance non défini | ⏸️ | Le socle Stratum gère database-per-tenant ; OPS02 clarifié (migration multi-bases) — pas d'item dédié |
| 7 | ADR Keycloak après que SOL01 a figé l'auth | ✅ | OPS.yaml (OPS01) : le Keycloak de SOL01 est explicitement PROVISOIRE ; l'ADR OPS01 (avec mesure) peut faire évoluer la topologie |
| 8 | x64 agent jamais buildé/testé localement | ✅ | run-tests.ps1 : suites agent x86 ET x64 |
| 9 | « accepted P2s » impossibles avec exit 2 systématique | ❌ | Infirmé : l'acceptation passe par l'agent orch-fix-review (session log), pas par le script — mécanique existante du protocole (Step 4) |
| 10 | Verrou orch-state mono-machine (mutex) | ✅ | orch-state.ps1 : verrou 2 couches — mutex + lock file à création exclusive dans $ORCH_REPO (cross-host). Testé |
| 11 | DoD parle encore de WPF | ✅ | definition-of-done.md réécrite : sections Blazor (bUnit + run-e2e), agent net48, frontières v6 |
| 12 | build-agent-context : lots SVC/WPF, routages v5 | ✅ | build-agent-context.ps1 : table lotToSpecs v6 complète (AGT/WEB/SUP/OPS/BRD ajoutés, SVC/CLI/WPF supprimés). Testé sur AGT01/WEB09 |
| 13 | MANIFEST-CONVENTIONS cite wpf-screen-item | ✅ | Remplacé par blazor-page-item + règle 9 (gates intra-segment) ajoutée |
| 14 | verify-archive (v5) sans équivalent opérateur | ✅ | TRK.yaml (TRK06) + API.yaml (API03) + WEB.yaml (WEB04) : vérification d'intégrité du coffre complet à la demande |
| 15 | Kit support éditeur absent du backlog | 🔶 D8 | Nouvel item DOC02 (FAQ, formation N1, matrice N1/N2/N3, SLA) |
| 16 | Veille réglementaire promise sans item | 🔶 D8 | Nouvel item DOC03 (sources, cadence, checklist d'impact, règle « jamais de code sans amendement de spec ») |

## Review v8 — P1

| # | Finding | Verdict | Correction |
|---|---------|---------|------------|
| 1 | SOL01 : périmètre Identity sans Party incompilable | 🔶 D1 | Identique v6-P1-1 (Party.Contracts vendorisé) |
| 2 | SUP03 : le socle n'a qu'un StubEmailTransport (pas de SMTP) | ✅ | SUP.yaml (SUP03) : implémentation IEmailTransport SMTP réel + ADR MailKit ; analyse d'impact ligne « Notifications email » passée à 🟡 |
| 3 | Module Job sans résolution de tenant (4 items bloqués) | ✅ | Nouvel item SOL06 (TenantJobRunner/ITenantJob — mécanique unique) ; TRK06/PIP01/PIP03/SUP01 la référencent ; analyse d'impact ligne « Ordonnanceur » passée à 🟡 |
| 4 | VAL04 : catégories AA/AAA non couvertes (taux 0 passerait) | ✅ | VAL.yaml : AA→taux>0, AAA→taux>0 ajoutés (source F03 §2.1) + question ouverte expert-comptable consignée |
| 5 | Flux 10.2/10.4 : aucune règle de dérivation | 🔶 D2 | PIP.yaml + PAA.yaml + PIV.yaml + F09 : V1 = Flux 10.4 uniquement ; 10.2 = capacité déclarée non alimentée (phase 2) |
| 6 | Échéances déclaratives incalculables (pas de fréquence) | 🔶 D4 | Identique v6-P1-5 + SUP01/WEB01 conditionnés à reportingFrequency non null |
| 7 | Gestion des agents absente (API + écran) après provisioning | ✅ | Nouveaux items API05 (endpoints liste/enregistrement/révocation/rotation) et WEB09 (écran Agents) |
| 8 | Auto-update sans côté plateforme | ✅ | Identique v6-P1-9 (OPS07) |
| 9 | Aucun item ne crée l'infra E2E (10 items blazor bloqués) | 🔶 D3 | Identique v6-P1-13 (SOL05 + run-e2e.ps1) |
| 10 | SOL03 contredit le blueprint sur l'exécution des E2E | 🔶 D3 | SOL.yaml (SOL03) + blueprint blazor-page-item + protocol.md alignés : E2E = suite séparée run-e2e.ps1, nœud e2e_run dédié |
| 11 | prompt.md « create it if absent » vs protocol « never recreate » | ✅ | prompt.md : « must already exist — if absent, EXIT 1 per protocol.md Step 1; never recreate it » |

## Review v8 — P2 (par thème)

| # | Finding | Verdict | Correction |
|---|---------|---------|------------|
| 1 | TRK01/TRK03 ne déclarent pas PIV04 (IDocumentIntake, AlterationDetected) | ✅ | manifest : TRK01 += PIV04, TRK03 += PIV04 |
| 2 | OPS03 depends_on vide trompeur | ✅ | manifest : OPS03 depends_on [OPS05] + commentaire d'ordre du bloc OPS |
| 3 | PIV04 : fausse alternative ADR « module Document du socle » | ✅ | PIV.yaml : option retirée (stockage fichier par tenant, ADR d'organisation) |
| 4 | Frontière IsCompanyHint ambiguë (agent vs plateforme) | ✅ | F01-F02 amendée (transcription brute côté agent) + VAL.yaml (VAL05) clarifié |
| 5 | verify-archive sans équivalent opérateur complet | ✅ | Identique v6-P2-14 |
| 6 | TVA04 : critère garde-fou production = faux vert potentiel | ✅ | TVA.yaml : le critère référence le test d'intégration du garde-fou PIP01 |
| 7 | F05 suffixe -R1 non amendé (contredit supersede) | ✅ | F05 §4.2 + §8 décision n°1 : caduc, mécanique supersede ; TRK.yaml (TRK02) : note de renvoi |
| 8 | LookupDirectoryAsync (annuaire) non documenté comme différé | ✅ | F05 §3 + PAA.yaml : annuaire différé en phase 2 (produit V1 = B2C) |
| 9 | Diagnostic « SIREN publié / tax_report_setting » non câblé | ✅ | PIP.yaml (PIP01) : contrôle pré-send + SUP.yaml (SUP01) : règle d'alerte dédiée |
| 10 | Périmètre réception V1 absent de DOC01/CMP03 | ✅ | DOC.yaml (DOC01) + CMP.yaml (CMP03) : réception = portail PA, jamais le produit (V1) |
| 11 | F06 §8 + Cadrage : « archivage délégué à la PA » contredit le WORM | ✅ | F06 §2/§8 + Cadrage (2 passages) : marqués caducs avec renvoi à la décision 2026-06-02 et TRK05/06 |
| 12 | Déclaration « néant » (période sans encaissement) non traitée | ✅ | PIP.yaml (PIP03) : comportement par défaut sûr (rien transmettre + signaler) + décision ouverte DGFiP consignée |
| 13 | Gestion continue utilisateurs/rôles absente | ✅ | OPS.yaml (OPS03) : écrans Identity du socle + E2E « 2e utilisateur + retrait de droit » |
| 14 | Reprise documents Sending après crash implicite | ✅ | PIP.yaml (PIP01) : reprise dès le premier cycle après redémarrage, testée |
| 15 | PIV02 : règles de canonicalisation non figées | ✅ | PIV.yaml (PIV02) : decimal/échappement \uXXXX/dates figés + UN writer partagé |
| 16 | SOL01 : volume ~50 800 LOC pour une session | ✅ | SOL.yaml (SOL01) : stratégie de copie scriptée hors-LLM + adaptation LLM, prérequis Docker |
| 17 | AGT04 : points durs Windows non tranchés | ✅ | AGT.yaml (AGT04) : ADR compte de service/verrou fichiers/updater détaché/rollback timeout |
| 18 | OPS02 : migration DbUp mono-base | ✅ | OPS.yaml (OPS02) : boucle multi-bases, échec partiel, blocage des push (503), sauvegarde pré-migration |
| 19 | OPS01 : coût/empreinte Keycloak non mesuré | ✅ | OPS.yaml (OPS01) : ADR avec mesure heap-bornée avant pricing |
| 20 | OPS05 : critère x86/x64 non écrit | ✅ | OPS.yaml (OPS05) : x86 par défaut si adaptateur ODBC 32-bit, même bitness service/adaptateurs |
| 21 | AGT01/AGT05 : verrou SQLite inter-process non défini | ✅ | AGT.yaml : mutex nommé Global\LiakontAgentRun + ACL ProgramData posées par l'installeur (OPS05) |
| 22 | OPS01 : restauration jamais testée de bout en bout | ✅ | OPS.yaml (OPS01) : restore → ArchiveVerifier vert en acceptance |
| 23 | PRA absent (RTO/RPO) | ✅ | OPS.yaml (OPS01) : PRA documenté + testé, lien méta-supervision |
| 24 | PIV05 : formulation ApiKey induit une réutilisation de table | ✅ | PIV.yaml (PIV05) : pattern uniquement, entité Agent propre scopée tenant |
| 25 | Migrations Notification V009/V010 seedent des données ERP | ✅ | SOL.yaml (SOL01) : neutralisation + consignation provenance |
| 26 | ModuleIsolationTests exigent MODULE.md/INVARIANTS.md/SCENARIOS.md | ✅ | SOL.yaml (SOL04) : obligation documentée ; PIV.yaml (PIV05) : livrables du module Ingestion |
| 27 | ADR-0010 Stratum dupliqué | ✅ | SOL.yaml (SOL01) : déduplication lors de la copie des ADR |
| 28 | codex-review : sortie vide/non conforme = CLEAN | ✅ | codex-review.ps1 : sentinelle « No findings. » obligatoire, sinon échec moteur |
| 29 | codex-review : -Base inexistante = diff vide silencieux | ✅ | codex-review.ps1 : git rev-parse --verify + échec explicite |
| 30 | build-agent-context : table désynchronisée | ✅ | Identique v6-P2-12 |
| 31 | DoD restée à l'état v5 | ✅ | Identique v6-P2-11 |
| 32 | orchestration-protocol.md : diagramme manifest v2 | ✅ | Diagramme + tableau des segments/gates régénérés depuis le manifest v6 |
| 33 | « F01-F11 » au lieu de F01-F12 | ✅ | protocol.md + README.md (2 occurrences) corrigés |
| 34 | MANIFEST-CONVENTIONS : wpf-screen-item | ✅ | Identique v6-P2-13 |
| 35 | verify-fast Run-Step : $LASTEXITCODE périmé | ✅ | verify-fast.ps1 : $global:LASTEXITCODE = 0 avant chaque step |
| 36 | Renvois morts (repo-standards.md, testing-strategy.md…) | ✅ | protocol.md + run-tests.ps1 : annotés « créé par SOL04/SOL05 » |
| 37 | Citations « F12 §7.4 » imprécises | ✅ | AGT.yaml, TRK.yaml, OPS.yaml : « F12 §7, décision n°X » |
| 38 | Citation « blueprint §2.5 » inexistante (VAL.yaml:5) | ✅ | VAL.yaml : « blueprint §2 règle 5 » |
| 39 | protocol.md : exemple feat/socle (branche v5 supprimée) | ✅ | protocol.md : exemple feat/socle-v6 |

## Questions ouvertes des reviews — traitement

| Question | Traitement |
|----------|------------|
| GATE_DEMO_ISATECH sans PR (gate intra-segment) | Documenté comme VOULU : commentaire dans le manifest + règle 9 de MANIFEST-CONVENTIONS.md (passage à done via orch-state.ps1) |
| Stratégie « jobs par tenant » | Tranché : item socle dédié SOL06 |
| Identity ↔ Party | Tranché : D1 (vendoriser Party.Contracts) |
| Découpage SOL01 / core-foundation | Atténué : copie scriptée hors-LLM (SOL01) ; le découpage du segment reste une option si SOL01 déborde |
| Topologie CMP (ISATECH) | Inchangé — reste une action humaine (tasks/todo.md) |
| AA/AAA à taux 0 légitime ? | Règle étendue (source F03) + question expert-comptable consignée dans VAL04 |
| Déclaration néant obligatoire ? | Comportement par défaut sûr + décision ouverte consignée dans PIP03 |
| Séquestre APP | Inchangé — dispositif contractuel hors backlog produit (action humaine) |
| Échéances déclaratives | Tranché : D4 (champ nullable, jamais de cadence devinée) |
| Anti-doublon après réinstallation d'agent | Tracé comme critère + test de PIV04 |
| OPS02 avant BRD01 (branding) | OPS.yaml (OPS02) : branding par défaut tant que BRD01 non livré |
| Commandes POSIX dans le protocole | protocol.md : note explicite « exécuter via l'outil Bash, jamais PowerShell 5.1 » |
| Résidus v5 dans le dépôt d'état | ⏸️ Conservés comme trace historique (pas de correction) |

## Review de cette session de correction (codex-review, 2026-06-03)

La review du working tree de cette session a tourné en boucle jusqu'à clean :

| Round | Finding | Correction |
|-------|---------|------------|
| 1 | [P2] WEB04 consomme API03 (bouton vérification archive) sans le déclarer en dépendance | manifest : WEB04 depends_on [WEB01, API03] |
| 1 | [P2] run-tests.ps1 : la garde compte les invocations de suite, pas les tests exécutés (exit 0 avec 0 test = faux vert résiduel) | run-tests.ps1 : comptage des tests exécutés (parsing sortie VSTest) + échec si une solution présente exécute 0 test |
| 2 | [P2] run-tests.ps1 : le parseur ne reconnaît que le format VSTest — avec Microsoft.Testing.Platform (.NET 10), FAIL erroné « 0 tests » | run-tests.ps1 : 3e format reconnu (MTP `total: N`) + distinction « 0 test réel » / « format non reconnu » (les deux échouent, messages distincts) ; validation en conditions réelles = acceptance SOL03 |

## Findings infirmés / non retenus

| Finding | Motif |
|---------|-------|
| « accepted P2s impossibles » (v6 P2) | Le mécanisme d'acceptation passe par l'agent orch-fix-review + session log, pas par le script — conforme au protocole Step 4 |
| « faux vert $script:Base » (signalé puis écarté par la v8 elle-même) | Les paramètres de script PowerShell SONT accessibles via le scope script — vérifié par test par le reviewer v8. Aucune correction |
| « catalogue système OPS manquant » (v6 P2) | La database-per-tenant du socle Stratum couvre le besoin ; OPS02 clarifie la migration multi-bases |
| Résidus v5 du dépôt d'état (events.jsonl, session-log) | Trace historique d'audit volontairement conservée |
