# Validation Module

> Module métier Liakont (namespace `Liakont.Modules.Validation.*`). Spec : `docs/conception/F04-Controles-Qualite-Validation.md`. Item fondateur : **VAL01** (framework). Règles métier : VAL02-VAL05.

## Purpose

Détecte **avant l'envoi** tout ce qu'une Plateforme Agréée (PA) rejetterait après envoi (F04 §1) :
position `Extract → CHECK → Send`. Un document qui échoue à un contrôle **bloquant** reste `Blocked`
et n'est jamais transmis (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3).

VAL01 livre le **socle** : le contrat d'une règle (`IDocumentRule`), le résultat agrégé
(`ValidationResult` / `ValidationIssue` / `ValidationSeverity`) et le moteur qui exécute les règles
et agrège leurs anomalies (`ValidationPipeline`). Les règles concrètes arrivent ensuite (identité
VAL02, cohérence VAL03, TVA/avoirs VAL04, garde-fou B2B/B2C VAL05).

Le module **détecte**, il ne **corrige jamais** les données (frontière `Validation` — `module-rules.md` §2).
Toute la logique de validation vit sur la **plateforme**, jamais dans l'agent (CLAUDE.md n°6).

## Boundaries

- **Owns:** le contrat de règle et le moteur d'agrégation (`Contracts` + `Domain`). Aucun schéma de
  base de données : VAL01 est un framework en mémoire, sans persistance propre.
- **Reads:** le document à valider est fourni en entrée (modèle pivot `Liakont.Agent.Contracts.Pivot`,
  lecture seule). Les règles qui ont besoin de paramétrage tenant (profil émetteur, table TVA) le
  liront via les **Contracts** des modules concernés (TenantSettings, TvaMapping), scopé par `CompanyId`.
- **Does NOT:** ne corrige/ne normalise jamais les données (détection seule) ; n'affaiblit jamais une
  anomalie bloquante en alerte (CLAUDE.md n°3) ; n'invente aucune règle fiscale (toute catégorie/seuil
  vient de `docs/conception/F*.md` — CLAUDE.md n°2) ; ne référence aucun module hors de ses `Contracts`.

## Decisions VAL01

- **`IDocumentRule.ValidateAsync` est asynchrone** (la spec F04 §5 illustre une signature synchrone).
  Motif : les règles aval interrogent d'autres modules (unicité du numéro via Documents — VAL03 ;
  couverture du mapping via TvaMapping — VAL04). Un contrat synchrone forcerait une rupture de contrat
  deux items plus loin. La méthode reste pure (sans effet de bord) et retourne la liste vide si conforme.
- **Garantie « jamais de règle silencieuse »** : une règle qui lève une exception est convertie par le
  pipeline en anomalie **bloquante** `RULE_CRASHED` (jamais un passage en succès — CLAUDE.md n°3).
  L'annulation (`OperationCanceledException`) est propagée, jamais convertie en `RULE_CRASHED`.
