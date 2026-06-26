# Bugs recette EncheresV6 / e-reporting B2C — backlog agents

Remontés par Karl pendant la recette hands-on (24/06/2026, branche `feat/ereporting-b2c`).
Un bug = une tâche agent. Format : symptôme → repro → diagnostic → fichiers → critère d'acceptation.

---

## ÉTAT — run autonome nuit 24→25/06/2026

**Mergés sur `feat/ereporting-b2c` (verify-fast complet VERT)** — 6 fixes en agents parallèles + 1 réappliqué :
- ✅ **BUG-1** heartbeat câblé (vrai bug : `HeartbeatReporter` jamais relié à l'hôte)
- ✅ **BUG-2** purge de la file locale à la désinstallation
- ✅ **BUG-3** UI composante (Frais reconnue consultée par B4 ; message « Adjudication » vrai)
- ✅ **BUG-4a** échec de save rendu visible (socle `DeclaredFormPage`, 9 formulaires)
- ✅ **BUG-6 / BUG-7 / BUG-10-message** messages opérateur (sans expert-comptable, diagnostiquables, sans « évolution ultérieure »)
- ✅ **BUG-9** pays non-ISO ENG/SCO/WAL/NIR → GB (réappliqué à la main : branche agent basée sur un commit périmé)

**Décisions Karl actées** :
- **SIREN → facture B2B, JAMAIS e-reporting.** L'e-reporting B2C = **particuliers uniquement** (évidence — ne plus poser la question).
- **BUG-4b** = « société porteuse » + l'opérateur global doit pouvoir paramétrer **FACILEMENT** des jobs sur **tous** les tenants (maintenance qui ne doit pas exploser avec le nb de clients).
  **✅ LIVRÉ (25/06, commit `f4835c9b`)** : abstraction `ISystemScheduleHost` (société porteuse système = sentinel
  plateforme ; `schedule.CompanyId` n'a aucun effet de portée au fan-out, juste une clé d'unicité/dé-dup). Un job
  système est planifiable ET consultable par un opérateur plateforme (sans société courante) — UNE planification
  couvre tous les tenants. Seeder dev unifié sur la même porteuse (anti double-exécution). Reste hors lot
  (pré-existant) : filtrer les types de fan-out système hors du `<select>` pour un éventuel opérateur tenant
  (aujourd'hui la page est super-admin only via `job.view`).

**Ouverts / découverts cette nuit** :
- **BUG-10-EXTRACTION (MAJEUR, NOUVEAU)** — voir en bas. Remplace l'ancien « BUG-10b ». Karl avait RAISON : le régime existe.
- **BUG-11 (100152)** — voir en bas. Faux blocage marge ; code correct → Host déployé périmé (à confirmer par rebuild).
- **BUG-5** (lignes avant transmission) — lot persistance, options A/B documentées — DÉCISION.
- **BUG-8** (e-reporting B2C taxable) — sourçage F03 + design — DÉCISION.

---

## BUG-1 — Heartbeat agent figé

- **Symptôme** : sur la console (Flotte / agents), « Dernier contact » ne change **jamais** après le tout premier
  contact émis lors de l'installation. Capture : « Dernier contact 24/06/2026 16:36 » figé.
- **Repro** : installer l'agent EncheresV6, le laisser tourner ; le heartbeat ne se rafraîchit plus.
- **Diagnostic (partiel)** : le service est `Running` mais le cycle semble mort. On a d'abord soupçonné
  `adapterConfig.EncheresV6.dossier` absent (corrigé via le wizard) — **à re-confirmer** : si « Dernier contact »
  reste figé APRÈS une réinstallation avec dossier/schéma corrects, c'est un **vrai bug heartbeat distinct** (la
  boucle de heartbeat ne re-poste pas, ou l'écriture côté plateforme ne met pas à jour `LastContactUtc`).
- **Fichiers (pistes)** : agent — boucle heartbeat (`heartbeatMinutes`) ; plateforme — endpoint de heartbeat agent
  + persistance `LastContactUtc` ; page Flotte.
- **Critère d'acceptation** : après install, « Dernier contact » se met à jour à chaque cycle (`heartbeatMinutes`),
  vérifié bout-en-bout (agent → plateforme → console).

## BUG-2 — Watermark survit à la réinstallation

- **Symptôme** : après désinstallation + réinstallation de l'agent, l'extraction reprend l'ancien watermark
  (les « nouveaux » documents ne sont pas ceux attendus). Contournement manuel utilisé : stop service →
  supprimer `agent-queue.db*` → start.
- **Diagnostic** : `agent-queue.db` (sqlite, table `agent_state`, clé `extraction.watermark.utc`) **n'est pas purgé**
  à la désinstallation. La désinstall doit nettoyer l'état local.
- **Fichiers (pistes)** : désinstalleur de l'agent / script de désinstallation du service ; chemin de la queue locale.
- **Critère d'acceptation** : une désinstallation purge la queue locale (`agent-queue.db*`) ; une réinstallation
  repart d'un état vierge (watermark vide → fenêtre `extractFromUtc` ou « nouveaux uniquement »).
  ⚠️ Lecture seule base SOURCE inchangée (la purge ne concerne QUE l'état local de l'agent).

## BUG-3 — Mapping TVA « composante » incohérent (table TVA)

- **Symptôme** : dans `/parametrage/table-tva`, le contrôle de cohérence affiche des règles « mortes » à tort.
  Deux captures « composante non consultée par le pipeline ».
- **Diagnostic** :
  - (a) **Adjudication** apparaît « morte » au CHECK : le pipeline générique mappe **toujours** via la part
    `Autre` (la dérivation adjudication/frais n'est pas livrée — ADR-0004 / PIP03b), donc une règle saisie sur
    la part « Adjudication » ne sera jamais consultée par le CHECK.
  - (b) **Frais** est affichée « morte » au contrôle de cohérence alors que **B4 la consulte** explicitement
    (`ResolveMarginAsync` lit la part `Frais`). La cause : `ConsultedMappingParts.PipelineConsulted()` est codé
    en dur sur `{ Autre }` → le contrôle de cohérence ignore les consommateurs hors-CHECK (B4).
    **⚠️ NE PAS supprimer les règles Frais** (faux « morte »).
- **Fichiers (pistes)** : `ConsultedMappingParts` (la liste `PipelineConsulted()`), le contrôle de cohérence de
  la page table TVA, `CheckTvaMapping` (part `Autre`), `B2cMarginResolver` (part `Frais`).
- **Critère d'acceptation** : le contrôle de cohérence reflète **tous** les consommateurs réels (CHECK = `Autre`,
  B4 = `Frais`) ; une règle réellement consultée n'est jamais marquée « morte » ; le message reste vrai et
  n'incite pas à supprimer une règle utile.

## BUG-4 — Bouton « Enregistrer » d'une planification : silencieux, rien n'est créé

- **Symptôme** : `/admin/jobs/new`, formulaire **valide** (nom, type, cron `*/1 * * * *` avec aperçu des 3
  prochaines exécutions affiché), clic « Enregistrer » → **rien** : aucune planification créée, aucun message,
  aucun log serveur. Capture fournie.
- **Diagnostic** : connecté en **sysadmin** (compte supervision/plateforme, **sans société courante**).
  `AdminJobScheduleForm.CreateAsync` fait `ActorContext.Current.CompanyId ?? throw "Aucune société sélectionnée."`
  → l'exception est levée **avant** l'appel serveur (d'où 0 log), et l'erreur **n'apparaît pas** à l'écran.
  Deux défauts :
  - (a) **UX** : un échec de création ne doit jamais être silencieux (l'exception devrait remonter en alerte).
  - (b) **Fonctionnel** : un **job SYSTÈME** (fan-out tous tenants, ex. « E-reporting B2C de la marge ») doit être
    planifiable sans dépendre d'une société courante de l'opérateur (le scheduler scope la planif à une company
    « porteuse », cf. `DevJobScheduleSeeder`). Sinon le sysadmin ne peut RIEN planifier.
- **Fichiers (pistes)** : `src/Modules/Job/Web/Pages/AdminJobScheduleForm.razor` (`CreateAsync` l.425-438),
  `src/Common/UI/Components/DeclaredFormPage.razor.cs` (`PerformSaveAsync` catch → MapDomainError/GlobalError),
  résolution de la company porteuse des jobs système.
- **Critère d'acceptation** : (a) tout échec de save affiche une erreur claire ; (b) un job système est
  planifiable depuis un compte plateforme (sélection/affectation d'une société porteuse, ou planif au niveau
  système). Test bUnit sur l'échec visible + sur la planif d'un fan-out.

## BUG-5 — Détail des lignes masqué tant que le document n'est pas transmis

- **Symptôme** : onglet **Contenu** d'un document, carte « Détail des lignes » → texte placeholder
  « Le détail ligne à ligne s'affichera ici une fois le document transmis à la Plateforme Agréée. […] le contenu
  complet est inclus dans le dossier d'export (onglet Archive). » Or **on veut voir les lignes AVANT** la
  transmission — c'est exactement là qu'on en a besoin pour **comprendre un blocage**.
- **Diagnostic** : `DocumentDetailView.razor` (l.78-177) ne rend le tableau des lignes que si `Content.HasLines`,
  sinon le placeholder. Le commentaire l.169-171 indique que la projection des lignes (`Content`) **n'est
  alimentée qu'une fois le document transmis** (« aucun pivot mappé n'est exposé » avant). Le pivot lu/mappé existe
  pourtant dès l'ingestion/CHECK (c'est lui qui est bloqué) → il faut **exposer les lignes lues + le résultat du
  mapping (catégorie/VATEX/taux) dès le CHECK**, pas seulement après transmission. (Contrainte F10 §1 « zéro JSON » :
  restitution LISIBLE en tableau, jamais un dump du pivot — déjà respectée par le tableau existant.)
- **Fichiers (pistes)** : `src/Host/Liakont.Host/Components/DocumentDetailView.razor` (bloc « Détail des lignes »),
  la projection qui alimente `Content`/`Content.Lines` (query console document), source des lignes = pivot persisté
  au CHECK.
- **Critère d'acceptation** : les lignes lues (+ mapping résultant catégorie/VATEX/taux, + contrôle de cohérence
  totaux↔lignes) s'affichent **dès que le document est lu/contrôlé** (états Bloqué / Prêt-à-envoyer inclus), pas
  uniquement après transmission. Le placeholder ne subsiste qu'en l'absence réelle de lignes. Test bUnit sur un
  document Bloqué qui montre ses lignes.

## BUG-6 — Les messages opérateur invoquent « l'expert-comptable » (à bannir)

- **Symptôme** : message de blocage du doc n°100152 : « Action opérateur : **faites valider par l'expert-comptable**
  le mapping du régime de cette adjudication… ». Règle produit non négociable (Karl, récurrent) : le mapping TVA est
  **INTÉGRÉ au produit** (figé, F03), **jamais** « à faire valider par l'expert-comptable » dans l'UI. L'expert-
  comptable est réservé aux questions ouvertes client-spécifiques (docs internes), pas aux messages opérateur.
- **Diagnostic** : ~40 fichiers mentionnent « expert-comptable », mais la cible est **uniquement les chaînes de
  MESSAGE OPÉRATEUR** (blocages, actions correctives affichées) — PAS les commentaires de code ni les docs internes.
  L'action corrective doit être **auto-suffisante** : « complétez/corrigez la table de mapping TVA (régime X →
  catégorie E + VATEX-EU-F/I/J) » ou « vérifiez la donnée source », jamais « faites valider par l'expert-comptable ».
