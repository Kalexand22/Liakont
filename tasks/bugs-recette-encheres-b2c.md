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

## ÉTAT — lot recette « 9 bugs » (run 2026-06-27 → 28/06)

Lot scopé par Karl : **Observabilité** (22, 27, 28) + **Robustesse** (18, 23, 24) + **UX recette** (19, 20, 25).
**Les 9 ✅ RÉSOLUS** et poussés sur `feat/ereporting-b2c` (verify-fast **Release** vert + run-tests Testcontainers
vert + revue Claude **clean** au round 3). Détail + commit sous chaque bug.

| Bug | Commit | Bug | Commit | Bug | Commit |
|---|---|---|---|---|---|
| BUG-20 / 27 / 28 | `5abbf1aa` | BUG-22 | `8cc08e6a` | BUG-24 | `39174c9e` |
| BUG-18 | `9cb6d3d6` | BUG-23 | `9cf206f5` | BUG-25 | `c3e08589` |
| BUG-19 | `84ed063b` | revue P2 | `517d247c` | | |

**Hors lot** : BUG-21 rétrogradé **P1 → P3** — FAUX PROBLÈME sur le flux recette (B2C agrégé : aucun numéro envoyé à
la PA ; affichage déjà distingué par famille, BUG-20). Résidu théorique full-B2B à confirmer en recette B2B (filet
`external_id`=`SourceReference`). Voir sa section. *(BUG-5 ✅ RÉSOLU — commit `bca433fd`.)*

**⇒ 0 bug réellement ouvert sur le flux recette EncheresV6 B2C.**

---

## ÉTAT — lot UX recette « BUG-12 à 16 » (run 2026-06-28)

Lot UX/seed relevé Karl 26/06 (« pas pour maintenant ») : **éditeur TVA** (12), **publication SIREN par capacité PA**
(13), **identité jamais seedée** (14), **profil légal éditable** (15), **densité Standard par défaut** (16). **Les 5
✅ RÉSOLUS** et poussés sur `feat/ereporting-b2c` (verify-fast **Release** vert + run-tests Testcontainers vert +
revue Claude **clean** au round 3). Détail + commit sous chaque bug.

| Bug | Commit | Bug | Commit |
|---|---|---|---|
| BUG-16 | `8c0aded2` | BUG-13 | `35bad969` |
| BUG-12 | `1d692312` | BUG-15 | `abaebc85` |
| BUG-14 (+ correctifs review 12/13/16) | ce commit | | |

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
- **✅ RÉSOLU (2026-06-28, commit `bca433fd`)** : `DocumentDetailView` rejoue le pivot **au read-time** (rejeu du
  mapping) → lignes + catégorie/VATEX/taux + contrôle de cohérence totaux↔lignes affichés **dès que le document est
  lu/contrôlé** (états Bloqué / Prêt-à-envoyer inclus). Le placeholder ne subsiste qu'en l'absence RÉELLE de lignes.
  Test bUnit sur un document non transmis qui montre ses lignes. (Option (a) du critère retenue ; pas de décision A/B
  en attente — le tracker était périmé.)

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
- **✅ RÉSOLU (2026-06-28, commit `1d692312` + correctif review « ce commit »)** : « Adjudication » (part morte)
  retirée de la création (filtrée par le handler `GetTvaMappingEditOptions`) ; « Autre » renommée
  **« Adjudication et factures (lignes de la pièce) »**, « Frais » → **« Frais (taux des honoraires acheteur/vendeur) »** ;
  texte d'aide explicite par composante. En MODIFICATION, une règle héritée portant `Part="Adjudication"` est rendue
  en **texte lisible** (`TvaComposanteVocabulary.ValueLabel`), jamais un `<select>` désactivé sans option (afficherait
  « — Choisir — »). Relabel purement présentationnel (énum domaine inchangée, parts réellement consultées intactes).
  Tests bUnit (`TvaRuleEditor`/`TableTvaView`) + handler.

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
- **✅ RÉSOLU (2026-06-28, commit `35bad969` + correctif NRE « ce commit »)** : le « requis » des champs
  « Type d'opération » / « Taille d'entreprise » est piloté par la **capacité PA déclarée**
  `SupportsTaxReportSettingWritable` (B2Brouter `true` ; SuperPDP/Générique/Chorus Pro `false`), jamais en dur ni
  `if (pa is …)` (CLAUDE.md n°8). La publication d'une PA qui les ignore (SuperPDP) n'est plus bloquée. Garde anti-NRE
  `(form.TypeOperation ?? string.Empty).Trim()` en aval (champ devenu facultatif). Tests bUnit.

## BUG-14 — Le seed importe l'IDENTITÉ LÉGALE du tenant (ne devrait JAMAIS) + écrase la saisie manuelle (relevé Karl 26/06, PAS pour maintenant)

- **Symptôme** : après création + import de seed, le profil du tenant affiche l'identité **du seed** (SIREN
  `976543215`, raison sociale « SVV INNEXA (démo) », adresse « 1 rue des Enchères », contact
  `alertes.svv@encheres-demo.test`) — PAS ce que l'opérateur a saisi à la création. Karl : « ce genre d'info ne
  devrait jamais être seedée ».
- **Cause (design)** : `tenant-profile.json` du seed porte l'**identité légale** (SIREN/raisonSociale/address/
  contactEmailAlerte) EN PLUS du paramétrage (fiscal/mapping/seuils/PA). Le wizard « Nouveau client » traite le
  seed et la saisie manuelle comme **EXCLUSIFS** (`ClientWizardSeedStep` : « si un seed est choisi, SON profil
  fait foi ») → le seed **ÉCRASE** la saisie légale de l'opérateur par des valeurs FICTIVES de démo.
- **Pourquoi c'est faux** : l'identité légale est une **donnée tenant-spécifique RÉELLE** (la vraie société), pas
  du paramétrage réutilisable. Seul le **paramétrage** (nature fiscale, table de mapping, seuils, comptes PA SANS
  secret) est seedable/réutilisable. Effet de bord concret ici : il a fallu RE-éditer le SIREN à la main pour le
  faire correspondre à l'entreprise sandbox SuperPDP (`000000002`).
- **À corriger** : DÉCOUPLER **profil légal** (saisie manuelle, par tenant, jamais seedé) et **paramétrage**
  (seedable). Soit retirer l'identité légale de `tenant-profile.json` (le seed ne porte que le paramétrage), soit
  lever l'exclusivité du wizard (identité = saisie manuelle, paramétrage = seed, combinés). Fichiers :
  `tenant-profile.json` (structure), `ImportTenantSeedHandler`, `TenantSeedReader`, `ClientNouveau.razor` /
  `ClientWizardSeedStep.razor` (lever le « seed XOR manuel » sur le légal). UI Liakont — règle review 19.
- **✅ RÉSOLU (2026-06-28, ce commit ; décision Karl : option A « identité jamais seedée »)** :
  - **Seed** : `tenant-profile.json` ne porte PLUS QUE le paramétrage (fiscal/planif/seuils) — identité légale retirée
    (3 fichiers : exemple + volontaire + judiciaire) ; `AddressSeed` supprimé, `ImportProfileAsync`/`ProfileImported`
    retirés de `ImportTenantSeedHandler`/`ImportTenantSeedResult`.
  - **Wizard** : `ClientNouveau.razor` n'est plus « seed XOR manuel » — il importe le seed PUIS crée TOUJOURS le profil
    avec l'identité saisie (ordre seed→profil : la garde override n'honore le companyId explicite que tant qu'aucun
    profil n'existe). Sans cet ordre, un client créé avec un seed restait sans profil (pipeline suspendu, CFG02).
  - **Garde create-only** ré-ancrée sur « un composant de paramétrage présent » (`HasAnyConfigurationAsync` : fiscal OU
    planif OU seuils OU compte PA), endpoint OPS03 (409) + DevTenantSeeder ; ancrer sur le seul fiscal aurait laissé un
    seed sans bloc fiscal écraser silencieusement des réglages console.
  - **DevTenantSeeder** : identité de dev posée PROGRAMMATIQUEMENT depuis `appsettings.Development.json` (hors fichier de
    seed, SIREN fictif), auto-réparation d'un split-brain « paramétrage présent / profil absent ».
  - **Tests** : unitaires (`TenantConfigurationProbe` toutes branches, `DevTenantSeeder`, wizard) + intégration
    (cohérence `companyId` profil↔fiscal↔registre, back→forward idempotent). verify-fast Release + run-tests verts,
    revue Claude clean round 3.

## BUG-15 — Profil légal du tenant NON éditable après création (relevé Karl 26/06, PAS pour maintenant)

- **Symptôme** : le profil (SIREN, raison sociale, adresse, contact) est en **LECTURE SEULE** dans
  `ParametrageView` (rendu en `<dl>`, aucun `InputText` ; le seul bouton est « Modifier les paramètres
  **FISCAUX** », pas le profil). Le SIREN n'est saisissable **qu'au wizard de création**. Pas d'action
  « Modifier le profil légal » post-création.
- **Conséquence** : pour corriger une faute de frappe ou changer le SIREN (ex. l'aligner sur l'entreprise
  sandbox SuperPDP `000000002`), il faut **SUPPRIMER + RECRÉER tout le tenant** — UX très lourde, et on perd
  le reste de la config (PA, etc.).
- **À corriger** : exposer une **édition du profil légal post-création** dans `ParametrageView`. Le command
  existe DÉJÀ (`ClientConsoleService.SaveProfileAsync` → `SaveTenantProfileCommand`) — il manque juste le
  point d'entrée UI (bouton + formulaire). ⚠️ changer le SIREN doit **ré-aligner la clé de publication**
  (`cin_scheme « 0002 »`) côté PA → ce n'est pas qu'un champ libre, prévoir la ré-publication.
