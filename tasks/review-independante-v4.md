# Review indépendante — Liakont backlog v4
Date : 2026-06-02
Reviewer : agent indépendant (session codex-desktop-20260602)

## Synthèse
- Findings P1 : 28
- Findings P2 : 19
- Verdict global : corrections requises avant lancement orchestration

Le manifest, les lots et le dépôt d'état sont structurellement alignés (`80 ↔ 80 ↔ 80`,
soit 68 items et 12 gates). Le lancement reste bloqué par des dépendances ordonnées à
l'envers, des trous fonctionnels et fiscaux, des specs non réalignées après les décisions
d'architecture, ainsi que des faux verts dans l'outillage de review.

## Findings P1
[P1] | docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md:17 | La spec affirme que DGFiP v3.1 du 31/10/2025 est la référence en vigueur. La page officielle DGFiP publie désormais la v3.2 du 30/04/2026 : https://www.impots.gouv.fr/specifications-externes-b2b | Dépouiller la v3.2 avant orchestration et réconcilier BT, flux 10.x, XSD, swaggers et règles.

[P1] | docs/conception/F10-Console-Admin-WPF.md:86 | F10 décrit encore une console appelant Core et lisant SQLite, contrairement à blueprint.md:106 et CLAUDE.md:88 qui imposent une cliente HTTP limitée à Api + ApiClient. | Réviser F10 avant d'hydrater les items WPF.

[P1] | docs/conception/F11-CLI-Mode-Automatique.md:28 | F11 conserve deux hôtes nominaux interchangeables partageant le Tracking, alors que blueprint.md:108 et orchestration/items/CLI.yaml:16 limitent le CLI au secours quand le Service est arrêté. | Réaligner F11 sur l'architecture Service seul écrivain.

[P1] | blueprint.md:103 | Le blueprint affirme que SQLite suffit définitivement, mais tasks/decisions.md:16 et orchestration/items/TRK.yaml:12 introduisent Dapper, SQL Server Express et un stockage multi-provider. | Arbitrer la doctrine puis modifier blueprint.md ou retirer TRK05.

[P1] | orchestration/manifest.yaml:83 | CFG02 (GatewayConfig, DPAPI, résolution des plug-ins) n'arrive qu'après GATE_CONSOLE_ADMIN, alors que SVC01, API01 et CLI01 en dépendent déjà (orchestration/items/CFG.yaml:42). | Déplacer la fondation de configuration avant service-api ; garder packaging et documentation dans toolkit.

[P1] | orchestration/manifest.yaml:83 | GATE_TOOLKIT peut valider le produit complet sans attendre GATE_PA_B2BROUTER, GATE_PA_SUPERPDP ni GATE_ADAPTER_ENCHERESV6, alors que CFG03 package les plug-ins (orchestration/items/CFG.yaml:65) et DOC.yaml:32 qualifie cette gate de produit complet. | Faire dépendre le packaging final des gates des composants livrés.

[P1] | orchestration/manifest.yaml:90 | Le segment CMP ne dépend pas de GATE_PA_SUPERPDP alors que CMP03 exige la démonstration des capacités B2Brouter vs Super PDP (orchestration/items/CMP.yaml:68). | Ajouter GATE_PA_SUPERPDP au chemin de démo ou retirer explicitement cette démonstration.

[P1] | orchestration/blueprints/wpf-screen-item.yaml:15 | Le blueprint WPF demande de charger les services Core et de planifier leurs appels directs. Il s'applique aux huit items WPF et contredit la frontière App → ApiClient. | Remplacer les mentions Core par endpoints API et méthodes ApiClient.

[P1] | orchestration/blueprints/wpf-screen-item.yaml:52 | Le smoke checklist est créé après commit_apply. Si la review est propre, merge_back intervient sans second commit et laisse ce fichier hors branche. | Déplacer smoke_checklist avant commit_compose ou ajouter un commit après sa génération.

[P1] | orchestration/items/PAA.yaml:54 | PAA02 autorise FakePaClient dans Gateway.Core/PaClient/Testing et l'inclut au produit livré. Core ne doit contenir que l'abstraction PA. | Livrer le faux PA dans un plug-in dédié ou le limiter aux projets de tests.