- **Fichiers (pistes)** : règles `src/Modules/Validation/Domain/Rules/*` (MappingCoverageRule, VatexRequiredRule,
  CategoryRateConsistencyRule…), `B2cMarginMarking.cs`, `DocumentCheckEvaluator.cs`, `TvaMapper.cs` — filtrer les
  chaînes affichées (verbatim message), laisser les commentaires.
- **Critère d'acceptation** : aucun **message opérateur** ne mentionne l'expert-comptable ; chaque blocage propose
  une action concrète sur le paramétrage tenant (table TVA) ou la donnée source. Test : grep des messages affichés.

## BUG-7 — Message de blocage non diagnostiquable (ne dit pas le code régime source réel)

- **Symptôme** : doc n°100152 « normalement sur du **code 6** » mais bloqué « non classé au régime de la marge ».
  Impossible de comprendre **pourquoi** : le message donne des **hypothèses génériques** (« adjudication exonérée
  sans VATEX, OU acheteur professionnel… ») mais **jamais le FAIT** : quel code régime source a réellement été lu,
  et vers quoi la table l'a mappé.
- **Diagnostic** : le message de blocage doit citer la **donnée réelle** du document — code régime source lu sur la
  ligne d'adjudication + résultat du mapping appliqué (catégorie/VATEX/taux obtenus) — pour que l'opérateur voie
  l'écart (ex. « régime source `6` → mappé catégorie S au lieu de E+VATEX » ou « ligne sans code régime »). Couplé à
  BUG-5 (voir les lignes lues + mapping résultant dès le CHECK). ⚠️ Ne pas inventer : afficher ce qui a été lu/mappé.
