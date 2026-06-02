# Review indépendante — Conformat backlog v4
Date : 2026-06-02
Reviewer : agent indépendant (session review-v5)

## Synthèse
- Findings P1 : 2
- Findings P2 : 23
- Verdict global : **corrections requises avant lancement orchestration**

Les deux P1 sont structurants pour la qualité du run autonome :
1. la boucle de review automatique est court-circuitée (faux-vert d'outillage) — chaque item
   serait mergé **sans review effective** ;
2. un trou fonctionnel explicitement exigé par F10 (résolution opérateur d'un rejet PA / d'un
   blocage non corrigeable) n'a ni item, ni endpoint, ni état.

Hors ces deux points, le backlog est d'une cohérence remarquable : structure 80↔80 sans orphelin
ni cycle, priorités alignées sur les dépendances, gates complètes, **zéro règle fiscale inventée**,
généricité (2 axes) respectée, garde-fous fiscaux présents. La majorité des P2 sont des écarts de
complétude (garde-fou présent dans la *description* mais pas dans les *critères d'acceptation*) et
des décisions techniques à border par ADR (net48).

---

## Findings P1

[P1] | tools/codex-review.ps1:236-249 + orchestration/blueprints/module-work-item.yaml:70-81 (ordre des nœuds) + orchestration/protocol.md:184 | **Faux-vert : la review automatique ne revoit rien en mode orchestration.** Dans tous les blueprints, le nœud `review` s'exécute APRÈS `commit_apply` (module-work-item : commit_compose→commit_apply→integration_tests→review). Or `codex-review.ps1` invoqué sans `-Base` (cas du nœud `review`, protocol.md:184 ne passe pas de base) ne revoit QUE l'arbre de travail non commité : après `commit_apply` (qui fait `git add -A && git commit` puis `rm .orch-commit-msg`), `git status --porcelain` est vide → la fonction principale tombe dans la branche « No uncommitted changes to review » (codex-review.ps1:246-249) → exit 0. Le nœud `review` réussit donc systématiquement **sans avoir revu une seule ligne**, et la boucle `fix_review` (qui reboucle verify→commit→review sur un arbre toujours propre) est elle aussi inopérante. C'est exactement le faux-vert que CLAUDE.md §9 interdit (« Review MUST happen on the current working tree, not only on committed changes » — mais ici l'arbre est propre car déjà commité). **Correction** : faire revoir le diff COMMITÉ de la sous-branche, p. ex. nœud `review` → `codex-review.ps1 -Base <segment-branch>` (ou `-Base HEAD~<n>`), OU déplacer `review` AVANT `commit_apply`. Vérifier que round 2+ (`-Round N`) reste branché sur la même base.

[P1] | orchestration/items/API.yaml:56-72 (API03) + orchestration/items/WPF.yaml:61-83 (WPF03) + orchestration/items/TRK.yaml:45-52 (TRK02) | **Aucun chemin opérateur pour les actions exigées par F10 sur un rejet PA ou un blocage non corrigeable.** F10-Console-Admin-WPF.md:65 exige explicitement les actions contextuelles `[Renvoyer]`, `[Marquer comme traité manuellement]`, `[Ignorer définitivement] (avec motif obligatoire, journalisé)`, et F10:54 décrit `RejectedByPa` comme « voir le motif, corriger, **renvoyer** ». Or : (a) aucun endpoint d'action (API03 n'a que send/send-all/verdict/trigger/audit-export) ni bouton console ne déclenche le renvoi sous nouveau numéro ; la règle existe au niveau données (TRK03 « renvoi autorisé sous NOUVEAU numéro, l'ancien passe Superseded ») mais rien ne l'actionne. (b) La machine à états TRK02 (Detected, Blocked, ReadyToSend, Sending, Issued, RejectedByPa, TechnicalError, Superseded) n'a **aucun état terminal** « traité manuellement » / « ignoré », et `Blocked` ne transite que vers `ReadyToSend`. Conséquence : un document que le produit bloque sans solution automatique (avoir orphelin VAL04, acheteur professionnel à traiter hors passerelle VAL05) reste bloqué indéfiniment, pollue compteurs et notifications, sans sortie tracée. **Correction** : étendre TRK02 avec des transitions/états terminaux journalisés (`Superseded` via renvoi, `ManuallyHandled`, `Ignored` + motif obligatoire) ; ajouter à API03 `POST /documents/{id}/resend`, `POST /documents/{id}/mark-handled`, `POST /documents/{id}/dismiss` (niveau actions, identité Windows journalisée) ; câbler les boutons dans WPF03/WPF05. Préciser comment le nouveau numéro de renvoi est obtenu (la source legacy est seule créatrice de numéros — PAB.yaml:44) sans le deviner.

---

## Findings P2

### Faisabilité technique net48 (règle « tout nouveau package = ADR » non budgétée)

[P2] | orchestration/items/TRK.yaml:213-215 (TRK08) | Extraction de texte PDF (stratégie 2) qualifiée de « simple » : aucun parser PDF n'est built-in en .NET Framework 4.8. Un package tiers est obligatoire, non prévu par l'item, et **piège de licence** : iTextSharp est AGPL (incompatible produit propriétaire). | Trancher le package ET sa licence (PdfPig Apache-2.0 ou Docnet.Core sont des candidats viables) via un ADR ; ajouter le nœud ADR à TRK08.

[P2] | orchestration/items/WPF.yaml:196-197 (WPF08) | « Aperçu PDF intégré (visionneuse simple) » : WPF n'a aucun contrôle PDF natif. WebBrowser/Trident dépend d'Acrobat installé ; WebView2/PdfiumViewer/MoonPdf sont des packages (+ runtime Edge ou `pdfium.dll` natif par plateforme). | Trancher la techno d'affichage et acter le package + son déploiement par un ADR (non prévu dans l'item).