- **Fichiers** : `ParametrageView.razor` (ajouter l'action/formulaire), `ClientConsoleService.SaveProfileAsync`
  (déjà présent). UI Liakont — règle review 19. Lié à [[BUG-14]] (mieux : traiter les deux ensemble — gestion
  complète du profil légal : saisie à la création, jamais seedé, éditable après).
- **✅ RÉSOLU (2026-06-28, commit `abaebc85` + test page « ce commit »)** : page **« Paramétrage › Profil légal »**
  (`/parametrage/profil`, policy `Settings`) éditant raison sociale / adresse / contact d'alerte ; **SIREN en LECTURE
  SEULE** (clé fonctionnelle immuable, INV-TENANTSETTINGS-001 — repris du profil persisté, ce chemin ne peut pas le
  changer ; un changement lève `ConflictException` côté handler). Lien d'accès depuis `ParametrageView`. Aucune logique
  métier dans la page (déléguée au service MediatR — règle 19). Tests bUnit (`ProfilView` + page `Profil` : permission,
  échec de chargement, profil absent, swallow du reload après save — WEB09) + intégration (`TenantProfileIntegrationTests`).

## BUG-16 — Densité d'interface par défaut = Compact au lieu de Standard pour un nouvel utilisateur (relevé Karl 26/06, PAS pour maintenant)

- **Symptôme** : après création, un utilisateur a la **Densité de l'interface = Compact** (Préférences) — devrait
  être **Standard** par défaut (un nouvel utilisateur ne devrait pas atterrir dans une vue dense).
- **Incohérence** : le **modèle** a pourtant le bon défaut — `UserPreferences.Density { get; init; } =
  DensityStandard` (`src/Modules/Identity/Application/Preferences/UserPreferences.cs:25`). Donc le défaut
  DONNÉES est Standard, mais l'**affichage EFFECTIF** est Compact → soit l'UI (Stratum.Common.UI) applique
  Compact comme défaut de rendu avant/au lieu de lire la préférence, soit la préférence du nouvel utilisateur est
  écrite à « compact » à la création.
- **À corriger** : aligner le défaut EFFECTIF sur `Standard` — vérifier (1) le rendu densité côté
  `Stratum.Common.UI` (classe CSS/état initial avant chargement de la pref), (2) l'init de la préférence à la
  création d'utilisateur (`PostgresUserPreferencesService` / provisioning). Socle vendored modifiable
  (consigner la provenance si on touche `Stratum.*`).
- **Fichiers (pistes)** : `Stratum.Common.UI` (composant densité / layout), `Identity` préférences
  (`UserPreferences`, `PostgresUserPreferencesService`). Règle review 19/20 (socle).
- **✅ RÉSOLU (2026-06-28, commit `8c0aded2`)** : le défaut EFFECTIF est aligné sur **Standard**. Cause = repli
  pré-paint du socle (`stratum-ui.js`, constante de défaut « compact » appliquée avant le circuit, qui primait sur le
  défaut C# `UserPreferences.Density = standard`). Changement VENDORED `compact`→`standard` aux 3 points de défaut JS +
  prélude `App.razor` (Host, non vendored), **consigné en provenance §4.43** (drift socle — règle 20). Vérif :
  `UserPreferencesTests` (défaut modèle) ; la garde de non-régression du repli pré-paint est **manuelle** (recette —
  no silent cap, explicité §4.43, pas de projet Playwright Host en V1).

## BUG-17 (MAJEUR — aiguillage) — La garde « marge non classée » bloque TOUT acheteur identifié au lieu de router vers son canal (relevé Karl 26/06)

- **Symptôme** : doc n°2000026 (acheteur VARRA Rosaria, **SIREN 998877666**, lot 22 **régime 6 = marge**) → **bloqué**
  « marge non classée … acheteur professionnel ». Or le **Livre Blanc SYMEV**
  (`C:\Source\Liakont-GoToMarket\Metiers\Encheres\Livre-Blanc-SYMEV\Livre-Blanc-SYMEV-Facturation-Electronique-Encheres.pdf`,
  **tableau maître p.7**) dit qu'une **marge vendue à un acheteur pro France** part en **e-invoicing** (facture B2B
  portant la mention « Régime particulier » E + VATEX-EU-F/I/J, TVA 0), **jamais bloquée**. Conforme aussi à la
  décision Karl actée plus haut (ligne 19) : **SIREN → facture B2B**. La garde marge ne respecte pas cette décision.
- **L'aiguillage de référence (tableau maître p.7) — 2 axes INDÉPENDANTS** :
  - **Régime du lot** (table TVA, fixé par le vendeur) → CONTENU fiscal : marge = E + VATEX-EU-F/I/J, TVA 0 ;
    prix total = S, 20 % (ou 5,5 % œuvre d'art).
  - **Profil acheteur** (tiers) → CANAL : particulier FR → e-reporting agrégé/jour (B2C) ; pro FR (SIREN) →
    e-invoicing B2B ; assujetti UE (n° TVA) → e-reporting unitaire B2B (+ intracom 262 ter exonéré / VIES / 289 B
    au prix total) ; hors UE → e-reporting unitaire exonéré (262 I).

  | Régime ↓ \ Acheteur → | particulier FR | pro FR (SIREN) | assujetti UE | hors UE |
  |---|---|---|---|---|
  | **Marge** (E+VATEX) | ✅ B4 marge agrégé | ⛔ **bloqué** (l.146) → e-invoicing | ⛔ **bloqué** → unitaire B2B | ⚠️ export gate B2C → acheteur identifié = bloqué |
  | **Prix total 20 %/5,5 %** (S) | ✅ B4 taxable agrégé | ✅ e-invoicing (passe car TVA>0) | ⛔ intracom 262 ter + VIES/289 B → non traité | ⚠️ export gate B2C |

  Seules cellules vraiment vertes : colonne B2C-particulier (tous régimes) + prix-total-pro-FR (qui marche **par
  accident**, n'étant pas happé par la garde marge car TVA>0).
- **Diagnostic (cause UNIQUE)** : toutes les markings (`B2cMarginMarking`, `B2cTaxableMarking`, `B2cExportMarking`,
  `B2cPlainTaxableMarking`) gardent sur le **même** prédicat `B2cBuyerClassification.IsNonProfessional`. Donc tout
  acheteur IDENTIFIÉ (pro FR, assujetti UE, étranger identifié) sur un document à **forme exonérée** (TVA 0 + frais)
  → `IsMarginDeclaration`/`IsExportDeclaration` = false → `LooksLikeUnclassifiedMargin` (`DocumentCheckEvaluator`
  **l.146**) = true → **BLOCK**, **AVANT** la validation (l.152, où `BuyerLooksProfessionalRule` laisserait passer le
  SIREN) et **AVANT** la garde de transmission B2B (l.200, qui route les SIREN vers e-invoicing). Le code a collé
  « canal = B2C ou pas » sur l'axe régime → il bloque tout ce qui n'est pas B2C-particulier.
- **Sous-issue STRUCTURELLE (2ᵉ volet du fix)** : les **frais acheteur** vivent dans `BuyerFees` (HORS lignes,
  ajoutés pour l'agrégat B4). Pour l'e-invoice marge-pro (cas d'usage n°33, p.17), le bordereau doit montrer
  **adjudication + frais acheteur TTC** en catégorie E. **À vérifier** : la voie document / le sérialiseur Factur-X
  lit-il `BuyerFees` ? Sinon l'e-invoice marge-pro serait **incomplète** (frais acheteur perdus) — c'est la peur
  légitime de la garde l.146. Fix = (a) router au lieu de bloquer **ET** (b) porter les frais acheteur en lignes sur
  la voie document.
- **Cellules OUVERTES — NE PAS TRANCHER** (p.7 les renvoie en annexe D, CLAUDE.md n°2) : intracom UE prix total
  (262 ter / VIES / état récap 289 B) ; export justif. de sortie / consignation-restitution. Les signaler, jamais
  inventer.
- **Fichiers (pistes)** : `src/Modules/Pipeline/Domain/B2cReporting/B2cMarginMarking.cs` (`IsMarginDeclaration`
  étape 4 + `LooksLikeUnclassifiedMargin`), `DocumentCheckEvaluator.cs` (garde l.146 + ordre vs l.152/l.200),
  `B2cBuyerClassification.cs` (prédicat partagé), `B2cTaxableMarking`/`B2cExportMarking`/`B2cPlainTaxableMarking`,
  la voie document / sérialiseur Factur-X (frais acheteur en lignes), `docs/conception/F03-Mapping-TVA.md`
  (amendement : matrice d'aiguillage).
- **Critère d'acceptation** : chaque cellule du tableau p.7 est routée vers SON canal (**test matriciel** régime ×
  acheteur) ; aucune cellule légitime n'est bloquée ; marge-pro / marge-UE partent en facture B2B avec mention
  « Régime particulier » + frais acheteur portés ; cellules ouvertes (289 B, consignation) explicitement signalées,
  jamais devinées.
- **Repros (cellules DISTINCTES de la matrice — le code les bloque à l'identique, elles visent des canaux différents)** :
  - **Marge × pro FR → e-invoicing** : doc n°2000026 (volontaire), n°100348 (judiciaire, Type A = **avoir** → le fix
    doit couvrir aussi les avoirs / PIP02). Acheteur SIREN 998877666.
  - **Marge × assujetti UE → e-reporting unitaire international** : doc n°100351 (acheteur n° TVA **BE**123456798,
    Belgique = UE). Marge taxée en France, **pas** d'exonération intra ; **jamais** e-invoicing domestique.
  Ces deux repros prouvent que BUG-17 ne se réduit PAS au cas pro-FR : tout acheteur identifié (FR / UE / hors UE) est
  bloqué à tort par la même garde l.146.
- **Approche recommandée (prêt-à-coder sans risque)** : rédiger d'abord la **matrice d'aiguillage complète en
  amendement F03** (fidèle à p.7, cellules ouvertes marquées), puis dériver **UN lot de code** (séparer
  `contenu = f(table, lot)` de `canal = f(acheteur)`) + test matriciel. **Décision Karl en attente** : spec F03
  d'abord, ou code direct en suivant p.7.
- **✅ RÉSOLU (2026-06-26, lot interactif, validé Karl « go amendement puis code »)** — en DEUX volets :
  - **Volet b (fondation)** — l'honoraire acheteur est porté en **VRAIE LIGNE** (rôle `PivotLineRole.BuyerFee`)
    et non plus dans le side-channel `BuyerFees` : total de pièce réel (adjudication + honoraire), Factur-X la
    porte (le sérialiseur ne lit que les lignes), perte d'honoraire de la voie document **levée**. Contrat
    (`PivotLineDto.Role` + `SourceTaxAmount`, additifs/hash-neutres), agent `EncheresV6RowMapper.MapBaDocument`,
    et **10 sites B2C** re-câblés par rôle via le helper partagé `B2cAuctionFeeLines` (markings marge/taxable/
    export/ordinaire, découverte, 2 prédicats d'aiguillage, base export, jobs B4 marge+taxable) — zéro
    double-comptage, zéro break silencieux. Amendement **F03 §2.3** (réconcilié 297 E) + **§2.10 règle 4**.
  - **Volet a (l'aiguillage)** — `B2cMarginMarking.LooksLikeUnclassifiedMargin` rendu **buyer-indépendant**
    (bloque UNIQUEMENT un régime vraiment non classé : E sans VATEX de marge ou catégories mêlées ; check de
    régime export buyer-indépendant `B2cExportMarking.IsExoneratedInternationalRegime`). Cellules : marge × **pro
    FR (SIREN)** → route **e-invoicing** (garde l.200) ; marge × **assujetti UE** (n° TVA sans SIREN, doc 100351)
    → **fail-close** via `BuyerLooksProfessionalRule` (« facture B2B requise, traitez manuellement ») ; **régime
    non classé** → reste bloqué. Pas de garde nouvelle (les gardes aval aiguillent).
  - **Trou de prod trouvé + corrigé** (garde de complétude par réflexion des tests de contrat) : le binder STJ du
    Host (`AgentApiJson.ConfigureContractBinding`) n'enregistrait pas `PivotLineRole` → un push agent portant une
    ligne BuyerFee serait rejeté en 400. Convertisseur ajouté (binding strict, RDL01).
  - **DÉFÉRÉ (suivi séparé)** : le **dé-pliage HT/TVA** d'un honoraire **taxable** (régime 5) en facture **B2B**
    Factur-X (la ligne reste TTC/taxe 0, correcte pour la marge + l'agrégat B2C, mais une e-invoice B2B taxable
    devrait montrer HT + 20 %). Aujourd'hui gaté par la capacité de transmission de la PA. À border avec la
    cellule « prix total × pro/UE » et BUG-18 (pays fiable).

## BUG-18 (P2 robustesse) — Normalisation des codes pays non-ISO : passer de la liste codée en dur à une table de correspondance (relevé Karl 26/06)

- **Symptôme** : doc n°2000020 bloqué « code pays de l'acheteur (« JAP ») … n'est pas un code pays ISO 3166-1
  alpha-2 valide ». `JAP` = Japon (ISO alpha-2 = `JP`). Donnée source EncheresV6 non-ISO, comme `ENG`/`EURO` avant.
- **Contexte** : **BUG-9** a déjà normalisé `ENG/SCO/WAL/NIR → GB` **en dur dans l'agent**
  (`EncheresV6RowMapper`). `JAP→JP` est le même besoin. Mais une **liste codée en dur qui grossit** à chaque
  nouveau code n'est pas tenable → d'où la proposition Karl d'une **table de correspondance**.
- **À corriger** :
  - **Immédiat (cheap)** : ajouter `JAP→JP` à la liste existante de BUG-9 (débloque 2000020).
  - **Structurel** : remplacer la liste en dur par une **table de correspondance** `code source → ISO alpha-2`.
    **Décision de placement** : (a) côté agent (extension de l'actuel, mais garde du « savoir client » dans l'agent),
    ou (b) **paramétrage plateforme par tenant** appliqué à l'ingestion — cohérent avec la table TVA (mapping
    validé, versionné, auditable ; l'agent transporte le brut, CLAUDE.md n°6). **Recommandation : (b).**
  - **Fail-closed** : un code non-ISO **non mappé** continue de **bloquer** (jamais deviné, CLAUDE.md n°2).
    Important car le pays **pilote l'aiguillage fiscal** (UE vs hors UE, p.7 / chap. 7) : une normalisation fausse
    mis-route fiscalement → elle doit être **déclarée et tracée**, jamais inférée.
- **Fichiers (pistes)** : `agent/src/Liakont.Agent.Adapters.EncheresV6/EncheresV6RowMapper.cs` (liste actuelle
  BUG-9) ; si (b) : normaliseur pays plateforme + table paramétrage tenant + UI (règle review 19) ;
  `src/Modules/Validation/Domain/Identity/CountryCodeValidator.cs` (inchangé — reste la garde finale ISO).
- **Critère d'acceptation** : `JAP→JP` débloque 2000020 ; la normalisation est une table extensible (pas une liste
  en dur) ; un code inconnu reste **bloqué** (fail-closed) ; test sur `JAP→JP` + un code inconnu laissé bloqué.
- **✅ RÉSOLU (2026-06-28, commit `9cb6d3d6`)** — table de correspondance extensible `NonIsoCountryCodeMap`
  (ENG/SCO/WAL/NIR→GB + JAP→JP) côté agent (`EncheresV6RowMapper`), fail-closed sur code non mappé ; tests
  JAP→JP + code inconnu laissé brut. Voie (b) paramétrage plateforme par tenant = suivi (non requis par le critère).

## BUG-19 (P2 UX — transverse) — Pas de navigation document-à-document (préc./suiv.) sur les vues détail (relevé Karl 26/06)

- **Symptôme** : depuis le détail d'un document, pour passer au document **suivant** il faut **revenir à la grille à
  chaque fois**. Aucun préc./suiv. sur la vue détail. Très pénible en recette (parcourir les bloqués un par un).
- **Besoin** : navigation **préc./suiv.** sur les vues DÉTAIL, parcourant la liste **filtrée/triée telle qu'affichée
  dans la grille** (le contexte de la grille — filtres, tri, page — est **conservé**). **TRANSVERSE à toute l'app**
  (documents, agents, émissions…), pas un one-off sur la page documents.
- **Conception** : c'est un comportement du **gabarit Stratum** (`DeclaredListPage` / vue détail), cohérent avec la
  règle « pas de grille maison, le pattern vit dans le socle » ([[console-web-stratum-design-system]]). Probable
  **modif socle vendored** (`Stratum.*`) → **consigner la provenance** ([[socle-stratum-modifiable]]). Conserver le
  **contexte de liste** (curseur + filtres) côté navigation — pas un naïf `id+1` (qui casserait dès qu'un filtre/tri
  est actif). Réutiliser `DocumentsListFilterMemory` (existe déjà) comme source du curseur.
- **Fichiers (pistes)** : socle Stratum (`DeclaredListPage` / routing détail), Host détail document
  (`DocumentDetailView`), `src/Host/Liakont.Host/Documents/DocumentsListFilterMemory.cs` (mémoire de filtre).
- **Critère d'acceptation** : depuis une vue détail, préc./suiv. parcourt la **liste filtrée** sans repasser par la
  grille ; bornes gérées (1er/dernier) ; contexte (filtre/tri) préservé ; **transverse** (≥ documents + une 2ᵉ
  entité) ; test bUnit (règle review 19) ; provenance socle consignée si `Stratum.*` modifié.
- **✅ RÉSOLU (2026-06-28, commit `84ed063b`)** — contexte de navigation de liste scopé (clé = URL de détail →
  transverse) capturé au clic de ligne dans le gabarit `DeclaredListPage` (modif socle consignée provenance §4.42)
  + composant socle `RecordNavigator` posé sur la fiche document ET la fiche émission B2C (≥ 2 entités) ; bornes
  désactivées au 1er/dernier ; rien hors d'une liste parcourue ; 12 tests (7 logique pure + 5 bUnit).

## BUG-20 (P2) — Type de pièce (famille BA/BV/facture client/note hono) absent comme champ affichable (relevé Karl 27/06)

- **Symptôme** : sur le document (console), rien n'indique sa **famille** (Bordereau acheteur / Bordereau vendeur /
  Facture client / Note d'honoraires). Karl : « ça serait bien d'avoir le type de document sur le document ».
- **Faits (sourcés code)** : la famille n'existe **que dans le préfixe du `SourceReference`** (`encheresv6:ba:`,
  `:bv:`, `:fc:`, `:nh:` — `EncheresV6RowMapper`). Le champ `PivotDocumentDto.SourceDocumentKind` porte aujourd'hui
  la **nature B/A** (vente/avoir, depuis `bordereau_ou_avoir` / `facture_ou_avoir`), **PAS** la famille. Donc aucun
  champ de famille n'est exposé.
- **À corriger** : surfacer une **famille de pièce** sur le document (libellé console : Bordereau acheteur/vendeur,
  Facture client, Note d'honoraires). Dérivable du préfixe `SourceReference` au minimum ; mieux = champ propre porté
  par l'agent (sans logique métier — c'est de la structure, pas du fiscal, CLAUDE.md n°6). Lié à BUG-21 (la famille
  est la clé de désambiguïsation du numéro).
- **Fichiers (pistes)** : `PivotDocumentDto` (champ famille ?), `EncheresV6RowMapper` (le poser), `DocumentDetailView`
  / liste documents (l'afficher). UI Liakont — règle review 19.
- **Critère d'acceptation** : chaque document affiche sa famille ; un BA et un BV de même numéro sont distinguables à
  l'œil ; test.
- **✅ RÉSOLU (2026-06-28, commit `5abbf1aa`)** — colonne « Famille de pièce » (Bordereau acheteur/vendeur, Facture
  client, Note d'honoraires) dérivée du préfixe `SourceReference` via `DocumentFamilyDisplay` (fonction TOTALE,
  fail-closed sur préfixe inconnu) ; affichée en liste + en-tête détail ; tests. Compromis tri/recherche (opère sur
  la référence source brute, comme la colonne « État ») documenté (commit `517d247c`).

## BUG-21 (P3 — à confirmer en recette B2B ; FAUX PROBLÈME sur le flux recette) — Numéro de pièce non unique entre familles (relevé Karl 27/06)

- **Symptôme / risque** : un même numéro peut exister sur un **BA** et un **BV** (et potentiellement facture/note).
  Le **`Number`** transmis sert d'identité ET de clé d'anti-doublon côté PA (SuperPDP dédoublonne par numéro). Deux
  pièces de familles différentes au même numéro → la PA peut les confondre / en rejeter une.
- **Faits (sourcés code)** : `SourceReference` est **déjà** préfixé par famille (`encheresv6:ba:`…) → unicité
  **interne** OK. Mais `Number` = numéro **brut** (`no_ba`/`no_bv`/`no_fact`/`no_note_hono`, `EncheresV6RowMapper`)
  → **non unique entre familles**. Touche surtout la **voie document / e-invoicing** (où le numéro part à la PA) ;
  l'e-reporting B2C agrégé est moins exposé (pas de numéro de pièce par opération).
- **Faits VÉRIFIÉS (28/06)** : (1) SuperPDP dédoublonne bien **par numéro** (`SuperPdpClient.cs:152` « facture déjà
  existante (id N) ») ⇒ collision réelle sur la voie document ; (2) on transmet `Number` = **entier source brut**
  (`no_ba`/`no_bv`/`no_fact`/`no_note_hono`, `EncheresV6RowMapper`), aucune souche/série ⇒ BA et BV de même entier
  → même `Number`. `SourceReference` (`encheresv6:ba:…`) est déjà unique mais n'est PAS la clé d'anti-doublon PA.
- **Ce n'est PAS bloqué sur la numérotation (F06/F14)** : F06/F14 traitent de la **génération** d'un numéro pour des
  pièces que *Liakont émet* (numérotation par mandant, autofacturation 389). Ici les numéros **préexistent dans la
  source** — Liakont les transmet, ne les fabrique pas. Le bug est une **unicité d'identifiant transmis**, pas une
  règle fiscale à inventer. On ne colle pas `BA-` devant un numéro légal de facture, mais ça n'est qu'un garde-fou,
  pas un verrou de spec.
- **Fait source confirmé (Karl, 28/06)** : dans Enchères/Zen, BA et BV partagent **le même entier** (séries
  indépendantes qui se recoupent), **aucune souche** distinctive. (NB : l'autofacturation 389 = souche par mandant
  est une feature FUTURE distincte — sans rapport avec ce bug.)
- **✅ VERDICT (28/06, rétrogradé P1→P3 avec Karl) — FAUX PROBLÈME sur le flux recette EncheresV6** :
  - **Transmission** : la voie document **rejette le B2C** (pas de destinataire identifié) → tout le flux marge/B2C
    part en **agrégé** (`b2c_transactions`), **SANS numéro de pièce** (`SendTenantJob.cs:540-556` : « déclaration
    10.3 … JAMAIS par cette voie document … différée vers son job B2C dédié »). On n'envoie donc **pas** le numéro
    de BV à SuperPDP dans ce flux → **aucune collision**.
  - **Affichage** : BA et BV déjà distingués à l'écran par la **« Famille de pièce »** (BUG-20, commit `5abbf1aa`).
  - ⇒ Rien à corriger pour la recette : ni transmission (agrégé), ni affichage (famille).
- **Résidu théorique (P3, à confirmer EN RECETTE B2B, pas dans ce flux)** : full B2B où un BA (acheteur B2B) ET un
  BV-commission (vendeur assujetti B2B) au même entier, sous le **même SIREN office**, partiraient **tous deux**
  comme e-factures numérotées → là seulement SuperPDP pourrait dédoublonner. À constater sur le terrain (pas deviné).
  **Filet trivial si rencontré** : `external_id` = `SourceReference` (déjà unique par famille) au lieu du numéro brut
  (`SuperPdpClient.cs:147`) ; le numéro légal BT-1 reste le numéro source (fidèle, rien d'inventé).
- **Fichiers (pistes, si le résidu B2B se confirme)** : `SuperPdpClient.cs` (clé `external_id`), `EncheresV6RowMapper`.
  Lié à BUG-20.

## BUG-22 (P2 observabilité) — Pas de détail d'une émission e-reporting B2C dans l'UI (motif de rejet PA + pièces composant l'agrégat) (relevé Karl 27/06)

- **Symptôme** : sur `/emissions-marge-b2c`, impossible d'ouvrir le **détail** d'une ligne d'émission : ni le
  **motif de rejet PA**, ni la **liste des documents** qui composent l'agrégat ne sont consultables. En recette il a
  fallu lire la base (`pipeline.b2c_margin_emissions`, colonnes `pa_response_snapshot` / `detail` / `document_id` /
  `emission_batch_id`) pour comprendre un rejet — l'opérateur ne peut pas diagnostiquer depuis la console.
- **Faits (sourcés)** : la donnée EST stockée — `pa_response_snapshot`
  (`{"http_status_code":400,"message":"cannot add transaction at date 2024-01-03"}`), `detail`
  (`[SPDP_B2C_REJECTED] …`), et chaque ligne porte `document_id` + `source_reference`, groupées par
  `emission_batch_id` (= l'agrégat jour×devise×taux). Il manque juste la **vue**.
- **À corriger** : page/fiche de **détail d'émission** (depuis `/emissions-marge-b2c`) affichant : statut + **motif PA**
  (`pa_response_snapshot`/`detail`, message opérateur en français), période/catégorie/rôle, et la **liste des pièces
  composant l'agrégat** (par `emission_batch_id`) avec lien vers chaque document. CLAUDE.md n°12 (message opérateur
  avec action corrective). Contrainte F10 « zéro JSON brut » → présenter le motif lisible, pas le snapshot brut.
- **Fichiers (pistes)** : `B2cMarginEmissions.razor` (+ détail), `IB2cMarginEmissionQueries` (exposer batch + pièces +
  snapshot), `PostgresB2cMarginEmissionStore`. UI Liakont — règle review 19 (bUnit). Recoupe BUG-5 (exposer le détail
  au read-time) et BUG-19 (navigation préc./suiv.).
- **Critère d'acceptation** : depuis une émission, l'opérateur voit le motif de rejet PA **et** les documents qui la
  composent, sans ouvrir la base ; test bUnit.
- **✅ RÉSOLU (2026-06-28, commit `8cc08e6a`)** — fiche `/emissions-marge-b2c/{id}` : synthèse + motif PA LISIBLE
  (`PaResponseSnapshotFormatter`, jamais de JSON brut — F10 §1) + pièces de l'agrégat (par `emission_batch_id`) avec
  lien par document ; tenant-scopée par la connexion (database-per-tenant) ; tests bUnit + intégration Postgres réelle.

## BUG-23 (P2 — à border avant prod) — Dérogation Luhn des SIREN de test sandbox PA non gâtée par l'environnement (relevé Karl 27/06)

- **Contexte** : pour exercer l'e-invoicing B2B en recette, le destinataire adressable du sandbox SuperPDP
  (« Tricatel » `000000001`) est nécessaire, mais il **ne satisfait pas la clé de Luhn** → bloqué par
  `BuyerIdentityRule` (`SirenValidator.IsValid`, F04 §3.2/§4.1). Décision Karl (27/06) : autoriser les SIREN de test
  sandbox côté acheteur.
- **Fait livré (27/06)** : liste fermée `PaSandboxTestSirens = { 000000001, 000000002 }` ajoutée à
  `SirenValidator.IsValid` (même mécanique que la dérogation La Poste). + test unitaire. Côté émetteur, la dérogation
  no-Luhn existait déjà (`IsWellFormed`, décision 18/06).
- **Risque résiduel (à corriger avant prod)** : la dérogation est **non gâtée** — ces faux SIREN passeraient AUSSI en
  **Production**. C'est un **affaiblissement d'une validation Blocking** (P1 en registre prod, P2 en build jetable).
  La voie propre : **restreindre la dérogation à l'environnement PA Staging/Sandbox** (le compte PA actif porte son
  `Environment`) — la dérogation ne s'applique que si la PA cible est Sandbox, jamais en Production.
- **Fichiers** : `src/Modules/Validation/Domain/Identity/SirenValidator.cs` (dérogation), à coupler à l'`Environment`
  du compte PA (TenantSettings / Transmission) — nécessite de passer le contexte env à la règle (aujourd'hui pure).
- **Critère d'acceptation** : `000000001` accepté quand la PA active est Sandbox ; **refusé** (Luhn) quand elle est
  Production ; tests des deux cas.
- **✅ RÉSOLU (2026-06-28, commits `9cf206f5` + durcissement `517d247c`)** — la dérogation `PaSandboxTestSirens` est
  gâtée par l'environnement du compte PA actif : tolérée seulement sur PREUVE POSITIVE de contexte hors production
  (au moins un compte actif, AUCUN en production), **fail-closed** sinon (`AllowsSandboxTestIdentifiers`). Le flag
  (`DocumentValidationContext.AllowSandboxTestIdentifiers`) est propagé aux deux seuls appelants prod
  (`BuyerIdentityRule`, `PartyRoleConsistencyRule`) ; tests sandbox accepté / production refusé / sans-compte refusé.

## BUG-24 (P2 — cohérence d'état) — Un document e-reporté (B2C) reste « À envoyer » (ReadyToSend) indéfiniment (relevé Karl 27/06)

- **Symptôme** : après un e-reporting B2C réussi (déclaration **Émis** côté SuperPDP), les **documents** qui composaient
  l'agrégat restent affichés **« À envoyer »**. L'opérateur ne distingue pas un doc déjà e-reporté d'un doc réellement
  en attente d'envoi.
- **Faits (sourcés code)** : « À envoyer » = état **`ReadyToSend`**. Les jobs B4 (`B2cMarginAggregatorTenantJob` &
  cie) **lisent** `GetByStateAsync(ReadyToSend)` et émettent l'agrégat, mais **ne transitionnent jamais le document**
  (le « reporté » vit dans `pipeline.b2c_margin_emissions`, pas dans l'état du doc). La garde D1 exclut ces docs de la
  voie document (`SendTenantJob`) → ils ne seront JAMAIS « envoyés » → bloqués visuellement en `ReadyToSend`.
- **Pas un bug de transmission** : pas de double-envoi (D1 + émission idempotente par `content_hash`). Mais l'UI propose
  même « Envoyer » sur ces docs (`DocumentActionBar`, `IsReadyToSend`) — action qui ne fait rien pour un B2C (differé).
- **À corriger (décision de design)** : un doc dont l'e-reporting est **Émis** doit refléter un état terminal
  **« E-reporté »** (et non « À envoyer », et sans action « Envoyer »). Deux voies :
  (a) **transition d'état** `ReadyToSend → Reported` après émission réussie (attention au ré-agrégat jour×taux : l'agrégat
  est recalculé depuis les `ReadyToSend` — sortir un doc du pool change le recalcul ; à concilier avec l'idempotence
  par `content_hash`) ; (b) **refléter** l'état d'émission au read-time (lien doc ↔ `emission_batch_id`, libellé
  « e-reporté » + masquer « Envoyer ») sans toucher la machine à états. Recoupe BUG-22 (lien doc↔émission) et BUG-5.
- **Fichiers (pistes)** : `B2cMarginAggregatorTenantJob` (+ taxable/export/plain — même schéma), machine à états du
  document (Pipeline.Domain), `DocumentActionBar.razor` / `DocumentDetailView.razor` (libellé + action). UI — règle 19.
- **Critère d'acceptation** : un doc e-reporté n'apparaît plus « À envoyer » ni ne propose « Envoyer » ; il montre
  « e-reporté » avec lien vers sa déclaration ; tests (job : pas de double-POST ; UI : libellé/action).
- **✅ RÉSOLU (2026-06-28, commit `39174c9e`)** — voie (b) read-time, AUCUNE modif de la machine à états : lien doc ↔
  lot d'émission **Issued** (`GetIssuedEmissionBatchForDocumentAsync`) → en-tête + onglet Contrôles affichent
  « E-reporté » + lien vers la déclaration, et la barre d'actions masque « Envoyer » (`CanSend = IsReadyToSend &&
  !IsB2cReported`) ; tests bUnit + intégration (lecture du lot Issued).

## BUG-25 (P3 cosmétique — fuseau) — Admin des planifications : « Prochaine exécution » en UTC, « Dernière exécution » en heure locale (relevé Karl 27/06)

- **Symptôme** : dans la liste des planifications (Jobs), la **« Prochaine exécution »** affiche l'heure **UTC**
  (suffixe « U », ex. `27/06/2026 11:21 U`) alors que la **« Dernière exécution »** est en **heure locale**
  (ex. `27/06/2026 13:19`). Résultat : la prochaine *paraît antérieure* à la dernière (alors qu'en UTC 11:21 > 11:19).
- **Pas un bug d'ordonnancement** : le `*/1 * * * *` tourne bien chaque minute ; c'est uniquement un **mélange de
  fuseaux à l'affichage**. Tous les « prochaine » sont en UTC (00:00, 11:01, 11:30…), tous les « dernière » en local.
- **À corriger** : afficher les deux colonnes au **même fuseau** = celui du **navigateur** (convention RB6, comme la
  page Traitements qui utilise `LiakontDate`/`DateTimeOffset` local). Au minimum, marquer le fuseau de façon cohérente.
- **Emplacement** : page **admin des planifications du SOCLE Stratum** (`Stratum.Modules.Job`, vendored) — la colonne
  « prochaine » rend l'heure cron en UTC, « dernière » via un composant local. **Socle modifiable + provenance**
  ([[socle-stratum-modifiable]] ; règle review 19/20 — page socle MODIFIÉE entre dans le périmètre test).
- **Critère d'acceptation** : « prochaine » et « dernière » dans le même fuseau (navigateur) ; la prochaine n'est
  jamais affichée avant la dernière pour un job qui vient de tourner ; test.
- **✅ RÉSOLU (2026-06-28, commit `c3e08589`)** — « Prochaine exécution » rendue via `<LiakontDate>` (fuseau
  navigateur), comme « Dernière » ; page socle `AdminJobSchedules` modifiée (provenance §4.41) ; test du rendu des
  deux colonnes au même fuseau navigateur résolu.

## BUG-26 (P1 — e-invoicing B2B) — Le Factur-X généré échoue la validation EN16931/CTC-FR de la PA (champs obligatoires manquants) (relevé Karl 27/06)

- **Symptôme** : les B2B (9000003 marge×pro-FR SIREN 967890120, 9000004 Tricatel 000000001) routent bien en
  e-invoicing, transmettent, mais sont **RejectedByPa** au `POST /v1.beta/invoices/convert` (HTTP **400**) — notre
  Factur-X/CII échoue la validation EN16931 + PEPPOL + CTC-FR de SuperPDP.
- **Motifs PA (sourcés `document_events.pa_response_snapshot`)** :
  1. **[BR-CO-25]** — montant dû (BT-115) > 0 mais **ni date d'échéance (BT-9) ni conditions de paiement (BT-20)**.
  2. **[BR-FR-05]/BT-22 (×3)** — mentions légales FR obligatoires absentes des notes (BG-1) : **frais de recouvrement
     (code PMT)**, **pénalités de retard (code PMD)**, **escompte ou son absence (code AAB)**.
  3. **[PEPPOL-EN16931-R008]** — élément **vide** interdit (`ApplicableHeaderTradeDelivery`).
- **Diagnostic** : le sérialiseur CII maison (F16, profil COMFORT) n'émet pas les conditions de paiement (BT-9/BT-20)
  ni les 3 mentions légales FR, et laisse un bloc `delivery` vide. C'est un trou de **contenu**, pas de routage/date/SIREN.
- **À corriger** : émettre BT-9 (ou BT-20) quand un montant est dû ; ajouter les 3 mentions FR (notes BT-22 PMT/PMD/AAB) ;
  supprimer/peupler le `delivery` vide. **Sourcer** (F16 + obligations FR de facturation) — les VALEURS (date d'échéance,
  texte des termes/mentions) viennent de la spec/paramétrage, jamais inventées (CLAUDE.md n°2).
- **Fichiers (pistes)** : sérialiseur CII/Factur-X (F16), construction de l'invoice depuis le pivot (voie document SEND).
- **Critère d'acceptation** : un B2B passe le `/invoices/convert` SuperPDP sans erreur bloquante (BR-CO-25 + BR-FR-05 +
  R008 levés) ; test sur le profil COMFORT (goldens Factur-X mis à jour).
- **✅ RÉSOLU (2026-06-27, validé bout-en-bout — 9000004 Émis)** : lot mentions de facturation paramétrables (défaut
  tenant `BillingMentions`, surchargeable doc, **bloquant si absent** sur B2B FR) — commits `0c03148c` (pivot + tenant +
  builder SuperPDP + sérialiseur CII + delivery R008), `2fb62935` (mentions dans la page Paramètres fiscaux),
  `f1c7776a` (relance d'un rejet + visuel des mentions). **Cause racine FINALE du « rien ne change »** : `CheckTvaMapping.Rebuild`
  (rejeu du mapping au SEND, juste APRÈS l'injection des mentions par l'enricher) reconstruisait le pivot en **oubliant
  les champs additifs** (`paymentTerms`/`notes`/`deliveryDate`/`invoicePeriod`, ADR-0007) → mentions amputées avant
  sérialisation → payload sans mentions (rejet) ET snapshot sans mentions (fiche vide). Fix `a16b310e` (Rebuild reporte
  tous les additifs) + test de non-régression `Evaluate_Preserves_Additive_Fields_Through_The_Mapping_Rebuild`.
  **État des repros** : **9000004** (TRICATEL `000000001`) → **Émis** ✅ ; **9000003** (GOURRIER Samuel, SIREN
  `967890120`, **PRO confirmé Karl**) → mentions OK mais rejet **différent** « pre-check: receiver address does not
  exist in peppol directory » = **limite SANDBOX** (SuperPDP ne connaît comme destinataire que `000000001`), non
  reproductible en PPF/PDP réel (un pro est dans l'annuaire) → **pas un bug produit**.

## BUG-27 (P2 observabilité — jumeau document de BUG-22) — Le motif de rejet PA d'un DOCUMENT n'est pas affiché dans l'UI (relevé Karl 27/06)

- **Symptôme** : sur un document **RejectedByPa**, l'onglet **Contrôles** dit « Le motif de rejet figure dans l'onglet
  Historique », mais l'**Historique** n'affiche que « Transition Sending → RejectedByPa » — **sans le motif**.
  L'opérateur ne peut pas savoir POURQUOI sans lire la base.
- **Faits (sourcés)** : la raison COMPLÈTE est stockée — `documents.document_events.pa_response_snapshot` (JSON :
  `errors[].Code/Message` + `rawResponse`, ex. les BR-CO-25 / BR-FR-05 ci-dessus). L'UI Historique ne lit que
  `event_type` + `detail`, pas `pa_response_snapshot`.
- **À corriger** : afficher le motif PA lisible (codes + messages opérateur en français, CLAUDE.md n°12) dans
  l'Historique (et/ou un encart sous « Refusé par la PA » de l'onglet Contrôles). Contrainte F10 « zéro JSON brut » →
  présenter les `errors[]` formatés, pas le snapshot brut. Jumeau de **BUG-22** (côté émission B2C) → traiter ensemble.
- **Fichiers (pistes)** : onglet Historique / Contrôles du document (`DocumentDetailView` / vue piste d'audit),
  `IDocumentEventQueries` (exposer `pa_response_snapshot`). UI Liakont — règle review 19.
- **Critère d'acceptation** : depuis un document rejeté, l'opérateur voit les motifs PA (codes + messages) sans ouvrir
  la base ; test bUnit.
- **✅ RÉSOLU (2026-06-28, commit `5abbf1aa`)** — motif PA LISIBLE (codes + messages FR via
  `PaResponseSnapshotFormatter`, jamais de JSON brut — F10 §1) affiché dans l'Historique/Contrôles du document depuis
  `pa_response_snapshot` ; tests bUnit. Jumeau de [[BUG-22]] (même formateur partagé).

## BUG-28 (P2 affichage) — En-tête du document : « SIREN émetteur : non renseigné » alors que la facture transmise le porte (relevé Karl 27/06)

- **Symptôme** : sur le détail d'un document **B2B Émis et accepté** par la PA (9000004), l'en-tête affiche
  **« SIREN émetteur : non renseigné »**. Trompeur sur un produit de conformité (laisse croire qu'on a transmis une
  facture sans SIREN vendeur).
- **Faits (sourcés base + code)** :
  - Le **snapshot transmis** (ce qui est parti à SuperPDP) porte bien l'émetteur :
    `Supplier.Siren = 000000002`, `Supplier.VatNumber = FR18000000002` (SVV INNEXA). **La facture envoyée est
    correcte** — c'est même ce qui a permis l'acceptation.
  - L'entité persistée `documents.documents.supplier_siren` = **NULL** (vérifié : `[<NULL>]` sur 9000003 ET 9000004).
  - `DocumentDetailView.razor:41` lit `Model.Document.SupplierSiren` (le champ **entité**) → vide → « non renseigné ».
    L'**Acheteur** (TRICATEL), lui, s'affiche car il est porté par l'agent et **persisté** (`customer_name`).
- **Diagnostic** : l'identité émetteur (SIREN/raison sociale/n° TVA vendeur) est injectée **au read-time/SEND depuis le
  profil tenant** (`PivotEmitterEnricher`) et **jamais persistée** sur l'entité Document (ADR-0031 : l'identité émetteur
  appartient à la plateforme, pas à l'agent). L'en-tête lit donc une source structurellement vide. Le **contenu**
  réellement transmis (snapshot) la porte, mais l'en-tête ne le lit pas.
- **À corriger** : faire lire à l'en-tête le **SIREN émetteur EFFECTIF** = celui du contenu transmis (le snapshot le
  porte), repli sur l'entité sinon. Concrètement : exposer `SupplierSiren` (et raison sociale émetteur ?) dans
  `DocumentContentView`, le peupler depuis `pivot.Supplier` dans `DocumentLineProjection.FromPivot`, et binder l'en-tête
  sur `Content.SupplierSiren ?? Model.Document.SupplierSiren`. Pour un doc **non transmis** (Bloqué/Prêt-à-envoyer),
  l'émetteur n'est pas encore résolu (`DocumentContentReplayService` passe `profile: null`) → soit le résoudre au
  read-time via le profil tenant, soit afficher « résolu à l'émission » (jamais une valeur inventée — CLAUDE.md n°2).
- **Fichiers (pistes)** : `src/Host/Liakont.Host/Components/DocumentDetailView.razor` (l.41), `DocumentContentView`
  (champ), `src/Host/Liakont.Host/Documents/DocumentLineProjection.cs` (`FromPivot`), éventuellement
  `DocumentContentReplayService` (résolution émetteur read-time). UI Liakont — règle review 19 (bUnit).
- **Critère d'acceptation** : un document transmis affiche le SIREN émetteur **réellement transmis** ; « non renseigné »
  n'apparaît que si l'émetteur n'est réellement pas résolu ; test bUnit. ⚠️ Affichage uniquement — la facture transmise
  est correcte (aucune correction fiscale/données requise).
- **✅ RÉSOLU (2026-06-28, commit `5abbf1aa`)** — l'en-tête lit le SIREN émetteur EFFECTIF (celui du contenu transmis
  via la projection, repli sur l'entité) ; « non renseigné » seulement si réellement non résolu ; affichage uniquement
  (snapshot transmis inchangé) ; tests bUnit.

---

# Session recette 2026-07-01 (env Isatech, SuperPDP sandbox) — nouveaux relevés Karl

> Recette « tous les derniers correctifs » sur Isatech (cf. `tasks/recette-isatech-correctifs.md` + `tasks/matrice-cas-encheres-b2c.md`).
> Relevés en session directe. Types : 🐞 bug · 🧩 feature/design · 🎨 affichage.

## BUG-24 (RÉOUVERT — P1 correctness d'état) 🐞 — le doc e-reporté reste « À envoyer » dans la LISTE
- **Relevé Karl** : sur `/documents` (LISTE), un document composant une émission e-reporting B2C **Issued** reste affiché
  « À envoyer » ; sur la **fiche** il est bien « E-reporté ». Repère : doc `9000001`, émission Fake `Issued`.
- **Cause RÉELLE (au-delà du symptôme liste/fiche)** : l'**état persisté est faux**. `DocumentState` modélise le canal
  « voie document » (`…→ReadyToSend→Sending→Issued→RejectedByPa`). Un doc B2C-reporting est déclaré via l'**agrégation**
  (`b2c_margin_emissions`), un **autre canal** → il n'atteint jamais `Issued` et **reste coincé en `ReadyToSend`**.
  L'overlay read-time de BUG-24 (`GetIssuedEmissionBatchForDocumentAsync` / `B2cReportedBatchId`) n'a **masqué** cet état
  faux **que sur la fiche**, pas dans la requête de liste → divergence. « ReadyToSend » = mensonge pour un doc déjà déclaré.
- **Décision (Karl, 2026-07-01)** : fix **état-d'abord**, PAS un 2ᵉ overlay. États dédiés `PendingEReporting` +
  terminal `EReported` ; le doc B2C-reporting ne va plus en `ReadyToSend` au CHECK ; transition vers `EReported` quand
  l'émission passe `Issued` (piloté par le job d'agrégation) ; **suppression** de l'overlay ; liste + fiche lisent le même
  `state`. Concerne les **3 canaux** : marge, taxable (`B2cPlainTaxable*`), export (`B2cExport*`).
- **Livrable** : **ADR-0037** (`docs/adr/ADR-0037-etat-document-ereported-canal-b2c-agrege.md`) + plan
  (`tasks/plan-bug24-etat-ereported.md`), écrits le 2026-07-01. `DocumentState` = donnée d'audit fiscal → ADR.
- **⚠️ Raffinement de la décision — À CONFIRMER par Karl** : l'ancrage code a montré que **le canal n'est PAS connu au
  CHECK** — `DocumentReceivedConsumer` fait `Detected → ReadyToSend` pour **tous** les docs, et la nature B2C
  (marge/taxable/export vs voie-document) est **résolue au JOB** (relecture pivot+mapping, aiguillage document-driven).
  Router vers un `PendingEReporting` **au CHECK** (l'idée notée) exigerait donc de **rejouer la résolution de canal au
  CHECK** (duplication des résolveurs + couplage). L'ADR **tranche donc pour l'option A** (la plus simple correcte) :
  **un seul** état neuf `EReported` + transition `ReadyToSend → EReported` au hook d'émission unique (`EmitOneAsync`,
  couvre les **4** canaux) + suppression de l'overlay. L'**option B** (`PendingEReporting` au CHECK) est en *alternative
  rejetée* motivée dans l'ADR. Si tu veux malgré tout l'option B (aucun `ReadyToSend` menteur même AVANT le job),
  c'est faisable mais + de surface — à trancher avant la session d'implémentation.
- Deux points voisins **posés** dans l'ADR (§Points NON TRANCHÉS), hors périmètre du fix : **D1** rejets d'agrégat (docs
  coincés `ReadyToSend` exclus par attempt-once) ; **D2** bouton « Envoyer » sur un doc B2C *avant* émission.
- **✅ IMPLÉMENTÉ (2026-07-01, working tree)** : état `DocumentState.EReported` + transition `ReadyToSend → EReported`
  (machine à états) + événement `DocumentEReported` + `Document.MarkEReported` + `IDocumentLifecycle.MarkEReportedAsync`
  (non-throwant/idempotent) branché au hook unique `B2cReportingEmitter.EmitOneAsync` (couvre les 4 canaux) ;
  `DocumentStateDisplay` (« E-reporté », vert) ; **overlay read-time SUPPRIMÉ** (service détail + VM + Razor + ActionBar +
  query `GetIssuedEmissionBatchForDocumentAsync`) ; migration backfill **V012** (garde `to_regclass`) ; tests machine à
  états + transitions + bUnit + doubles ajustés. verify-fast (build Debug+Release + unit) vert. **Reste : run-tests
  intégration + revue + commit.**

## Table de correspondance pays — aucun accès console (P1 principe produit) 🧩
- **Relevé Karl** : « je ne vois aucun accès pour la table de correspondance pays (ex. `BEL→BE`) ; ça doit être dans la
  **Supervision**, ce n'est pas ce qui était demandé. »
- **Cause** : la table est **codée en dur dans l'AGENT** (`EncheresV6RowMapper.NonIsoCountryCodeMap` : `ENG/JAP`…), ce qui
  **contredit `F04 §2.4`** (une table de correspondance qui varie par source = **paramétrage, « JAMAIS codée en dur »**).
  BUG-18 l'a rendue « extensible » mais dans le code. `BEL` (alpha-3) n'est pas mappé → transporté brut → **bloqué** (BT-55).
  Supervision : **0 surface pays** aujourd'hui (uniquement des alertes).
- **Cible** : **référentiel pays cross-tenant géré en Supervision** (opérateur d'instance ; normalisation ISO 3166 = technique
  universelle, pas fiscale per-tenant), appliqué **à l'ingestion côté plateforme** ; l'agent transporte `code_pays` brut ;
  on **sort** ENG/JAP/BEL du code agent. Item de build (ADR + plan à prévoir).
- **✅ ADR + plan livrés (2026-07-01)** : `ADR-0038` + `tasks/plan-referentiel-pays.md`. Raffinements conformité (revue
  adverse) : home **NEUTRE** (module `Reference`, **PAS** Supervision — tenant-scopé lecture seule) ; normalisation au
  **read-time** (CHECK/SEND/affichage), **jamais** dans le hash anti-doublon ; mutations **auditées append-only** +
  cible **validée ISO** à l'écriture ; gate **`liakont.settings`** (pas Supervision). **Reste à implémenter.**

## Config d'envoi d'emails en Supervision (Gmail / Office 365) — P2 feature 🧩
- **Relevé Karl** : dans **Supervision** (hors tenant), pouvoir renseigner les données d'envoi d'emails, avec une boîte
  potentiellement **Google ou Office 365** — paramétrage plus complexe que juste SMTP.
- **Cible** : abstraction `IEmailSender` à **providers** (SMTP · Google OAuth2 · Microsoft Graph/O365 OAuth2) ; config +
  secrets **chiffrés**, gérés en Supervision (cross-tenant). Item de build (ADR + plan à prévoir). Note : l'`AlertEmailNotifier`
  existe déjà côté Supervision (alertes) — point de départ.
- **✅ ADR + plan livrés (2026-07-01)** : `ADR-0039` (amende ADR-0018) + `tasks/plan-config-email-instance.md`. Décisions :
  seam vendored `IEmailTransport` **inchangé** (impl `services.Replace`, socle intact) ; **MailKit XOAUTH2 natif** pour
  Gmail/O365 (**0 package**, token via `IEmailOAuthTokenProvider` HttpClient) ; secrets **chiffrés** (`ISecretProtector`,
  purposes dédiés, préservés à l'écriture) ; config **base système** dans un module **instance-level** (`FleetSupervision`/
  `InstanceSettings`, **PAS** Supervision) ; permission neuve **`liakont.instance.settings`** (pas la Supervision read-only) ;
  précédence **DB-autoritaire**.
- **✅ IMPLÉMENTÉ (2026-07-01, branche `feat/recette-encheres-config-email-instance`)** : module `FleetSupervision`
  (store ciphertext base système + migration V003), Host (`EmailSecretPurposes`, `HttpEmailOAuthTokenProvider`,
  `SmtpEmailTransport` provider-aware, service console `IInstanceEmailConfigService`), page `/email-instance` sous
  **Supervision** (gate `liakont.instance.settings`, sélecteur de fournisseur, secrets masqués, bouton « email de test »),
  permission neuve accordée à `superviseur` (matrice §3 sourcée). Tests unitaires + intégration Testcontainers.
  `verify-fast` + `run-tests` verts. Voir la note d'implémentation d'ADR-0039. **Fusion humaine à venir.**

## BUG (affichage) — détail d'émission e-reporting B2C collé à gauche 🎨
- **Relevé Karl (capture)** : `/emissions-marge-b2c/{id}` (fiche BUG-22) — partie haute ET tableau tassés à gauche, titre géant.
- **Cause** : page livrée **sans son `.razor.css` scopé** ; les classes `liakont-doc-detail*` (partagées avec la fiche
  document) restaient au style navigateur (CSS isolé Blazor = scopé par composant).
- **✅ CORRIGÉ (2026-07-01)** : ajout de `src/Host/Liakont.Host/Components/Pages/B2cMarginEmissionDetail.razor.css` (grille
  dense de l'en-tête + tableau pleine largeur + titre 1.5rem), calqué sur `DocumentDetail(.View).razor.css`. À committer + verify.

## Seed de données récentes multi-cas (support de recette) 🧩
- **Demande Karl** : seeder « tout plein » de données sur les derniers jours (fin juin → 01/07/2026) dans `EncheresV6_Demo`,
  couvrant les cas de `tasks/matrice-cas-encheres-b2c.md` (BA/BV, B2C marge/taxable, B2B SIREN, export, étranger, fail-closed).
- **Stratégie** : script SQL idempotent (comme `inject-demo-siren.sql`) qui **clone des lignes existantes valides** (offset
  d'IDs + dates récentes) plutôt que fabriquer à la main (risque colonnes). Couplage marge confirmé **agrégé jour×devise×taux,
  sans clé BA↔BV** → cloner BA(régime 6)+BV(régime 6) datés récents suffit. **En cours.**

## BUG-29 (P2 observabilité) 🐞 — Écran « Exécutions de jobs » TOUJOURS vide (tenant-scopé strict) — relevé Karl 2026-07-01
- **Relevé Karl** : « la planif de job tourne bien, mais l'écran exécution reste toujours vide. »
- **Cause (sourcée code)** : `/admin/jobs/executions` (`AdminJobExecutions.razor`) est **tenant-scopé strict** :
  - La page (`LoadExecutionsAsync`, l.146-152) : si `ActorContext.Current.CompanyId is null` (opérateur plateforme /
    sysadmin **sans société courante**) → retourne `[]` **sans requête** → vide.
  - La query (`PostgresJobExecutionsQueries.ListAsync`, l.32) : `WHERE company_id = @CompanyId` **strict**.
  - Or le job qui tourne (E-reporting B2C, fan-out tous tenants) est un **job SYSTÈME** : le scheduler
    (`JobScheduler` l.108-111) crée l'entrée `job.jobs` avec `companyId = schedule.CompanyId` = la **société
    « porteuse » système** (sentinel plateforme, cf. BUG-4b / `ISystemScheduleHost`), **≠** la société courante de
    Karl. Donc `WHERE company_id = <société courante>` ne ramène jamais les exécutions du job système → **écran vide
    en permanence**, alors même que les jobs s'exécutent (planif OK).
- **Nature** : symétrique EXACT de **BUG-4b** — on a rendu les jobs système *planifiables/consultables* par un
  opérateur plateforme, mais l'écran des **EXÉCUTIONS** est resté tenant-scopé strict → il rate les runs système ET
  est vide pour un opérateur sans société. Faux-vert d'observabilité (« ça tourne, mais je ne vois rien »).
- **À corriger** : pour un opérateur plateforme (et pour les jobs système), l'écran doit exposer les exécutions de la
  **porteuse système** (comme la page de planif le fait déjà via `ISystemScheduleHost`), **sans** ouvrir de fuite
  cross-tenant des jobs *tenant* réels (CLAUDE.md n°9 préservé). Aligner le scoping de `PostgresJobExecutionsQueries`
  + `AdminJobExecutions` sur celui des planifs système.
- **Fichiers (pistes)** : `src/Modules/Job/Web/Pages/AdminJobExecutions.razor` (`LoadExecutionsAsync`, garde
  `CompanyId is null`), `src/Modules/Job/Infrastructure/Queries/PostgresJobExecutionsQueries.cs` (`WHERE company_id`),
  `ISystemScheduleHost` (porteuse système), cohérence avec `AdminJobSchedules` / BUG-4b.
- **Critère d'acceptation** : un job système planifié qui s'exécute est **visible** dans « Exécutions de jobs » pour
  l'opérateur plateforme ; aucune fuite des jobs tenant réels vers un autre tenant ; test.
- **✅ RÉSOLU (2026-07-02)** : repli BUG-4b répliqué sur `AdminJobExecutions.LoadExecutionsAsync` —
  `ActorContext.Current.CompanyId ?? SystemScheduleHost.CrossTenantHostCompanyId`. Un opérateur plateforme sans
  société courante consulte les exécutions de la **porteuse système** (`5c8ed001-…`) au lieu d'une liste vide ; la
  query `PostgresJobExecutionsQueries` reste tenant-scopée STRICTE (aucune fuite — CLAUDE.md n°9). Tests bUnit :
  plateforme → requête sur la porteuse ; socle nu (pas de porteuse) → toujours vide. verify-fast Release vert.

## Écran « Politiques » (= Politiques d'audit) — utilité produit à trancher 🧩 — question Karl 2026-07-01
- **Question Karl** : « écran Politiques => ça sert à quoi ? »
- **Réponse (sourcée code)** : `/admin/audit/policies` (`AdminAuditPolicies.razor`, module **socle vendored**
  `Stratum.Modules.Audit`) = administration de la **piste d'audit générique du socle** : par `EntityType`, quels
  **champs** sont journalisés (`TrackedFields` : « Tous » ou N), politique **Active/Inactive**. Actions : créer /
  modifier / **désactiver** (unitaire + en masse). Ce n'est PAS une surface Liakont — c'est du socle.
- **Point produit à trancher** : sur un produit de conformité fiscale, exposer un bouton **« Désactiver »** une
  politique d'audit à un opérateur est contre-intuitif, voire dangereux. ⚠️ **À VÉRIFIER avant de trancher** : la
  piste d'audit FISCALE (`DocumentEvent`, `MappingChangeLog`, append-only — règle métier n°4) passe-t-elle par ce
  mécanisme configurable du socle, ou est-elle câblée en dur indépendamment ? Si elle en dépend, « désactiver une
  politique » affaiblirait un invariant WORM (**P1**). Si elle est indépendante, l'écran ne pilote que l'audit
  générique (users / config).
- **✅ DÉCISION Karl (2026-07-01) : option (a) — masquer de la nav** (« ça ne nous sert à rien, du moins pour le
  moment »). **IMPLÉMENTÉ** : `AuditNavVisibilityFilter.GetSection()` retire l'item « Politiques »
  (`/admin/audit/policies`) de la section Audit ; « Journal d'audit » demeure ; la **ROUTE reste ouverte** au
  super-admin (socle non modifié — CLAUDE.md n°11). Test `AuditNavVisibilityFilterTests` adapté (prouve l'absence
  de l'item, section non vide). verify-fast en cours.
- **✅ POINT DE FOND VÉRIFIÉ (2026-07-02) — AUCUN risque (Karl avait raison)** : les « Politiques d'audit »
  (`AuditPolicy.IsEnabled`) ne gouvernent **aucun** chemin d'écriture actif dans le code Liakont. Preuves :
  - **Piste fiscale `DocumentEvent`** → `PostgresDocumentUnitOfWork.AppendEventAsync` = `INSERT INTO
    documents.document_events` **inconditionnel** (même transaction que le document ; immuabilité par **trigger
    base** — CLAUDE.md n°4). Zéro consultation d'`AuditPolicy`.
  - **`MappingChangeLog`** (module TvaMapping) : même pattern UnitOfWork ; le module TvaMapping ne référence
    **jamais** le module Audit (`AuditPolicy` / `IAuditQueries` / `IActivityLogger`).
  - **Journal d'activité générique** (`ActivityLogger` → `audit.activities`) : écrit **inconditionnellement**
    (try/catch swallow — INV-AUDIT-002) et ne consulte pas les politiques. `AuditPolicy.IsEnabled` n'est lu par
    **aucun** writer — seulement par les handlers de gestion Get/Set/Disable de l'écran masqué.
  ⇒ L'écran est une surface socle **déclarative NON câblée** ; le masquage est purement visuel et « désactiver »
  une politique n'a aucun effet (ni piste fiscale, ni journal générique). **Pas de P1 ; rien de plus à faire.**
- **Fichiers** : `src/Modules/Audit/Web/Pages/AdminAuditPolicies.razor`,
  `src/Host/Liakont.Host/Navigation/AuditNavVisibilityFilter.cs`.

## BUG-30 (transverse — UX / texte produit) 🎨 — Références de DOCS DE CONCEPTION / codes d'items internes affichés à l'opérateur (à bannir) — relevé Karl 2026-07-01
- **Relevé Karl** : « (les valeurs par défaut viennent de F12 §5.2) » (écrans Alertes / Supervision) — « ce genre de
  référence à des docs de conception est à bannir des écrans, ET de tout texte dans l'appli. » → audit complet demandé.
- **Principe** : aucun texte VU par l'opérateur ne doit citer une spec (`F01`..`F17`, `§`), un ADR, un code d'item
  interne (`FIX###`, `WEB##`, `TVA##`, `PIP##`, `INV-…`, `blueprint §…`) ni un code normatif cryptique (`BT-##`,
  `BG-##`). Même registre que « expert-comptable » (BUG-6), « démo », « évolution ultérieure » : jargon interne interdit
  à l'écran. ⚠️ La **traçabilité** (`detailTechnique`, `fieldRef`, logs) GARDE ses refs — voir « NE PAS toucher ».
- **Méthode** : audit 3 agents (UI Host / pages modules / messages C#), chaque hit tranché EN CONTEXTE visible vs
  commentaire (l'énorme majorité des `F##` du repo sont des commentaires `@* *@` / `///` — écartés).

### FERME — 27 occurrences CONFIRMÉES affichées (à nettoyer)
**Écrans (HelpText / markup) — Host `src/Host/Liakont.Host/Components/` :**
- `AlertesView.razor` : l.17 (F12 §5.1), l.57 (F12 §5.2 — le cas relevé), l.98 (F12 §5.3), l.140 (F12 §5.3), l.184 (F12 §5.3.1)
- `FiscalView.razor` : l.23 (F09), l.36 (F12-A §3.2), l.51 (F09 §5.2), l.66 (F12-A §3.3 **+ « expert-comptable »**) ; l.16 « expert-comptable » (sans code)
- `PaPublicationView.razor` : l.214 (F05 — propriété `TaxReportFieldHelp` → HelpText l.108/119)
- `ProfilView.razor` : l.24 (INV-TENANTSETTINGS-001)
- `MentionsFacturationView.razor` : l.24 (EN 16931 BT-20)

**Messages opérateur (endpoint API → toast/bannière + journal d'activité affiché) :**
- `src/Modules/Documents/Web/DocumentActionsEndpointMapping.cs` : l.177 (F06 §3), l.220 (F06 §4), l.298 (F08 §A.4), l.321 (F08 §A.4)

**Messages opérateur (domaine — motifs de blocage / `Detail` affichés) :**
- `src/Modules/TvaMapping/Domain/Services/TvaMapper.cs` : l.60 (F03 §4.1), l.71 (F03 §3) — motif de blocage
- `.../TvaMapping/Domain/Services/VatCategoryParser.cs` : l.32, l.46 (F03 §2.1) — message d'`ArgumentException` opérateur
- `.../TvaMapping/Domain/Services/MappingTableValidator.cs` : l.37 (F03 §4.1), l.67 (F03 §3), l.76 (F03 §2.1), l.95 (F03 §2.2) — validation table console
- `src/Modules/Documents/Domain/Entities/Document.cs` : l.385 (F06 §4) — `DocumentEvent.Detail` (Supersede), piste d'audit affichée
- `.../Documents/Domain/Entities/DocumentEvent.cs` : l.273-274 (F08 §A.4) — `Detail` (motif COURANT affiché console)
- `src/Modules/Pipeline/Domain/Payments/PaymentAggregationCalculator.cs` : l.65 (F09 §2) — `PaymentExclusion.Detail`

### À TRANCHER — ~58 occurrences « DOUTE » (rendu opérateur non prouvé)
- **Le plus à risque = gardes de value objects `TenantSettings/Domain`** (`TenantProfile`, `TenantAddress`,
  `AlertThresholds`, `AlertRoutingRule`, `ExtractionSchedule`, `PaAccount`) : messages FR préfixés `INV-TENANTSETTINGS-…`
  qui **peuvent remonter à l'écran** comme erreur de validation d'un formulaire console (ex. « INV-TENANTSETTINGS-001 :
  le SIREN doit comporter 9 chiffres »). **À VÉRIFIER** : ces gardes alimentent-elles un message de formulaire ? Si oui →
  retirer le préfixe `INV-…` du texte affiché (garder l'invariant en commentaire / code d'erreur non rendu).
- Autres doutes (probablement PAS affichés, à confirmer) : exceptions techniques (`InvalidDocumentTransition`, FacturX
  `BT-/BG-`), gardes `Require` de `Documents`/`Payments/Domain` (déclenchées sur bug d'appelant), messages « tenant-scopé
  blueprint §7 » (garde interne), statuts de gate/ancrage (`ApprovalGate`, `ArchiveAnchoring`, ADR-0011/0028),
  descriptions `LogActivityAsync` (`DocumentControlActionsService` l.87/112, F08 §A.4).

### NE PAS toucher (refs LÉGITIMES, hors chemin opérateur — un nettoyage aveugle serait une régression)
- **`ValidationIssue.DetailTechnique` + `FieldRef`** (toutes les règles `Validation/Domain/Rules/*`) : contiennent
  F04/F15/ADR/BT/BG MAIS documentés « jamais affichés à l'opérateur ». `DocumentCheckEvaluator` n'agrège QUE
  `MessageOperateur` (propre, sans ref) ; le Host ne référence jamais `DetailTechnique`/`FieldRef`. → traçabilité, GARDER.
- **`SystemJobDefinitions.Label`** (item-codes PIP/TRK/FX/ADR…) : consommés uniquement par `[LoggerMessage]` /
  health-check ; la console affiche `ScheduleName` (sans code). → log, GARDER. *(Piste flairée par l'agent UI = faux positif.)*
- Commentaires `///`/`//`/`@* *@`, tests, `agent/**` : hors sujet.

### Trou connexe (BUG-6 incomplet)
- BUG-6 (bannir « expert-comptable » des messages) n'a nettoyé que les règles de validation : **`FiscalView.razor`
  l.16 et l.66 citent encore « expert-comptable »** dans des HelpText. À traiter avec ce lot.

### Critère d'acceptation
- Aucune des **27** occurrences fermes ne subsiste à l'écran : reformuler en langage opérateur (ce que ça veut dire +
  action concrète), **sans citer la source interne**. Les DOUTE confirmés affichés = nettoyés ; les non-affichés = laissés.
- Refs de traçabilité (`DetailTechnique`/`FieldRef` / Labels de jobs / logs) INTACTES.
- **Garde de non-régression** à envisager : test qui échoue si une chaîne AFFICHÉE (HelpText / markup /
  `MessageOperateur` / `Detail` / `ActionProblem`) matche `F\d|§|ADR-|INV-|BT-\d|BG-\d|FIX\d|WEB\d|PIP\d|blueprint`.
- **Périmètre lourd (5 écrans + 6 fichiers domaine + 1 endpoint) → LOT DÉDIÉ** (feu vert Karl).

### ✅ RÉSOLU (2026-07-02) — les 27 nettoyées
- **27/27 refs retirées** du texte affiché (sens opérateur + fiscal préservés ; commentaires `@* *@` / `///` et
  `DetailTechnique`/`FieldRef`/logs INTACTS) : écrans `AlertesView` / `FiscalView` / `PaPublicationView` / `ProfilView` /
  `MentionsFacturationView` ; endpoint `DocumentActionsEndpointMapping` ; domaine `TvaMapper` / `VatCategoryParser` /
  `MappingTableValidator` / `Document` / `DocumentEvent` / `PaymentAggregationCalculator`. Jargon `defaultBehavior=block`
  retiré aussi. Tests alignés : `TvaMapperTests` (« block » → « bloqué ») + `TvaRuleEditorTests`. **verify-fast Release vert.**
- **Faux positif écarté** : `BuyerLooksProfessionalRule` a un `messageOperateur` propre (ref en `detailTechnique`, non affiché).
- **⏸ EN ATTENTE décision Karl** : « expert-comptable » GARDÉ dans `FiscalView` (l.16/66) + `MentionsFacturationView`
  (l.17/34) — cas d'usage LÉGITIME (décision fiscale client réellement ouverte), distinct des blocages de BUG-6.
- **Reste (non traité, DOUTE)** : gardes value-objects `TenantSettings` préfixées `INV-TENANTSETTINGS-…` qui peuvent
  remonter en erreur de formulaire (ex. « INV-TENANTSETTINGS-001 : le SIREN… ») — à trancher / nettoyer dans un 2ᵉ passage.

## Pattern à garder en tête (dette transverse)
- **Overlay read-time posé sur la fiche mais pas la liste** = divergence possible (cas BUG-24). Règle : le « statut affiché »
  doit avoir **une seule dérivation** consommée par liste + fiche (idéalement l'**état persisté** correct). Auditer les autres
  overlays fiche-seule (récap marge, mentions) — non-statut donc moins critiques.

## BUG-31 (P2 — config email d'instance) 🐞 — L'invitation email (création d'utilisateur / reset mot de passe) ignore la config email EN BASE — relevé Karl 2026-07-02
- **Relevé Karl** : envoi Gmail testé OK (page « Configuration email », ADR-0039) ; puis création d'un
  utilisateur → « ça m'affiche le mot de passe sans jamais parler d'envoi d'email ».
- **Cause (sourcée code)** : les gardes amont testaient `SmtpOptions.IsConfigured` (appsettings, ADR-0018)
  — `KeycloakTenantUserProvisioner.TrySendInvitationAsync` et
  `KeycloakTenantUserManagementService.TrySendResetEmailAsync` — alors que la config d'envoi vit désormais
  EN BASE (chiffrée, autoritaire — ADR-0039). Config Gmail en base + appsettings vides → garde faussement
  « non configuré » → invitation jamais tentée, mot de passe temporaire affiché à l'opérateur. Le transport
  aval (`SmtpEmailTransport.ResolveAsync`), lui, résolvait déjà correctement (DB puis appsettings).
- **✅ RÉSOLU (2026-07-02)** : nouvelle abstraction Host `IEmailSendAvailability.IsConfiguredAsync()`
  implémentée par `SmtpEmailTransport` (MÊME précédence que l'envoi — helper partagé `IsAuthoritativeDbConfig` :
  DB autoritaire → repli appsettings — mais SANS déchiffrer : null-checks seulement) et consommée par les DEUX
  gardes (invitation + reset) à la place de `SmtpOptions`. DI : même instance scoped que `IEmailTransport`
  (assertion d'identité dans `SmtpTransportRegistrationTests`). Méthode privée morte
  `SmtpEmailTransport.IsConfigured()` (sémantique périmée du bug) supprimée.
- **Durci en review (round 1, 3 P2)** : la sonde touche la base système → elle passe DANS le try/catch des
  deux services (un échec de sonde vaut « pas d'envoi possible » : mot de passe remis à l'opérateur — jamais
  la compensation IdP du provisioner, jamais un 500 post-reset) ; et elle ne déchiffre JAMAIS (une clé
  invalide fait échouer l'ENVOI, rattrapé, pas la sonde). Tests : `IsConfiguredAsync` (DB seule = LE bug /
  appsettings seule / rien / protector qui lève) + provisioner « sonde en échec → compte gardé, mdp remis ».
- **⚠️ Angle mort restant (chantier séparé, non traité ici)** : `EmailDocumentDeliveryChannel` (transmission
  Factur-X par email) lit AUSSI `SmtpOptions` (appsettings) et bloque si non configuré — une instance
  configurée UNIQUEMENT en base (a fortiori Gmail/O365 OAuth) ne peut pas transmettre par ce canal. Le
  raccorder exige de porter le canal en provider-aware (OAuth compris) : à backloguer comme item propre.
- **✅ VALIDÉ recette Karl (2026-07-02)** : « ça marche » — invitation Gmail reçue à la création d'utilisateur
  sur l'env Isatech, plus d'affichage du mot de passe.

## BUG-32 (P3 cosmétique — UI) 🎨 — Dialog « Ajouter ou modifier une correspondance » (Référentiel pays) : champs non alignés, visuel brut — relevé Karl 2026-07-02
- **Relevé Karl** : « l'affichage de l'écran de création de correspondance pays est bizarre … c'est surtout
  l'alignement des champs et le visuel qui n'est pas propre. »
- **Cause (sourcée code)** : le corps du dialog (`ReferentielPays.razor`) utilisait des classes CSS
  `referentiel-pays-dialog__field/__label/__input/__text` **définies nulle part** (pas de `.razor.css`, rien
  dans `components.css`/`app.css`) → labels inline collés aux inputs, largeurs inégales, aucun habillage.
- **✅ RÉSOLU (2026-07-02)** : markup remplacé par le gabarit du design system — `FormField` (socle
  `Stratum.Common.UI.Components`, label au-dessus + Required) + `input.form-control` (pleine largeur stylée),
  même pattern qu'`AlertesView`. `data-testid` inchangés (tests bUnit verts, 6/6). Rappel de la règle :
  jamais de classes CSS maison non définies dans une page console (design system Stratum obligatoire).


## CHANTIER GED-PDF — rapatriement des PDF BA/BV depuis la GED EncheresV6 — relevé Karl 2026-07-02
- **Relevé Karl** : « Dans l'état actuel, le plus gros truc qui me manque, c'est l'envoi des PDF des BA/BV »
  (= ramener les PDF de la GED Enchères SVV sur le serveur — pas de génération, pas d'envoi email).
- **Constat côté produit (sourcé code)** : le rail « pièces jointes » ADP05 est construit (agent :
  `IEncheresV6PdfSource` + `FileSystemEncheresV6PdfSource` testé ; serveur : module Ingestion
  `IIngestedPdfStore`, Reconciliation, UI `DocumentAttachment`) mais **débranché** : les deux extracteurs
  renvoient `Array.Empty` en dur (`PervasiveExtractor.cs:197`, `EncheresV6FixtureExtractor.cs:237`) et
  aucune section PDF n'existe dans `agent.json`/factory/wizard.
- **Design source élucidé (référentiel Magic `DataSources.xml`)** : 6 tables — `GED_Type`,
  `GED_Parametre_Global`, `GED_Parametre_Document` (racine `Chemin_stockage` + arborescence paramétrée
  année/mois/dossier/référence/type), `GED_document_joint` (fichier : nom, extension, période, audit),
  `GED_document_joint_Ext` (lien vente), `GED_Relation` (liaison générique Code_flux + Ref_numerique1/2 +
  Ref_alpha1/2 → No_document_joint). **PDF = fichiers sur disque, pas de blob.**
- **Blocage extraction résolu** : ces tables ne sont pas déclarées au dictionnaire SQL Zen (DDF, noms ≤ 20
  car.) — Magic les lit en Btrieve direct. Outil `ged-extract.ps1` livré dans le workspace source (à côté
  d'`EncheresExtract.exe`, hors repo) : déclaration `CREATE TABLE … USING … IN DICTIONARY` (métadonnées
  seules, jamais la data ; `DROP` nu interdit — variante `IN DICTIONARY` imposée) puis extraction JSON.
- **✅ FAIT (2026-07-02)** : `build-sqlserver-from-samples.ps1` étendu aux 6 tables GED (types UTINYINT +
  binaire hex sans corruption ; skip explicite tant que les samples ne sont pas revenus du serveur).
  Review clean (round 2), verify-fast vert.
- **✅ FAIT (2026-07-02, après-midi)** :
  - (1) samples GED extraits (702 documents / 702 relations / 702 ext, 10 param, 13 types, 1 global) →
    `encheresv6-demo-sqlserver.sql` régénéré (gitignoré) et **tables GED appliquées chirurgicalement** sur
    la base live `EncheresV6_Demo` (sans `demo.ps1 source -Force`, qui régénérerait le mot de passe RO et
    casserait la chaîne ODBC chiffrée des agents).
  - (2) dossier GED copié sur le poste (`C:\Source\Enchères SVV\Tools\EncheresExtract\GED`) et **croisé** :
    570/570 BA+BV non supprimés retrouvés, arborescence prouvée
    `<racine>\<No_dossier>\<Année>\<Mois 2 ch.>\<Type_modele>\<Référence>\<Nom_fichier><ext>` ;
    liaison = `GED_Relation.Ref_numerique1` = no_ba (flux 5) / no_bv (flux 6, table `GED_Type`).
    NB : flux 7 (factures client) a `Ref_numerique1 = 0` → liaison NON sourcée, hors périmètre.
  - (3) chantier produit livré : `GedTableEncheresV6PdfSource` (ODBC lecture seule, `SelectGedLinkedPdfSql`
    avec Supprime=0 + scope dossier, chemin reconstruit + garde sous-racine, racine =
    `GED_Param_Document.Chemin_stockage` avec override `gedPdfRoot`) ; délégation `PervasiveExtractor`
    (capacités reflétées) ; config `adapterConfig.EncheresV6.gedPdf="tables"` (+ `gedPdfRoot`), valeurs
    inconnues/orphelines/fixture-mode refusées en français ; tables GED dans `CheckHealth` quand la source
    est active ; gabarits `demo.ps1 agent-config -GedPdfRoot <chemin>`. **+ fix robustesse
    `ExtractionCycle`** : collecte des PDF AVANT le skip anti re-push du document (un PDF tardif/raté après
    acquittement était perdu définitivement) — idempotence par la clé PDF (chemin). Échec ODBC de la
    collecte = `SourceUnavailableException` propagée (cycle réessayable, jamais de PDF perdu en silence) ;
    anomalie de donnée = Warning français + bordereau transmis sans pièce (70 BA réels sans PDF : légitime).
- **⏳ RESTE** : (a) recette Karl (agents : ajouter `gedPdf`/`gedPdfRoot` à l'agent.json installé, relancer
  un run, vérifier la pièce jointe sur un document dans la console) — NB : les documents déjà acquittés ne
  sont ré-examinés que s'ils repassent dans la fenêtre d'extraction ; pour un rattrapage complet, ré-extraire
  la période (reset du filigrane/queue locale — le serveur dédoublonne) ; (b) install prod : la déclaration
  des tables GED au dictionnaire Zen devient une étape d'installation (wizard) puisque l'agent lit via le
  même ODBC ; (c) éventuel fast-follow : liaison GED des factures client (flux 7) si la clé réelle est
  élucidée un jour (Ref_numerique1=0 aujourd'hui).