- **Fichiers (pistes)** : génération du message de blocage marge (`B2cMarginMarking` / `DocumentCheckEvaluator` /
  règle de couverture), projection console des lignes (BUG-5).
- **Critère d'acceptation** : un document bloqué affiche le code régime source réel de chaque ligne + le mapping
  obtenu, suffisant pour comprendre le blocage **sans** ouvrir le code ni la base. Repro à utiliser : doc n°100152.

## BUG-8 (GRAVE) — E-reporting B2C « classique » (hors marge) absent + menu à réorganiser

- **Symptôme** : la console expose un menu **« Émissions marge B2C »** mais **rien** pour le **B2C taxable hors
  régime de la marge**. Asymétrie absurde : on expose le **cas particulier** (marge) sans le **cas général** (toute
  vente B2C taxable à un particulier — ex. inventaire/estimation, ou une adjudication taxable standard). Le
  e-reporting B2C de transactions doit couvrir **tout** le B2C, pas seulement la marge.
- **Diagnostic** : le job B4 (`AggregateB2cMarginAllTrigger`) ne capte que la marge (flag `IsB2cReportingDeclaration`
  dérivé sur `E + VATEX marge`). Un doc B2C **taxable standard** (catégorie `S`, taux positif, acheteur sans SIREN)
  n'a aujourd'hui **ni dérivation/marquage, ni job d'agrégation, ni transmission, ni page, ni item de menu** → il
  n'est tout simplement **pas e-reporté**. (C'est aussi le doc débloqué par le fix E ci-dessus, qui n'a ensuite
  aucune destination.)