[P1] | orchestration/items/TVA.yaml:17 | MappingRule ne porte que code régime + part, alors que F03 exige aussi des flags source et un taux de frais potentiellement calculé (docs/conception/F03-Mapping-TVA.md:84). | Étendre le modèle de règle et tester les cas marge/frais.

[P1] | orchestration/items/TVA.yaml:24 | Une table TVA NON VALIDÉE reste chargeable ; aucun critère Pipeline n'interdit explicitement l'envoi production sans validatedBy/validatedDate, malgré TVA05:108 et F03:100. | Ajouter une validation Blocking pré-envoi et un test de refus.

[P1] | orchestration/items/TVA.yaml:19 | La validation structurelle ne bloque que E + 0 % sans VATEX. F04:66 laisse les autres incohérences catégorie/taux en Warning, ce qui permettrait un envoi fiscalement faux. | Définir les combinaisons autorisées depuis la source officielle et les rendre Blocking.

[P1] | orchestration/items/PIP.yaml:72 | PIP03 fige l'imputation prorata et l'envoi paiement alors que l'option TVA sur les débits et OperationCategory Mixte restent à trancher dans F09:82-93. | Piloter l'activation par un paramétrage fiscal validé ; bloquer tant que la décision manque.

[P1] | docs/conception/F07-F08-Avoirs-Frontiere-B2B-B2C.md:68 | Les rectificatifs e-reporting RE annule-et-remplace sont requis, mais PIP03 ne prévoit qu'un agrégat négatif (orchestration/items/PIP.yaml:74), sans flux rectificatif, suivi ni idempotence. | Ajouter modèle, transmission, tracking et tests RE/MO.

[P1] | orchestration/items/PAA.yaml:29 | Le backlog présente un même envoi de paiements pour Flux 10.2 et 10.4 sans modéliser le type de flux ni le routage international, alors que F01-F02:23 les distingue. | Définir les schémas v3.2 exacts puis séparer modèles, sérialisation et tests.

[P1] | orchestration/items/TRK.yaml:26 | PaymentAggregate a un état, mais aucune piste d'audit immuable équivalente aux documents : pas d'événement append-only, snapshot envoyé, réponse PA ni paquet WORM. | Ajouter journal append-only et archivage complet des transmissions 10.2/10.4.

[P1] | docs/conception/F06-Tracking-Piste-Audit.md:63 | La spec exige une alerte si la source change après émission. PIV04 calcule le hash, mais aucun item ne compare une ré-extraction à un document Issued pour journaliser l'altération sans réémettre. | Ajouter détection, événement append-only, alerte et tests.

[P1] | orchestration/items/TRK.yaml:71 | Le Tracking prévoit RejectedByPa → Superseded, mais API/WPF n'exposent aucun renvoi avec nouveau numéro et lien vers l'ancien. F05:111 laisse en outre la numérotation à valider fiscalement. | Obtenir la décision fiscale sourcée puis ajouter moteur, endpoint et action WPF journalisée.

[P1] | docs/conception/F07-F08-Avoirs-Frontiere-B2B-B2C.md:97 | L'avoir orphelin doit pouvoir être traité manuellement, mais API03 n'offre qu'un verdict B2B/B2C. | Ajouter une action générique « traité manuellement » avec motif et événement.

[P1] | orchestration/items/TRK.yaml:75 | TRK03 expose GetPotentiallySentDocuments(), mais PIP01 ne l'utilise pas avant retry d'un TechnicalError. Un POST accepté puis perdu côté réseau peut être doublonné au run suivant. | Ajouter une réconciliation PA obligatoire avant retry ambigu et un test inter-run.

[P1] | orchestration/items/TRK.yaml:134 | Le coffre prévoit facture PA et bordereau source, mais PIP01 ne câble ni GetGeneratedDocumentAsync ni GetAttachments. | Ajouter récupération, archivage/addendum et tests conditionnés par capacités.

