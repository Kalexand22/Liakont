# Review indépendante — Conformat backlog v6
Date : 2026-06-03
Reviewer : agent indépendant (session Codex)

## Synthèse
- Findings P1 : 13
- Findings P2 : 16
- Verdict global : corrections requises avant lancement orchestration

## Findings P1
[P1] | orchestration/items/SOL.yaml:21 | SOL01 demande de vendoriser `Stratum.Modules.Identity` sans aucun module ERP, mais `C:\Source\Stratum\src\Modules\Identity\Application\Stratum.Modules.Identity.Application.csproj:10` référence `../../Party/Contracts/Stratum.Modules.Party.Contracts.csproj` et `CreateUserHandler.cs:11-17` consomme `IPartyQueries`. Le périmètre "Identity/Job/Notification/Audit seulement" ne compile pas sur pièce. | Soit inclure explicitement `Party.Contracts` et son adaptation/provenance dans SOL01, soit découpler Identity de Party avant vendoring, avec test de build du socle vendored.

[P1] | orchestration/manifest.yaml:104 | PIV04 exige l'authentification par clé API agent (`orchestration/items/PIV.yaml:106`), mais PIV05, qui crée le cycle de vie des agents et le middleware d'authentification, arrive après PIV04 et dépend de PIV04. L'ingestion devra être stubée ou non authentifiée, ce qui contredit l'isolation tenant. | Extraire l'auth agent dans un item antérieur ou inverser la dépendance : PIV05 avant PIV04, puis PIV04 dépend de PIV05.

[P1] | orchestration/manifest.yaml:116 | VAL02 vérifie la cohérence entre document et SIREN du profil tenant (`orchestration/items/VAL.yaml:44`), mais le profil tenant est créé par CFG02 (`orchestration/items/CFG.yaml:45`), qui n'est pas une dépendance et arrive plus tard. | Ajouter CFG02 comme dépendance de VAL02, ou déplacer le contrat minimal du profil tenant avant VAL02.

[P1] | orchestration/items/PIV.yaml:16 | `CreditNoteRefs` est défini comme `List<string>`, alors que F07-F08 impose une liste `{ Number, IssueDate }` (`docs/conception/F07-F08-Avoirs-Frontiere-B2B-B2C.md:88`) et B2Brouter exige `amended_number` + `amended_date` (`docs/conception/F05-Client-API-B2Brouter.md:32`). Le contrat agent↔plateforme perd la date obligatoire des avoirs. | Remplacer par un DTO typé `PivotDocumentRefDto { Number, IssueDate, SourceReference? }` et couvrir avoir simple, partiel et groupé dans les golden files.

[P1] | orchestration/items/CFG.yaml:19 | Le paramétrage fiscal tenant ne prévoit pas le régime/cadence de TVA nécessaire au e-reporting des paiements, alors que F09 définit les fréquences selon régime (`docs/conception/F09-E-Reporting-Paiement.md:23`) et laisse le régime CMP à confirmer (`F09:89`). PIP ne peut pas décider quand envoyer Flux 10.2/10.4. | Ajouter un champ de régime/cadence déclarative par tenant ; valeur absente = transmission paiement bloquée avec message opérateur français et action corrective.

[P1] | orchestration/items/PIP.yaml:92 | PIP03 impose une imputation "prorata HT frais / HT total", alors que F09 présente cette option puis indique une agrégation jour×taux à valider (`docs/conception/F09-E-Reporting-Paiement.md:66-70`, décision `F09:93`). C'est une règle fiscale inventée dans le backlog. | Ne pas hard-coder le prorata ; rendre la méthode d'imputation paramétrée/sourcée, et bloquer les transmissions paiement tant que la décision n'est pas validée.

[P1] | orchestration/items/OPS.yaml:166 | OPS06 ajoute une suppression de tenant après export. Cela crée un chemin de suppression de tracking/archive alors que le coffre WORM interdit toute modification/suppression (`orchestration/items/TRK.yaml:165`) et que la conservation fiscale reste un engagement produit. | Remplacer par désactivation/résiliation logique ; tout effacement légal éventuel doit préserver audit, preuves d'export et obligations de rétention.

[P1] | orchestration/items/AGT.yaml:140 | AGT04 accepte l'authenticité d'auto-update par hash SHA-256 publié via la même API que `updateUrl`, alors que F12 décrit un "package signé" (`docs/conception/F12-Architecture-Plateforme-Agent.md:103-105`). En cas de compromission plateforme/API, le hash ne protège pas le code exécuté chez le client. | Exiger un manifeste/package signé en V1, documenter le modèle de confiance par ADR, et tester signature invalide/refus/rollback.