- **Travail (complet)** :
  1. **Sourcer F03** d'abord (flux de transaction B2C taxable standard : assiette = base HT, TVA par taux) — ne rien
     inventer (CLAUDE.md n°2). Bloquer si la spec ne tranche pas.
  2. **Dériver/marquer** le B2C taxable (S + taux positif + acheteur B2C) comme déclaration de transaction B2C
     taxable (distincte de la marge).
  3. **Agréger + transmettre** : pendant du B4 pour le taxable (agrégat jour×devise×taux, base HT + TVA par taux,
     transmission PA), avec le même journal d'émission append-only / anti-doublon au grain document.
  4. **Console + menu** : réorganiser pour exposer l'**e-reporting B2C dans son ensemble** (marge ET taxable) de
     façon cohérente — pas un item « marge » isolé. Gabarit `DeclaredListPage` (jamais de grille maison),
     **aucune surface démo/placeholder**.
- **Critère d'acceptation** : toute vente B2C (marge **ou** taxable standard) est e-reportée bout-en-bout ; le menu
  présente l'e-reporting B2C de manière complète et cohérente ; aucun doc B2C ne reste « sans destination » ;
  parité design-system, zéro grille maison, zéro item démo.

## BUG-9 — Code pays non-ISO (« ENG ») bloque au lieu d'être normalisé

- **Symptôme** : doc n°100264 bloqué « Le code pays de l'acheteur (« ENG ») … n'est pas un code pays ISO 3166-1
  alpha-2 valide ». `ENG` (Angleterre) est de la **donnée source réelle** EncheresV6 (`entete_ba.code_pays`).
- **Diagnostic** : même nature que `EURO`→`EUR` (déjà corrigé) — donnée source non-ISO systémique. L'Angleterre
  n'a pas de code ISO alpha-2 propre ; elle relève du Royaume-Uni → **`GB`** (ISO 3166-1). À **normaliser côté
  agent** (mapping de codes pays non-ISO connus : `ENG`/`SCO`/`WAL`/`NIR` → `GB`), pas bloquer. Sourcer ISO 3166-1 ;
  pour tout code non mappable de façon sûre, laisser passer brut et laisser la plateforme trancher (ne pas deviner).
- **Fichiers (pistes)** : `EncheresV6RowMapper` (à côté de `NormalizeCurrency`), ou un normaliseur pays partagé.
- **Critère d'acceptation** : un acheteur `code_pays = ENG` produit `GB` ; le doc n'est plus bloqué pour ce motif ;
  test unitaire RowMapper sur ENG/SCO/WAL/NIR→GB + un code inconnu laissé brut.