[P1] | orchestration/items/TRK.yaml:147 | Les addenda tardifs sont référencés sans règle explicite de chaînage immuable. Une altération d'un tax-report ajouté après émission pourrait échapper à la chaîne initiale. | Définir le chaînage des addenda et tester l'altération de chaque pièce tardive.

[P1] | orchestration/items/TRK.yaml:184 | TRK07 produit un export fiscal par document ou période, mais API03 n'expose que le document et CLI01 uniquement verify-archive. | Ajouter export par période dans API, WPF et CLI.

[P1] | docs/market/Offre-Editeur-Passerelle.md:203 | L'offre inclut réception et e-reporting B2B international à chaque palier. Le backlog n'a aucun lot de réception, annuaire ou Flux 10.1 ; F01-F02:201 classe encore 10.1 en phase 2. | Réduire immédiatement l'offre ou ajouter les lots requis.

[P1] | docs/market/Offre-Editeur-Passerelle.md:43 | L'offre affirme que le moteur génère Factur-X/UBL. Le backlog délègue la génération à la PA et ne prévoit que sa récupération (PAB.yaml:62, TRK.yaml:134). | Corriger la promesse ou ajouter la génération locale.

[P1] | tools/codex-review.ps1:166 | Le script considère la review réussie dès que le processus reviewer retourne 0 ; il ne parse jamais les findings P1/P2. Une review contenant des P1 passe donc pour propre et le blueprint continue vers merge_back. | Capturer la sortie, détecter les findings, journaliser et retourner un code non nul tant qu'ils ne sont pas traités.

[P1] | tools/orch-state.ps1:128 | update accepte tout statut sans machine à états ni validation du type item/gate. Une commande erronée peut promouvoir pending → done ou une gate vers un statut arbitraire. | Implémenter transitions autorisées, statuts validés et tests de rejet.

## Findings P2
[P2] | orchestration/manifest.yaml:157 | SVC01 doit déjà démarrer l'API, mais API01 n'arrive qu'après SVC01. | Limiter SVC01 au bootstrap ou déplacer l'intégration API après API01.

[P2] | orchestration/manifest.yaml:205 | GATE_PROD_CMP devient prête dès que CMP04 est done, alors que CMP04 exige seulement une checklist renseignée et non des prérequis cochés. | Bloquer CMP04 tant que les prérequis obligatoires ne sont pas satisfaits ou ajouter une gate externe dédiée.

[P2] | orchestration/items/CFG.yaml:15 | CFG01 ne spécifie que les groupes Windows lecture/action ; API04:102 ajoute un troisième droit paramétrage. | Ajouter le groupe paramétrage à F12 et à check-config.

[P2] | orchestration/items/TRK.yaml:19 | Les colonnes monétaires persistées n'imposent aucun type SQL fixe. | Exiger stockage decimal exact et tests round-trip sans float/double.

[P2] | orchestration/items/TRK.yaml:90 | TRK04 impose les snapshots pour Issued mais pas la conservation brute des réponses RejectedByPa prévue par F06. | Archiver chaque tentative, succès ou rejet.

[P2] | orchestration/items/ADP.yaml:38 | La lecture seule est déclarée, mais les critères ne prouvent pas l'absence d'écriture, verrou explicite ou transaction modifiante. | Tester avec compte DB lecture seule et journal de commandes.

[P2] | orchestration/items/VAL.yaml:50 | VAL03/VAL04 n'explicitent pas le contrôle Total passerelle = Total source ni la cohérence taux/catégorie de F04:54 et F04:66. | Ajouter règles, sévérités et tests.

[P2] | docs/market/Offre-Editeur-Passerelle.md:180 | Le kit support éditeur promis n'est pas couvert : dashboard support, FAQ, formation, matrice d'escalade/SLA. | Ajouter un item documentation/support avant le premier déploiement en grappe.

[P2] | orchestration/items/TRK.yaml:173 | TRK07 demande une implémentation OpenTimestamps complète (création, upgrade, vérification) et RFC 3161 sans dépendance identifiée. Ce n'est pas un simple POST HTTP. | Faire un spike, choisir bibliothèque ou implémentation, et produire l'ADR de dépendance.