[P1] | orchestration/items/PIV.yaml:155 | PIV05 expose `latestAgentVersion`, `updateRequired`, `updateUrl + hash` et AGT04 les consomme, mais aucun item ne publie les packages d'update, le registre de versions, le canal HTTPS ou la version attendue par tenant/instance. OPS05 ne couvre que l'installeur. | Étendre OPS05/PIV05 ou créer un item dédié : stockage packages versionnés, manifeste signé, publication, version cible, hash/signature, tests heartbeat/426 → téléchargement → update.

[P1] | orchestration/items/API.yaml:5 | Les items API/WEB/SUP utilisent `conformat.read`, `conformat.actions`, `conformat.settings` et `conformat.supervision`, mais aucun item ne définit le seed Keycloak, les rôles standard, la matrice permissions→rôles, ni le mapping utilisateur→tenant initial. OPS03 dit seulement "compte + rôle" (`orchestration/items/OPS.yaml:73`). | Ajouter au socle/provisioning les permissions applicatives, rôles lecture/actions/paramétrage/supervision, seed realm/client roles, mapping tenant et tests API/E2E multi-rôles.

[P1] | tools/verify-fast.ps1:64 | Si `$ORCH_REPO/state.yaml` est absent ou pointe au mauvais dépôt, `Test-SolItemPending` retourne `true`; les solutions manquantes sont alors traitées comme "SOL pending" et peuvent être skippées, contrairement au protocole qui rend state.yaml obligatoire. | Échouer immédiatement si le state repo ou state.yaml est absent/illisible ; rendre le bootstrap pré-SOL explicite via option dédiée.

[P1] | tools/run-tests.ps1:43 | `run-tests.ps1` initialise `$overallExit = 0` et peut terminer `PASS` sans exécuter aucune suite quand les deux solutions sont absentes et considérées pending. C'est un faux vert de test complet. | Compter les suites/tests exécutés et échouer si zéro suite ou zéro test hors mode bootstrap explicite.

[P1] | tools/run-tests.ps1:69 | Le script déclare inclure les E2E Playwright, mais il ne démarre ni Host, ni PostgreSQL, ni Keycloak, n'installe les navigateurs, ni ne vérifie les prérequis/base URL. Les pages Blazor peuvent donc avoir des E2E présents mais jamais exécutables. | Ajouter un harness E2E explicite (ou `run-e2e.ps1`) qui démarre l'instance de test et échoue si les prérequis Playwright/Keycloak sont absents.

## Findings P2
[P2] | orchestration/items/VAL.yaml:62 | `SourceTotalsRule` rend l'écart total plateforme/source Blocking, alors que F04 le classe en alerte et confirme "alerte" dans les décisions (`docs/conception/F04-Controles-Qualite-Validation.md:60`, `F04:128`). | Aligner VAL03 sur Warning ou amender F04 avec une décision datée si le durcissement est voulu.

[P2] | orchestration/items/PIV.yaml:53 | PIV02 place `CanonicalJsonWriter` et `PayloadHasher` dans `Conformat.Agent.Contracts`, alors que PIV01/SOL02 décrivent l'assembly comme DTOs purs sans logique ni package. | Séparer DTOs et utilitaires techniques, ou préciser que "aucune logique" signifie "aucune logique métier" et tester cette exception.

[P2] | orchestration/items/SOL.yaml:43 | SOL01 exclut le code Stratum copié du périmètre de review, mais aucun contrôle mécanique ne vérifie que les fichiers vendored sont byte-identiques au commit source ou que les modifications locales sont consignées. | Ajouter un vérificateur de provenance qui compare la copie au commit Stratum et échoue sur modification non listée.

[P2] | orchestration/manifest.yaml:202 | OPS03 fournit un package agent pré-configuré dans l'assistant tenant (`orchestration/items/OPS.yaml:74-75`), mais ne dépend pas d'OPS05, qui produit ce packaging. | Faire dépendre OPS03 d'OPS05 ou scinder l'étape package agent dans un item post-OPS05.

[P2] | orchestration/manifest.yaml:205 | OPS06 expose l'export depuis l'écran Clients d'OPS03 (`orchestration/items/OPS.yaml:156-157`), mais ne dépend pas d'OPS03. | Ajouter `OPS06 -> OPS03` et un E2E "Clients → Exporter ce client → bundle vérifiable".