## BUG-10 — Message « 0 code régime » faux : attribue à une « scission BG-30 / évolution ultérieure » au lieu de la vraie cause

- **Symptôme** : doc n°100264, lignes « Adjudication lot 29/30 » bloquées « 0 code(s) régime et 1 ventilation(s)
  TVA … scission EN 16931 BG-30 / ADR-0004 D3-1 non couverte … **ce cas sera couvert par une évolution ultérieure
  du pipeline** ».
- **Fait (base source)** : `ligne_pv.code_regime_tva` est **NULL** pour les lots 29 et 30 → le **régime TVA est
  absent en source**. Ce n'est **PAS** une scission multi-régime (BG-30) : c'est une **absence pure de code régime**.
- **Diagnostic** :
  - (a) Le **message est faux** : il invoque une scission BG-30 / une « évolution ultérieure » alors que la cause
    réelle est « le lot n'a pas de code régime TVA en source ». Et « sera couvert par une évolution ultérieure »
    est un **aveu de non-couverture** affiché à l'opérateur (même registre que « démo » / « expert-comptable » :
    à bannir). ⚠️ Ce cas n'est **pas** ce que prépare le lot B2C-marge — c'est une **donnée source incomplète**.
  - (b) Le **blocage en soi est fondé** (on ne devine jamais un régime — CLAUDE.md n°2). Décider s'il existe un
    **régime par défaut SOURCÉ** (table `Regime_tva`, défaut dossier/vente) ; sinon le blocage reste correct, mais
    avec un **message vrai** : « lot N : aucun code régime TVA en source ; corrigez la donnée source ».
- **Fichiers (pistes)** : `CheckTvaMapping.BuildPlan` (ShapeBlockReason « 0 code régime »), génération du message,
  extraction `EncheresV6` (jointure `ligne_pv` → vérifier que NULL = vraiment absent, pas une jointure ratée).
- **Critère d'acceptation** : le message nomme la vraie cause (régime source absent), ne promet aucune « évolution
  ultérieure », et propose une action concrète. Repro : doc n°100264 (lots 29/30).

---

## Diagnostic confirmé sur la donnée réelle (base `EncheresV6_Demo`)

- **100152 = FAUX BLOCAGE** (renforce BUG-7 ; règle de mapping CORRECTE) : adjudication **code régime 6** (marge),
  acheteur **particulier** (société vide, SIREN NULL), frais présents → **vraie marge B2C**, qui **devrait passer**.
  Le message accuse pourtant « ou acheteur **professionnel** » (FAUX) et ne montre pas le code 6.
  **Réglage vérifié sur capture** : la règle `6 → E + VATEX-EU-F` est posée sur la composante **« Hors Enchères »**
  qui EST la part **`Autre`** (`TvaComposanteVocabulary.ValueLabel(Autre) = "Hors Enchères"`) — donc **la bonne
  part** (celle que le CHECK consulte). Le « Adjudication » de la ligne est seulement le **libellé** de la règle.
  **Cause restante la plus probable : blocage PÉRIMÉ** — le message « exonérée **sans** VATEX » trahit que le doc a
  été contrôlé AVANT que le VATEX soit en place. **Action** : re-vérifier le 100152 (re-déclencher le CHECK) ; il
  doit passer en marge. **Si toujours bloqué après re-vérification avec table validée → vrai bug de propagation du
  VATEX du mapping vers la ligne au CHECK** (à creuser : `TvaMapper` renvoie-t-il `Vatex` ? `EnrichLine` le pose).
  ⚠️ **Piège UX (BUG-3 confirmé)** : pour mapper l'**adjudication**, il faut la composante **« Hors Enchères »**
  (= `Autre`), PAS la composante « Adjudication » (qui existe mais n'est JAMAIS consultée) — contre-intuitif.
- **100264** : pays ENG (BUG-9 ✅) + **régime PRÉSENT mais raté à l'extraction** (BUG-10-EXTRACTION ci-dessous —
  PAS « régime absent » comme écrit initialement : Karl avait raison).

---

## BUG-10-EXTRACTION (MAJEUR) — l'agent rate le code régime quand il vit sur le PV, pas sur `ligne_pv.no_ba`

- **Symptôme** : doc n°100264 bloqué « 0 code régime » alors qu'il **est** en régime 6 (marge). Karl : « quand on
  regarde en bdd, on a des codes régime ». **Vérifié en base, il a raison.**