[P2] | orchestration/items/TRK.yaml:213 | TRK08 exige l'extraction de texte PDF sans OCR, mais aucun parseur PDF net48 ni ADR n'est prévu. | Choisir une dépendance ou restreindre le matching au nom/date/montant.

[P2] | orchestration/items/WPF.yaml:196 | WPF08 exige un aperçu PDF intégré, mais WPF/net48 n'offre pas de visionneuse PDF native et aucun choix technique n'est cadré. | Décider composant, WebView ou ouverture externe ; ajouter ADR si package.

[P2] | orchestration/items/API.yaml:12 | API01 est réaliste avec HttpListener + auth Windows, mais l'ADR demandé ne cite pas URLACL, compte de service, firewall, SPN/Negotiate ni clients hors domaine. | Inclure ces prérequis dans l'ADR et les tests d'installation réseau.

[P2] | tools/verify-fast.ps1:69 | manifest-sanity ne vérifie ni réciprocité manifest/lots/state, ni cycles, gates, blueprints, priorités ou chemins de specs. | Étendre SOL02 avec les contrôles structurels utilisés par cette review.

[P2] | tools/verify-fast.ps1:96 | Le build n'impose ni x86 ni x64 alors que SOL01:44 exige les deux plateformes. | Construire explicitement les deux plateformes.

[P2] | tools/run-tests.ps1:19 | L'absence de solution retourne 0 sans distinguer bootstrap légitime et suppression accidentelle après SOL01. | Autoriser bootstrap seulement avant SOL01 ou avec flag explicite.

[P2] | tools/run-tests.ps1:27 | Le filtre n'exclut pas Integration.SqlServer alors que SOL02:60 déclare cette suite optionnelle selon disponibilité LocalDB. | Exclure explicitement cette catégorie du run standard.

[P2] | tools/orch-state.ps1:116 | claim accepte une sous-branche absente et enregistre `unknown`, ce qui contourne la convention `<segment-branch>-<item_id>`. | Rendre Subbranch obligatoire et valider le format.

[P2] | orchestration/protocol.md:104 | Le protocole demande d'initialiser state.yaml s'il manque, mais orch-state.ps1:38 échoue immédiatement et aucune commande init n'existe. | Ajouter une commande init atomique ou supprimer cette branche du protocole.

[P2] | orchestration/MANIFEST-CONVENTIONS.md:32 | Les archives de segments sont décrites comme `archive/` dans le dépôt source alors que protocol.md:28 les place dans `$ORCH_REPO/archive/`. | Choisir un emplacement unique et supprimer les répertoires historiques ambigus.

## Dimensions sans finding
- A — cardinalités et références structurelles : Aucun finding. Manifest, lots et state sont alignés `80 ↔ 80 ↔ 80`; aucune dépendance manquante, aucun cycle, aucune inversion de priorité intra-segment.
- B — CMP limité au paramétrage : Aucun finding. Les items CMP restent sous `deployments/cmp/` et interdisent explicitement les modifications de `src/`.
- F — garde-fous déjà présents : BR-CO-15 Blocking, arrondi half-up, decimal côté pivot, DocumentEvent append-only, coffre sans suppression applicative, absence d'OCR V1 et limite « pas de certification NF Z42-013 » sont explicités.
- H — encodage PowerShell : Aucun finding. Les cinq scripts `.ps1` ont un BOM UTF-8 et orch-state utilise explicitement UTF-8 pour state.yaml.

## Questions ouvertes (ni P1 ni P2 — à trancher par un humain)
- Quel statut commercial annoncer tant que réception, Flux 10.1 et plug-in Super PDP ne sont pas dans le chemin de release ?
- Le produit doit-il réellement supporter SQL Server Express dès V1, ou SQLite local reste-t-il la doctrine ?
- Quel composant net48 retenir pour extraction et aperçu PDF ?
- Quelle bibliothèque et quelle politique de confiance retenir pour OpenTimestamps et RFC 3161 ?
- Faut-il mettre à jour les références juridiques avant production pour tenir compte de la recodification applicable au 1er septembre 2026 ?