[P2] | orchestration/items/OPS.yaml:79 | La création automatique de base tenant est demandée, mais le backlog ne définit pas clairement le catalogue système/instance qui permet de lister et créer des tenants avant que leur base existe. | Spécifier le schéma/base système, transaction de création, migrations DbUp par tenant et rollback.

[P2] | orchestration/items/OPS.yaml:25 | L'ADR Keycloak est repoussée à OPS01, mais SOL01 exige déjà un Host de dev avec PostgreSQL + Keycloak (`orchestration/items/SOL.yaml:39`). Une décision d'empreinte/coût arrive après que la fondation l'a figée. | Déplacer l'ADR/mesure Keycloak dans SOL01 ou rendre SOL01 explicitement provisoire sur l'auth.

[P2] | tools/verify-fast.ps1:181 | La vérification locale ne build l'agent qu'en x86 ; `run-tests.ps1` lance `dotnet test` sans build x64 explicite. Or SOL02/OPS05 exigent x86 et x64. | Ajouter un build/test x64 dans `run-tests.ps1` ou une gate obligatoire de packaging x86+x64.

[P2] | tools/codex-review.ps1:301 | Les acceptances autorisent "accepted P2s", mais `codex-review.ps1` sort 2 sur tout P2 sans mécanisme d'acceptation/allowlist. Une session avec P2 accepté ne peut pas devenir clean. | Ajouter un mécanisme explicite d'acceptation traçable ou modifier les acceptances pour exiger la correction de tous les P2.

[P2] | tools/orch-state.ps1:44 | Le verrou d'état est un mutex Windows nommé, valable seulement sur la même machine, alors que le protocole décrit un `$ORCH_REPO` partagé et stocke les hostnames. Des agents cross-host pourraient écrire `state.yaml` simultanément. | Utiliser un lock file dans `$ORCH_REPO` avec création exclusive, ou documenter/enforcer orchestration mono-hôte.

[P2] | docs/architecture/definition-of-done.md:21 | La DoD active parle encore d'items WPF et de `wpf-screen-item`, alors que v6 utilise Blazor + Playwright. | Remplacer la section WPF par exigences Blazor : bUnit, Playwright, handlers MediatR, pas d'accès DB direct.

[P2] | tools/build-agent-context.ps1:60 | L'hydratation de contexte garde les lots `SVC`/`WPF` et route API/DOC vers F10/F11 avec exemples WPF/Gateway, ce qui peut réintroduire des hypothèses v5 dans les sessions v6. | Mettre à jour la table lots→docs pour AGT/SUP/OPS/WEB et supprimer les lots supprimés.

[P2] | orchestration/MANIFEST-CONVENTIONS.md:46 | Les conventions citent encore `wpf-screen-item`, qui n'existe plus ; le blueprint réel v6 est `blazor-page-item`. | Remplacer par `blazor-page-item`.

[P2] | orchestration/items/TRK.yaml:189 | La v5 exposait une commande explicite `verify-archive`; v6 garde `ArchiveVerifier` mais ne garantit qu'un "outil/procédure" via OPS06. L'export fiscal doit rester vérifiable hors plateforme. | Ajouter un livrable concret (`tools/verify-archive.ps1` ou CLI) packagé avec OPS06/DOC, retour non zéro en cas d'altération.

[P2] | docs/market/Offre-Editeur-Passerelle.md:184 | L'offre impose FAQ/base de connaissances, formation support éditeur, matrice d'escalade, SLA et canal ticket, mais DOC01 ne couvre que guides utilisateur/admin. | Ajouter un item DOC/OPS "kit support éditeur" avec FAQ, formation N1, matrice N1/N2/N3, SLA/canal et workflow d'escalade.

[P2] | docs/market/Offre-Editeur-Passerelle.md:205 | La veille réglementaire est promise dans chaque palier, mais aucun item ne définit sources surveillées, cadence, analyse d'impact, release notes ni régression quand DGFiP/AFNOR évolue. | Ajouter un item OPS/DOC de veille réglementaire : sources, rythme, checklist d'impact, communication client et non-invention de règles fiscales.

## Dimensions sans finding
- Aucune dimension entièrement sans finding.

## Questions ouvertes (ni P1 ni P2 — à trancher par un humain)
- Le backlog v6 doit-il couvrir les livrables commerciaux OE-3/OE-4/OE-5 (contrat type avocat, one-pager, liste éditeurs qualifiés), ou ces éléments restent-ils hors orchestration produit ?
- L'authenticode des packages agent est-il accessible dès V1, ou faut-il accepter temporairement un manifeste signé par clé applicative dédiée ?