- **Fait (base `EncheresV6_Demo`)** :
  - `entete_ba[100264].no_pv = 77` ; `entete_pv[77].code_regime_tva = 6` ; toutes les `ligne_pv WHERE no_pv=77`
    sont **régime 6**. Le régime EXISTE, au niveau du **PV de vente**.
  - `ligne_pv WHERE no_ba=100264` = **VIDE** → l'extracteur (jointure du régime via `ligne_pv.no_ba` + `no_ligne_pv`,
    cf. `EncheresV6Schema` Q1) ne ramène **rien** → l'agent émet `sourceRegimeCodes = []` → CHECK bloque « 0 régime ».
  - **`170` bordereaux** (`entete_ba`) sont SANS `ligne_pv` par `no_ba` → tous bloqués à tort par ce chemin.
  - Le `no_ligne_pv` côté `lignes_ba` (29/30) ≠ ceux de `ligne_pv` du PV 77 (1..13) ⇒ le modèle de liaison est
    plus riche que `no_ba`+`no_ligne_pv` : le régime se rattache via le **PV** (`entete_ba.no_pv` → `entete_pv` /
    `ligne_pv.no_pv`).
- **Diagnostic** : la résolution du régime de l'adjudication doit passer par le **PV** (`no_pv`), pas par
  `ligne_pv.no_ba`. À cadrer précisément sur le modèle réel (régime au grain PV `entete_pv.code_regime_tva`, ou au
  grain ligne `ligne_pv` via `no_pv` + correspondance lot) — **avec accès à la base pour valider la jointure, ne
  rien deviner** (CLAUDE.md n°2 ; lecture seule stricte). ⚠️ NE PAS retomber sur « régime absent » : c'est un
  rattachement à corriger, pas une donnée manquante.
- **Fichiers (pistes)** : `agent/src/Liakont.Agent.Adapters.EncheresV6/Source/EncheresV6Schema.cs` (Q1 + jointure
  `ligne_pv`), l'extracteur EncheresV6, modèles source (`entete_pv` à exposer ?).
- **Critère d'acceptation** : le doc 100264/2000026 (et les ~170) sort avec son **régime** ; tests sur un BA dont
  `ligne_pv.no_ba = 0`. Lecture seule stricte préservée.