- **`ValidationResult.IsValid`** (nommage de l'item VAL01) = absence d'anomalie bloquante ; `HasBlockingIssue`
  est l'inverse (le « IsBlocking » de F04 §5). Les alertes (Warning) n'invalident pas le document (F04 §3.3).
- **Persistance différée :** la persistance des anomalies avec le document (F04, console WEB03) relève du
  module **Documents** (lot TRK), absent à ce stade. VAL01 ne porte aucune persistance.

## Decisions VAL02

- **Validateurs élémentaires dans `Domain/Identity`** (`SirenValidator`, `SiretValidator`,
  `FrenchVatNumberValidator`, `CountryCodeValidator`) : fonctions pures, sans dépendance. Algorithmes
  et dérogations (Luhn, exception La Poste 356000000, clé TVA, liste ISO 3166-1 alpha-2) viennent de
  F04 §3.1/§3.2/§4.1/§4.2 — aucune règle inventée (CLAUDE.md n°2). Ils remplaceront à terme la copie
  temporaire `TenantSettings.Domain.Services.SirenValidator` (CFG02) ; consolidation hors périmètre
  VAL02 (frontière inter-modules : TenantSettings ne peut pas référencer `Validation.Domain`).
- **Le SIREN émetteur de référence vient du profil tenant**, lu via `ITenantSettingsQueries`
  (`TenantSettings.Contracts`) scopé par `CompanyId` (note v6, item VAL02) — pas du document. La règle
  vérifie présence + validité + cohérence document↔profil ; le **SIRET émetteur** porté par le document
  est validé s'il est fourni (F04 §3.1). Tout écart est **bloquant** (CLAUDE.md n°3).
- **`FrenchVatNumberValidator` est une brique réutilisable**, pas une règle bloquante VAL02 : F04 §3
  ne liste aucun contrôle de **format** de n° de TVA. Il est livré au titre de l'item VAL02 (point 3)
  et consommé par VAL05 (indice « société » fort) et par un futur contrôle de cohérence TVA. La
  vérification « SIREN intégré au n° TVA = SIREN émetteur » (F04 §4.2) relèvera de cette règle future,
  pas du validateur seul — c'est tracé ici pour éviter toute fausse impression de couverture.
- **`SupplierIdentityRule` / `BuyerIdentityRule`** implémentent `IDocumentRule` (asynchrone côté
  émetteur car il lit le profil tenant ; pur côté acheteur). **Câblage différé** : l'enregistrement DI
  des règles et le branchement du `ValidationPipeline` dans le pipeline d'envoi (lot PIP) ne sont pas
  portés par VAL02 — cohérent avec la posture « persistance/câblage différés » de VAL01. Les règles
  sont consommées par le `ValidationPipeline` existant une fois enregistrées.
- **Code pays = liste ISO 3166-1 alpha-2 officielle (249 codes) embarquée**. `BUYER_COUNTRY_INVALID`
  étant bloquant, une liste incomplète provoquerait un faux blocage : la liste est figée et testée
  (échantillons valides/invalides). Le Kosovo (XK, user-assigned) n'est pas reconnu — évolution par
  mise à jour explicite, jamais devinée. **Compromis V1 assumé** (F04 §6, décision 1/2) : on n'embarque
  pas le moteur genericode/Schematron normatif en V1 ; la dérivation de la codelist genericode versionnée
  (source unique, détection de dérive) est un chantier **phase 2**, au même titre que les artefacts
  FNFE-MPE (F04 §6, décision 2). Le risque de dérive ISO est faible (révisions rares) et borné par les tests.

## Decisions VAL03

- **Règles de cohérence du document (F04 §3.3)** dans `Domain/Rules/` : `LineTotalsRule`
  (Σ lignes HT/TVA = totaux, BLOQUANT), `ArithmeticRule` (BR-CO-15 : TTC = HT + TVA, BLOQUANT, fatale
  EN 16931), `SourceTotalsRule` (total passerelle ≠ total source = ALERTE, décision F04 #3/D5),
  `StructureRule` (≥ 1 ligne, date plausible, devise ISO 4217), `UniquenessRule` (numéro présent et
  non déjà émis pour le tenant).
- **Réconciliation SANS tolérance, en `decimal`, après arrondi half-up 2 décimales** via
  `PivotRounding.RoundAmount` (CLAUDE.md n°1 ; EN 16931 : aucune tolérance sur l'arithmétique). Un
  écart d'un centime BLOQUE — jamais de rattrapage silencieux.
- **Niveaux de sévérité repris de la spec, jamais durcis ni affaiblis** (CLAUDE.md n°2, n°3) :
  l'écart passerelle/source est un Warning PARCE QUE la spec le dit (F04 §3.3, décision #3), pas par
  affaiblissement ; date future = BLOQUANT, date < 2000 = ALERTE (décision F04 #4).
- **Devise validée contre un référentiel ISO 4217** (`Iso4217Currencies`) — donnée de référence
  (genericode v17.0, F04 §2.4), pas une règle fiscale inventée. Comparaison insensible à la casse.
- **Horloge injectée (`TimeProvider`)** dans `StructureRule` pour une détection « date dans le
  futur » déterministe et testable.
- **Anti-doublon via le port `IIssuedDocumentLookup`** (Contracts) : Validation DÉCLARE le besoin,
  l'implémentation réelle est livrée par le module Documents/Tracking (TRK03). Tant qu'il n'existe
  pas, un faux d'essai suffit (acceptance VAL03). La frontière Contracts-only est préservée.
- **Pas encore de câblage DI** : comme VAL01, les règles sont des classes `Domain` testées en
  instanciation directe. Leur enregistrement dans le conteneur (pour alimenter `ValidationPipeline`)
  se fera au moment où le pipeline d'envoi est composé (Host / lot PIP) — prématuré ici.

## Decisions VAL04

- **Les règles TVA valident le pivot DÉJÀ MAPPÉ (post-F03).** `CategoryCode` / `Rate` / `VatexCode`
  d'une ventilation sont le RÉSULTAT du mapping plateforme (l'agent les laisse toujours nuls — frontière
  contrat). Conséquence assumée et FAIL-SAFE : une catégorie non résolue à ce stade = régime non mappé →
  `MappingCoverageRule` bloque (jamais d'envoi à l'aveugle, CLAUDE.md n°3). Le pipeline (PIP01) mappe
  avant de valider.
- **Détection d'un avoir = présence de `CreditNoteRefs`** (signal structurel EN 16931 BG-3 / BT-25). La
  classification générale facture/avoir à partir du type source brut (`SourceDocumentKind`) est un concern
  plateforme DISTINCT, non encore bâti et NON spécifié (la correspondance type-source → avoir varie par
  logiciel) : VAL04 ne l'invente pas (CLAUDE.md n°2). Le cas orphelin couvert ici est l'avoir qui
  RÉFÉRENCE un original absent de la plateforme (F07-F08 §B.4, cas réel : original pré-réforme / hors
  passerelle).
- **Lookup de la facture d'origine derrière une abstraction.** `IIssuedInvoiceLookup`
  (`Contracts/CreditNotes`) tranche `KnownIssued` / `KnownNotIssued` / `Unknown` (F07-F08 §B.5),
  tenant-scopé par `CompanyId`. L'implémentation réelle (module Documents / suivi des émissions) arrive
  avec **TRK03** ; jusque-là un double de test suffit (même schéma que l'unicité de VAL03). Frontière
  Contracts-only : la règle ne référence jamais le module Documents directement (module-rules.md §3).
- **Aucune règle fiscale inventée (CLAUDE.md n°2) :** liste des 9 catégories UNCL5305 = F03 §2.1 ;
  cohérence catégorie/taux en BLOCKING = F04 §3.4 (amendée 2026-06-02) ; VATEX obligatoire sur E à taux 0 =
  F04 §3.4 ; montants positifs sur avoir = F07-F08 §B.2. QUESTION OUVERTE tracée (catégorie AA/AAA à
  taux 0 légitime ?) : déférée à l'expert-comptable ; la règle applique la spec actuelle, à AMENDER
  jamais à assouplir silencieusement.
- **Pas de wiring DI dans VAL04.** Les règles implémentent le contrat publié `IDocumentRule` ; leur
  enregistrement (avec l'implémentation réelle de `IIssuedInvoiceLookup`) est branché par le consommateur
  du pipeline (PIP01), comme pour le socle VAL01. Aucun nouveau projet ni modification de la solution.

## Decisions VAL05

- **Garde-fou B2B/B2C, pas reclassement** (F08) : `BuyerLooksProfessionalRule` DÉTECTE un acheteur
  qui semble professionnel et **bloque** (`BUYER_LOOKS_PROFESSIONAL`, Blocking) ; elle ne reclasse ni
  ne corrige jamais le document. Le verdict opérateur (« confirmer B2C » / « traiter manuellement »)
  et sa journalisation relèvent de l'endpoint verdict (API02), de la console (WEB03) et de la piste
  d'audit (lot TRK) — **câblage différé**, comme VAL01/VAL02. VAL05 livre la détection + l'anomalie
  qui déclenche ce verdict, pas la persistance du verdict.
- **Toute l'heuristique vit sur la plateforme** (`CompanyHintDetector`, `Domain/Detection`). L'agent
  ne transmet que des champs source BRUTS (`PivotPartyDto.IsCompanyHint` = transcription du champ
  `societe`) — aucune décision côté agent (frontière agent/plateforme, amendement F01-F02 du
  2026-06-03 ; CLAUDE.md n°6). Les indices et la liste de formes juridiques sont EXACTEMENT ceux de
  F07-F08 §A.4 : 2 indices FORTS (`societe` brut ; n° de TVA intracommunautaire présent), 1 indice
  MOYEN (forme juridique). `LooksProfessional` = au moins un indice fort OU la forme juridique (la
  spec ne définit qu'un seul indice moyen — un seuil « deux indices moyens » serait inatteignable).
- **Forme juridique = liste figée et non extensible** : `SARL, SAS, SA, EURL, EI` (F07-F08 §A.4),
  repérées par `[GeneratedRegex]` avec limites de mot (`\b`) pour ne pas matcher en sous-chaîne
  (« EI » dans « BEIGNET », « SA » dans « SABATIER »). Le « … » de la spec est traité comme
  illustratif : ajouter une forme serait inventer une règle (CLAUDE.md n°2) → amendement de spec requis.
- **n° de TVA « présent », pas « valide »** : F07-F08 §A.4 dit « présent ». On ne filtre pas via
  `FrenchVatNumberValidator` (qui rejetterait un n° étranger ou non encore vérifié) : un n° de TVA
  renseigné, fût-il étranger, signale tout autant un professionnel ; filtrer sous-détecterait et
  affaiblirait le garde-fou (CLAUDE.md n°3). `FrenchVatNumberValidator` reste réservé à un futur
  contrôle de cohérence de format (note VAL02), distinct de ce garde-fou.
- **Le montant n'est jamais un critère** (F07-F08 §A.4) : garanti **structurellement** — le détecteur
  ne reçoit que l'acheteur (`PivotPartyDto`), jamais les totaux. Un test le verrouille (bordereau à
  montant élevé + acheteur particulier = aucune anomalie).

## Decisions RD404 (cohérence des rôles de tiers — finding RD4-09)

- **Contrôle d'INTÉGRITÉ de données, pas une règle fiscale (CLAUDE.md n°2).** Le pivot porte
  `IsSelfBilled` / `Invoicer` (auto-facturation 389, ADR-0004 D3-6) et `Payee` (affacturage BG-10), et le
  pipeline consomme déjà `IsSelfBilled` (garde d'émission 389, MND07), mais AUCUNE règle n'en contrôlait la
  cohérence. `PartyRoleConsistencyRule` la rétablit sans inventer de règle : **F15 §1.8** ferme l'existence
  d'un contrôle CTC propre au 389 (au-delà de G1.01 type admis et G1.42/G1.45 numérotation) et range
  l'identification de l'émetteur matériel dans les **règles de rôle générales** — exactement comme l'identité
  émetteur/acheteur (VAL02).
- **Auto-facturation ⇒ émetteur matériel présent ET identifié.** Une 389 est, par définition (UNTDID 1001,
  F15 §1.2 ; art. 289 I-2 CGI, F15 §1.1), émise par un tiers DISTINCT du vendeur. `Invoicer` null = identique
  au vendeur (contrat) : un `IsSelfBilled=true` sans `Invoicer` est donc internement incohérent → BLOQUANT
  (`SELF_BILLED_INVOICER_MISSING`). « Identifié » = SIREN BT-30 présent et valide (clé de Luhn,
  `SirenValidator`) → sinon BLOQUANT (`SELF_BILLED_INVOICER_UNIDENTIFIED`). « Bloquer plutôt qu'envoyer faux »
  (CLAUDE.md n°3) : un 389 projetterait un type de document distinct du 380 standard.
- **Réciproque : `Invoicer` présent ⇒ `IsSelfBilled` cohérent.** Un émetteur de facture distinct du vendeur
  relève de l'auto-facturation / facturation pour compte de tiers (ADR-0004 D3-6) ; le porter sans marquer
  l'auto-facturation est incohérent → BLOQUANT (`INVOICER_WITHOUT_SELF_BILLED`).
- **`Payee` (affacturage BG-10) = DIFFÉRÉ EXPLICITE (RD409).** Le champ est au contrat et au hash canonique
  mais INERTE : aucun sérialiseur PA ne le projette en V1 (grep `src/PaClients` Payee = 0). Décision RD404 :
  ne PAS inventer une projection affacturage (non sourcée — F15 §6.5/§6 ouvre l'articulation 393/396), mais
  ne pas non plus laisser un champ contractuel passer pour transmis alors qu'il est inerte. Sa présence est
  donc SIGNALÉE à l'opérateur (`PAYEE_NOT_TRANSMITTED`, **Warning** — jamais bloquant, donnée optionnelle),
  et le différé est tracé pour l'addendum RD409 (`tasks/redline-adr-0004.md`).
- **Règle PURE, aucune dépendance, aucune écriture.** Détecte seulement (INV-VALIDATION-007). Enregistrée
  ligne par ligne dans `ValidationModuleRegistration` (ensemble explicite et auditable).

## Published Events

Aucun. (Le résultat de validation est consommé par le pipeline d'envoi — lot PIP — via les `Contracts`.)

## Consumed Events

Aucun.

## Dependencies

- `Liakont.Agent.Contracts` (modèle pivot du document en entrée — `PivotDocumentDto`, `PivotRounding`).
- `Liakont.Modules.TenantSettings.Contracts` (VAL02) : lecture du profil émetteur du tenant
  (`ITenantSettingsQueries.GetTenantProfile`), scopée par `CompanyId`. Frontière inter-modules
  **Contracts-only** respectée (`module-rules.md` §3).
- Le besoin d'unicité de numéro (VAL03) est exprimé par le port `IIssuedDocumentLookup` (dans
  **Validation.Contracts**), implémenté à l'exécution par le module Documents/Tracking (TRK03) —
  inversion de dépendance (aucune référence sortante de Validation pour ce besoin).