[P2] | orchestration/items/TRK.yaml:176-181 (TRK07) | OpenTimestampsAnchor décrit comme « protocole simple / POST HTTP », mais la sérialisation et la vérification du format binaire `.ots` (arbres d'opérations, attestation Bitcoin, cycle pending→complete) ne sont pas triviales, et aucune des deux voies n'est sans décision : implémentation maison (dette + tests du parsing binaire) ou package (`NBitcoin`/`opentimestamps`). | Ajouter un ADR à TRK07 actant la voie retenue.

[P2] | orchestration/items/TRK.yaml:182-184 (TRK07) | Rfc3161Anchor : les API `Rfc3161TimestampRequest`/`Rfc3161TimestampToken` **n'existent pas** en .NET Framework 4.8 (ajoutées en .NET 5+). Construire `TimeStampReq` ASN.1/DER et valider le token via `SignedCms` est faisable built-in mais coûteux ; l'alternative BouncyCastle est un package. | Documenter le choix (ASN.1 manuel built-in vs BouncyCastle → ADR) dans TRK07.

[P2] | orchestration/items/TRK.yaml:18 (schéma SQLite) + blueprint.md:182 (stack) | `System.Data.SQLite` requiert `SQLite.Interop.dll` natif **distinct par plateforme** (x86/x64). Rien ne confirme que le packaging (CFG03) déploie les deux interops dans la sortie Service/CLI/App. | Préciser dans CFG03 la copie de `SQLite.Interop.dll` par plateforme, sinon échec runtime `Unable to load DLL 'SQLite.Interop.dll'` selon l'architecture du process.

### Garde-fous fiscaux présents en description mais absents des critères d'acceptation

[P2] | orchestration/items/PIV.yaml:43-47 (PIV02) | Le type `Amount (decimal)` est en description (PIV02:39) mais l'acceptance ne réaffirme pas « decimal partout, aucun float/double » comme le fait PIV01:30 — pour un type portant montants encaissés et bases TVA. | Aligner l'acceptance de PIV02 sur PIV01.

[P2] | orchestration/items/PIP.yaml:83-90 (PIP03) | L'agrégation jour×taux calcule `taxable_base`/`vat_amount` (l'endroit exact où une dérive float s'introduit) sans qu'aucun critère d'acceptation n'exige `decimal` ni n'interdise float. | Ajouter « montants agrégés en decimal, aucun float/double » à l'acceptance de PIP03.

[P2] | orchestration/items/TVA.yaml:118-125 (TVA05) | `MappingChangeLog` est un journal d'audit append-only (règle non négociable §4), mais — contrairement à TRK04:98 (DocumentEvent) et TRK06:158 (WORM) — l'acceptance n'exige pas l'append-only « vérifié par test ». | Ajouter « append-only vérifié par test (modification d'une entrée existante rejetée) » à TVA05.

[P2] | orchestration/items/ADP.yaml:46-52 (ADP02) | La lecture seule stricte (interdiction INSERT/UPDATE/DELETE, pas de verrou/transaction d'écriture) est en description (ADP02:38-39) mais l'acceptance se limite à « en lecture seule stricte » sans critère testable — alors que c'est un P1 en review (CLAUDE.md §13). | Décliner en critères : « aucune commande non-SELECT émise (vérifié), connexion read-only si le driver le supporte, aucun verrou ».

[P2] | orchestration/items/VAL.yaml:58-64 (VAL03) | L'arrondi commercial half-up 2 décimales (description VAL03:52, exigé par F04 §4.3 + blueprint §7) n'est pas un critère d'acceptation, alors que la comparaison « sans tolérance » sur des montants non arrondis créerait de faux écarts. | Ajouter « comparaison après arrondi half-up 2 décimales, en decimal » à VAL03.

### Couverture des specs (écarts de complétude)

[P2] | orchestration/items/VAL.yaml:90-95 (VAL05) vs F07-F08-Avoirs-Frontiere-B2B-B2C.md (A.4) | L'heuristique `IsCompanyHint` de VAL05 (« 1 indice fort OU **2 indices moyens** ») n'est pas réalisable : la spec ne définit qu'**un seul** indice moyen (forme juridique). VAL05 ajoute aussi des formes (`SCI, SASU`) absentes de la liste F07-F08. | Aligner sur la spec (déclencher sur 1 indice fort) ou faire trancher seuil + liste et les ajouter à F07-F08 avant figeage.

[P2] | F04-Controles-Qualite-Validation.md:39 — contrôle « Tax_report_setting actif côté PA pour ce SIREN » (Warning, diagnostic démarrage) | Exigence de contrôle F04 non rattachée à un item de validation (partiellement couverte par PAB03 `EnsureTaxReportSettingAsync` et CLI01 `check-config`, mais sans le diagnostic « Transport not available »). | Rattacher explicitement à CLI01/CFG02 (check-config) ou à VAL.

[P2] | F04-Controles-Qualite-Validation.md:54 — contrôle « Total passerelle = Total source (`total_bordereau`) » (Warning) | Le contrôle « écart total passerelle ≠ total source = bug d'extraction probable » de F04 n'est repris dans aucun item ; VAL03 couvre Σlignes=totaux mais pas la comparaison au total source brut (présent dans PivotTotals, PIV01). | Ajouter une règle Warning `SOURCE_TOTAL_MISMATCH` à VAL03/VAL04.

[P2] | orchestration/items/CLI.yaml:11-24 (CLI01) vs F11-CLI-Mode-Automatique.md §2 | F11 spécifie 6 commandes CLI (`run, extract, send, sync-status, check-config, report`). CLI01 (décision 2026-06-02 réduisant le CLI à mise en service/secours) n'en offre que 5 autres ; `extract`, `send`, `sync-status`, `report` disparaissent sans note d'alignement dans F11. | Noter dans F11 que ces sous-commandes sont remplacées par les endpoints API + `run`, pour éviter une lecture « commandes manquantes ».

[P2] | orchestration/items/PIV.yaml:53-57 (PIV03) vs F01-F02 §4 (contrat IExtractor) | Deux éléments du contrat F01-F02 manquent à PIV03 : `ExtractorInfo GetInfo()` (identité adaptateur) et les exceptions typées R7 (`SourceUnavailableException`/`SourceSchemaException`) qui pilotent la décision réessayable vs non-réessayable. Sans elles, PIP01 ne peut pas distinguer un échec d'extraction réessayable d'une erreur de schéma. | Confirmer que `HealthCheckResult` couvre l'identité et ajouter explicitement les types d'exception R7 à PIV03 (ou justifier leur absence).

### Parcours opérateur (trous secondaires, hors le P1)

[P2] | orchestration/items/API.yaml:56-72 (API03) + orchestration/items/WPF.yaml:113-134 (WPF05) | Pas d'action « revérifier ce document » ciblée après correction : le seul déblocage d'un `Blocked` (source corrigée, table TVA complétée, verdict B2C posé) passe par un run complet (`POST /runs/trigger`), lourd et susceptible d'envoyer d'autres documents. Le re-check existe au niveau moteur (PIP01 CHECK) donc ce n'est pas un cul-de-sac. | Ajouter `POST /documents/{id}/recheck` + bouton [Revérifier] dans WPF03, ou documenter explicitement le déblocage par run complet.

[P2] | orchestration/items/API.yaml:56-72 (API03) vs orchestration/items/TRK.yaml:184-187 (TRK07) | Export contrôle fiscal par **période** non exposé : TRK07 produit un dossier « pour un document OU une période », mais l'API n'offre que `GET /documents/{id}/audit-export` (par document) et WPF03 n'exporte que sur l'écran détail. Un vérificateur demande un exercice entier. | Exposer `GET /audit-export?from=&to=` + une action d'export par période (écran Journal/Configuration).

[P2] | orchestration/items/PIP.yaml:8-37 (PIP01) vs orchestration/items/TRK.yaml:75-76 (TRK03) | `GetPotentiallySentDocuments()` est exposé par TRK03 « pour que le pipeline vérifie côté PA avant de retenter », mais PIP01 ne décrit aucune étape de reprise des documents restés en `Sending` après un crash dur du Service. La reprise sans doublon ne couvre que le timeout intra-exécution (PAB02). | Ajouter à PIP01 une étape de reprise au démarrage : documents en `Sending` → `GetPotentiallySentDocuments()` → vérification statut PA avant décision.

### Outillage & protocole d'orchestration

[P2] | tools/orch-state.ps1:26 + 128-146 (commande `update`) | Aucune validation : `-Status` n'a pas de `ValidateSet` et `update` remplace le statut par regex sans garde de transition. Une faute de frappe (`-Status doen`) ou une transition interdite (`done`→`pending`, mutation d'une gate en statut arbitraire) est écrite **silencieusement** dans state.yaml = faux-vert d'état. | Ajouter `[ValidateSet('pending','claimed','in_progress','done','blocked','stale','failed','gate_pending')]` sur `-Status` et un garde de transitions autorisées.

[P2] | tools/run-tests.ps1:27 vs orchestration/items/SOL.yaml:60 (SOL02) + orchestration/items/TRK.yaml:112 (TRK05) | `run-tests.ps1` n'exclut que `Category!=Staging`, alors que l'acceptance de SOL02 et TRK05 imposent d'exclure aussi `Category=Integration.SqlServer` (LocalDB optionnel). Sur une machine sans SQL Server LocalDB, les tests SQL Server de TRK05 s'exécuteraient et échoueraient → `integration_tests` (retry on_exhaust: blocked) bloquerait TRK05. | Lors de SOL02, réécrire le filtre en `Category!=Staging&Category!=Integration.SqlServer` (conformément à sa propre acceptance).

[P2] | orchestration/protocol.md:58-62 (Step 0 acquisition de slot) | L'acquisition de lease écrit `leases/slot-N.yaml` sans création atomique exclusive : deux agents qui voient le même slot libre peuvent l'écrire tous les deux (TOCTOU). Mitigé par le `claim` sous mutex (un seul obtient chaque item), mais deux agents peuvent croire occuper le même slot. | Créer le fichier de lease en mode exclusif (échec si existe) ou passer l'acquisition de slot par `orch-state.ps1` sous le même mutex.

[P2] | orchestration/session-log/ + orchestration/archive/ (dépôt source) | Ces deux répertoires (vides, `.gitkeep`) dupliquent les répertoires réels de `$ORCH_REPO` et ne sont pas gitignorés (contrairement à `orchestration/state/*.yaml`, .gitignore:51). Un agent pourrait y écrire un log/segment par erreur au lieu de `$ORCH_REPO`. `orchestration/state/` est lui correctement neutralisé par son README. | Supprimer `session-log/` et `archive/` du dépôt source (ou y mettre le même README d'avertissement que `state/`).

[P2] | docs/conception/F06-Tracking-Piste-Audit.md §9 (décision #3) vs orchestration/items/TRK.yaml:1-8 + tasks/decisions.md:17 | F06 §9 recommande « hash chaîné / horodatage qualifié : NON en V1 », alors que TRK06/TRK07 les imposent. Le backlog est cohérent avec la décision 2026-06-02 (decisions.md:17), mais la spec F06 n'a pas été mise à jour → la spec contredit désormais le backlog. | Mettre à jour F06 §9 #3 pour refléter la décision « coffre WORM + chaîne de hashes + ancrage intégrés en V1 ».

---

## Dimensions sans finding

- **A. Cohérence structurelle de l'orchestration** : saine. Vérifié par script : 80 items manifest ↔ 80 items state.yaml (identiques), chaque item présent dans son fichier de lot, aucun orphelin, toutes les gates référencées (`gate:`/`depends_on_gate:`) déclarées, aucun `depends_on` cassé, **aucun cycle**, priorités strictement croissantes le long des dépendances et toutes uniques, chaque gate de segment forme un « leaf-set » complet (les feuilles couvrent transitivement tous les items de leur segment). Convention de sous-branche `<segment>-<item>` (tiret) cohérente entre protocol.md:159, orch-state.ps1, decisions.md et lessons.md. `.orch-commit-msg` correctement gitignoré (.gitignore:59).

- **B. Alignement backlog ↔ doctrine (généricité)** : aucun finding. Aucune donnée client dans un item de code (régimes 5/6, SIREN CMP, chaîne ODBC réelle vivent tous dans le lot CMP = `deployments/cmp/`, et les items CMP01/02 exigent « aucun fichier créé/modifié dans src/ »). La console (WPF01) ne référence que Api+ApiClient (vérifié dans l'acceptance). Aucune dépendance à un PA concret hors plug-in : PIP01 « ne référence JAMAIS un plug-in PA concret », comportement piloté par `PaCapabilities`/`ExtractorCapabilities` (PIP03 « aucun flag de configuration produit — uniquement la capacité du PA »). La table d'exemple TVA04 utilise des codes fictifs (REGIME-A...).

- **D. Graphe de dépendances et ordonnancement des segments** : aucun finding. L'ordre des gates permet de livrer (socle → core-foundation → {pa-framework, adapter-encheresv6} → {pa-b2brouter, pa-superpdp, pipeline} → service-api → console-admin → toolkit → cmp). Les dépendances inter-segments implicites sont satisfaites par l'ordre des gates : API04 (service-api) a besoin de TVA05 + TRK08 (core-foundation), disponibles car core-foundation est transitivement en amont ; WPF08 a besoin de PIV03 (pool) + API04, en amont ; PIP/SVC se démontrent sur FixtureExtractor + FakePaClient (Core) sans dépendre des plug-ins concrets. Super PDP (bloqué tant que sa sandbox humaine n'est pas ouverte) n'est PAS sur le chemin critique de la prod CMP (qui passe par B2Brouter) — découplage correct.

- **F. Solidité fiscale** : aucun P1. Garde-fous fatals présents et testés dans l'acceptance (BR-CO-15 tolérance zéro VAL03, VATEX si E+0% aux 3 niveaux TVA01/TVA05/VAL04, montants positifs avoirs VAL04/PIP02, régime inconnu→blocage TVA02, refus d'envoi table NON VALIDÉE/exemple TVA01/04/05, DocumentEvent append-only TRK04, coffre WORM TRK06, pas de purge auto TRK04). Aucune validation Blocking affaiblie en Warning. Limites assumées (NF Z42-013, OCR, B2B garde-fou) cohérentes entre items et decisions.md. Les écarts relevés (decimal/append-only/read-only absents de l'*acceptance*) sont des P2 listés ci-dessus.

- **C. Couverture des specs** : F01 à F11 toutes couvertes par ≥ 1 item ; **aucune règle fiscale chiffrée inventée** (clé TVA intracommunautaire, exception La Poste 356000000, taux 20/10/2,1, catégories UNCL5305, VATEX-EU-F/I/J, modèle d'avoir 381 → tous tracés vers F03/F04/F07-F08). Écarts de complétude en P2.

---

## Questions ouvertes (ni P1 ni P2 — à trancher par un humain)

- **Pas de jalon de démo « tranche verticale » avant GATE_DEMO_ISATECH.** La démo ISATECH (lot CMP) dépend de GATE_TOOLKIT, donc du produit COMPLET (11 segments jusqu'au packaging). Pour l'échéance de septembre 2026, il n'existe aucun point de démonstration intermédiaire plus tôt (p. ex. pipeline + console sur fixtures avant l'adaptateur réel). C'est conforme à la décision « la démo est une application du produit » (decisions.md:20), mais c'est un risque d'ordonnancement à assumer consciemment.

- **Self-review : le réviseur et l'implémenteur partagent le même modèle/outil.** `codex-review.ps1` lance `claude --print --model opus` comme réviseur primaire (Codex en repli), c'est-à-dire le même moteur que l'orchestrateur Opus. La gate humaine reste le filet de sécurité (l'IA ne merge jamais dans main), mais une indépendance réelle du réviseur (Codex en primaire, ou un second modèle) renforcerait la valeur de la boucle — surtout une fois le P1 codex-review corrigé.

- **Numéro de renvoi après rejet PA (lié au P1 n°2).** La source legacy étant seule créatrice de numéros (PAB.yaml:44 « pas de resend → recréer avec un numéro unique »), la mécanique exacte du renvoi (ré-extraction du document corrigé côté source vs saisie opérateur) est une décision produit + fiscale à trancher avant d'écrire l'item — à ne pas deviner.