- **✅ RÉSOLU (diagnostic, 25/06, validé PO)** : la vraie clé de liaison ligne-bordereau ↔ ligne-PV est
  **`no_ligne_tout_pv`** (identifiant GLOBAL de ligne de PV) — `lignes_ba.no_ligne_tout_pv ↔ ligne_pv.no_ligne_tout_pv`.
  Cause exacte : `ligne_pv.no_ba` vaut souvent **0** → l'ancienne jointure `ON lp.no_ba = e.no_ba AND lp.no_ligne_pv =
  l.no_ligne_pv` rate. Preuve : BA 2000026 lot 22 → `no_ligne_tout_pv = 287` → `ligne_pv[287].code_regime_tva = 6`
  (avec `no_ba = 0`). **Grain LOT confirmé** : 43 PV de la base ont des régimes MIXTES par lot (interdit toute
  simplification au grain vente/`entete_pv`). Fix EN COURS — agent, branche `fix/bug-10-extraction-regime-pv`
  (Q1 du `EncheresV6Schema` : jointure `ligne_pv` via `no_ligne_tout_pv` ; BV/Q2 inchangé, anti double-comptage).

## BUG-11 — 100152 : message diagnostiquable OK (BUG-7 ✅) ; mon diag base était FAUX (autre base)

- **Résolu indirectement** : après rebuild du Host (BUG-7 déployé), le message de blocage du 100152 est désormais
  **diagnostiquable** — il cite le fait : « régime source **5** → catégorie S, taux 20 % ; acheteur particulier ».
- **⚠️ Mon diagnostic de la nuit était FAUX** : j'avais requêté `EncheresV6_Demo` (localhost), où le 100152 a **1 lot
  en régime 6**. L'agent de Karl voit **3 lots (8/9/11) en régime 5** → **l'agent lit une AUTRE base** que celle que
  j'interrogeais. Tous mes diagnostics base de la nuit (100152, 100264, PV 77, etc.) sont sur la **mauvaise base** —
  À REFAIRE sur la base réelle de l'agent (chaîne ODBC à demander à Karl).
- **Fond (à trancher par Karl, expert)** : le 100152 (base réelle) est en **régime 5 → S (taxable)**, pas marge.
  Soit ces lots sont **vraiment taxables** (mais « aucune TVA distincte » sur du S 20 % est incohérent → sent la
  marge), soit le **régime 5 de ces lots est de la marge** mal classée → mapper `5 → E + VATEX`. NE PAS deviner :
  c'est une question fiscale/métier (CLAUDE.md n°2).
- **Action** : (1) obtenir la base/chaîne ODBC réelle de l'agent ; (2) Karl tranche régime 5 = marge ou taxable ;
  (3) si effet de bord de la garde `LooksLikeUnclassifiedMargin` sur un doc réellement taxable → affiner la garde.

## BUG-12 — UX « Composante » de la table de mapping TVA : confuse (relevé Karl 26/06, PAS pour maintenant)

- **Symptôme** : l'éditeur de règle (vertical enchères activé) propose 3 composantes — **Adjudication**, **Frais**,
  **Autre** (« Hors Enchères ») — mais c'est **perturbant pour un utilisateur** : on ne comprend pas laquelle
  mapper ni pourquoi.
- **Faits (sourcés code)** : `TvaMappingPart` { Adjudication=0, Frais=1, Autre=2 }. Consommateurs RÉELS
  (`TableTvaView.razor` l.607-608 + jobs) : **Autre/« Hors Enchères »** = lue par `CheckTvaMapping` (lignes :
  adjudication, factures/notes) ; **Frais** = lue par B4 marge+taxable (`Part = TvaMappingPart.Frais`, taux de la
  commission) ; **Adjudication = MORTE** (jamais consultée).
- **À corriger (UX)** :
  1. **Retirer/masquer « Adjudication »** de l'éditeur (ou la marquer non éditable « non consultée ») — offrir une
     composante morte induit en erreur (l'utilisateur crée une règle qui ne sert à rien).
  2. **Renommer « Autre »** (« Hors Enchères » est cryptique) en un libellé parlant (à trancher avec Karl) ; idem
     clarifier **« Frais »**.
  3. **Texte d'aide explicite** : dire À QUOI sert chaque composante (Frais = taux de la marge/commission ; l'autre
     = les lignes) pour qu'un opérateur sache quoi saisir sans connaître le code.
- **Fichiers (pistes)** : `src/Host/Liakont.Host/Components/TvaRuleEditor.razor`, `TableTvaView.razor`
  (libellés composante l.526, `PartNotConsulted` l.607). UI Liakont — règle review 19 (test bUnit/Playwright).
- **NB** : le fond fonctionne (marge/taxable exigent une règle Frais — cf. seed `82e6361b`) ; c'est purement la
  LISIBILITÉ de l'éditeur qui est en cause.

## BUG-13 — Publication SIREN : champs « Type d'opération » / « Taille d'entreprise » requis MÊME quand la PA les ignore (relevé Karl 26/06, PAS pour maintenant)

- **Symptôme** : l'écran « Publication du SIREN / transmission » rend **obligatoires** (`*`) « Type d'opération » et
  « Taille d'entreprise », mais pour **SuperPDP ces champs ne servent à rien** : `SuperPdpClient.EnsureTaxReportSettingAsync`
  ne transmet AUCUN champ (SuperPDP n'expose pas d'écriture de `tax_report_setting` ; la KYC du SIREN se fait dans
  l'espace SuperPDP). L'opérateur doit donc saisir des valeurs bidon obligatoires qui n'ont aucun effet.
- **Faits** : la contrainte « requis » est codée en dur dans le formulaire (F05), indépendante de la PA active. Or
  CLAUDE.md n°8 : aucun comportement produit ne doit dépendre de ce qu'UN PA sait faire — symétriquement, un champ
  ne devrait pas être imposé si la PA active n'en a pas l'usage.
- **À corriger** : piloter le « requis » de ces champs par la **capacité / les besoins de la PA active**
  (`PaCapabilities`), pas en dur ; ou au minimum ne pas bloquer la publication d'une PA qui les ignore (SuperPDP).
  Idéalement, proposer/valider les valeurs attendues par la PA quand elle en a (au lieu de « jamais devinée »).
- **Fichiers (pistes)** : l'écran de publication SIREN (Host Components, `PaPublicationConsoleService`),
  `PaTaxReportSettingRequest` (Transmission.Contracts). UI Liakont — règle review 19.
