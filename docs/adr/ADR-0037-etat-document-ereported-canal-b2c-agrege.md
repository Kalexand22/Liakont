# ADR-0037 — État de document `EReported` : le canal e-reporting B2C agrégé transitionne l'état persisté (fin de l'overlay read-time BUG-24)

- **Statut** : Proposé (2026-07-01).
- **Date** : 2026-07-01
- **Nature** : cet ADR **précède** le chantier d'implémentation. Il corrige un **bug de modélisation
  d'état** relevé en recette EncheresV6 (BUG-24) : un document inclus dans une déclaration d'e-reporting
  B2C **agrégée** reste indéfiniment à l'état persisté `ReadyToSend` après une émission **acceptée** par la
  Plateforme Agréée — la liste console l'affiche « À envoyer » alors qu'il est reporté. Les sections
  **Décision** et **Invariants** sont **normatives** (elles décrivent la cible, pas l'état du code : aucun
  invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test). Aucune règle fiscale n'est
  inventée (CLAUDE.md n°2) : la **forme** de l'e-reporting B2C est déjà fixée (F03 §2.5/§2.6, ADR-0015) ;
  cet ADR ne décide **que** le cycle de vie de l'**état** du document, pas la fiscalité.

- **Numérotation** : ADR-**0037**. Prochain numéro libre (`docs/adr/README.md` : max = ADR-0036 ; `0031`
  est attribué **deux fois** — câblage-agent + licence-fluentassertions — donc **jamais réutilisé**). Aucune
  collision : 0037 est neuf.

- **Contexte décisionnel** : bug **BUG-24** (`tasks/bugs-recette-encheres-b2c.md`, session recette
  2026-07-01) ; correctif **incomplet** actuel = overlay read-time (commit `39174c9e`) qui superpose un
  badge « E-reporté » **sur la fiche seule** en interrogeant le journal d'émission au moment de la lecture —
  la **liste** reste fausse et l'**état persisté** reste menteur (correction Karl : « cohérent-mais-faux =
  faux → le bug est l'état persisté incorrect »). Sources code réelles :
  `src/Modules/Documents/Domain/Entities/DocumentState.cs` (enum d'états, persisté en TEXTE, **pas** de
  `EReported`), `src/Modules/Documents/Domain/StateMachine/DocumentStateMachine.cs`
  (`AllowedTransitions`, liste fermée), `src/Modules/Documents/Contracts/Lifecycle/IDocumentLifecycle.cs`
  (seule surface de transition d'état, tenant-scopée + atomique),
  `src/Modules/Pipeline/Infrastructure/B2cReporting/B2cReportingEmitter.cs` (émetteur PARTAGÉ des 4 canaux
  B2C, bloc `status == Issued` l. 152-158, gel du lien reporting↔pièce),
  `src/Modules/Pipeline/Infrastructure/Check/DocumentReceivedConsumer.cs` (CHECK `Detected → ReadyToSend`,
  **commun** voie-document ET e-reporting), les 4 jobs
  `B2c{Margin,Taxable,PlainTaxable,Export}*TenantJob.cs` (découverte par `GetByStateAsync("ReadyToSend")`),
  overlay à retirer (`Host/Liakont.Host/Documents/DocumentDetailConsoleQueryService.cs`,
  `DocumentDetailViewModel.cs`, `Components/DocumentDetailView.razor`, `Components/DocumentActionBar.razor`,
  `Modules/Pipeline/Infrastructure/Queries/PostgresB2cMarginEmissionQueries.cs`). ADR liés : ADR-0015
  (snapshot de ventilation au passage `ReadyToSend`), ADR-0012 (acquittement/réconciliation par statut).

## Contexte

Le module `Documents` porte une **machine à états explicite** (`DocumentStateMachine`, F06 §3) : la liste
FERMÉE des transitions autorisées est la source de vérité unique de la légalité d'un changement d'état, et
**l'état est une donnée d'audit fiscal** persistée en TEXTE dans `documents.state`. Le cycle nominal de la
**voie document** (transmission pièce-à-pièce à la PA) est `Detected → ReadyToSend → Sending →
Issued | RejectedByPa | TechnicalError`. `Issued` signifie « **accepté par la Plateforme Agréée, preuve de
transmission disponible** » (le n° de flux PA **du document lui-même**).

L'e-reporting B2C est une **autre voie** : le document n'est **pas** transmis pièce-à-pièce, il est
**agrégé** (jour × devise × taux) avec d'autres, et c'est l'**agrégat** qui est transmis (F03 §2.5/§2.6 :
TMA1 marge, TLB1 taxable/export…). La « preuve » d'un document reporté n'est donc **pas** un n° de flux
pièce mais le **lot d'émission** (`pipeline.b2c_margin_emissions`) + le **lien reporting↔pièce gelé**
(`documents.reporting_piece_links`, ADR-0007/B2C04).

**Le canal n'est pas connu au CHECK** : `DocumentReceivedConsumer` fait `Detected → ReadyToSend`
(mapping OK + validation OK) pour **TOUS** les documents, voie-document comme e-reporting, sans distinction.
La nature « B2C-marge / B2C-taxable / export / voie-document » est **résolue au moment du job** : chaque job
d'agrégation lit le pool `GetByStateAsync("ReadyToSend")`, **relit le pivot stagé + le mapping TVA**, et ne
retient que les documents de **son** canal (résolveurs `B2cMarginResolver`, `B2cTaxableResolver`, …). C'est
l'**aiguillage document-driven** assumé : la capacité PA ne gate que la transmission au bord, le flux est
piloté par le contenu du document, résolu au traitement — **pas** par un flag persisté à l'ingestion.

**Le bug (BUG-24)** : les 4 jobs B2C convergent tous vers `B2cReportingEmitter.EmitAllAsync → EmitOneAsync`,
qui — sur émission acceptée (`status == Issued`) — écrit le journal append-only et **gèle le lien
reporting↔pièce par document**, mais **ne transitionne JAMAIS l'état du document**. L'anti-doublon
attempt-once vit dans le **journal** (`GetHandledDocumentIdsAsync`, `SELECT DISTINCT document_id`), pas dans
l'état : le document est donc **Issued dans le journal mais figé `ReadyToSend`** — un **état menteur**. La
console lit `documents.state` : la **liste** affiche « À envoyer » pour un document déjà reporté, et les
**compteurs d'attente** sont durablement gonflés par des documents qui ne partiront jamais par la voie
document. Le correctif actuel (commit `39174c9e`) **dérive** l'état à la lecture (overlay read-time,
journal → badge « E-reporté ») **sur la fiche seule** : il masque le symptôme sans corriger la cause
(l'état persisté reste faux) et laisse la **liste** incohérente — deux vues, deux vérités pour la même
donnée. Corriger l'état à la **source** (le persister) est la seule voie qui rende la liste, les compteurs
et la fiche cohérents à partir d'**une** donnée.

## Décision

### 1. Nouvel état persisté `DocumentState.EReported` — la voie agrégée a son état d'aboutissement propre

On ajoute à l'énumération un état **`EReported`** : « le document a été inclus dans une déclaration
d'e-reporting B2C **agrégée** transmise et **acceptée** par la Plateforme Agréée ; la preuve est le lot
d'émission + le lien reporting↔pièce gelé ». Il est **distinct de `Issued`** (voie document, preuve
pièce-à-pièce) : un document B2C n'emprunte **jamais** `Sending`/`Issued` (il n'y a pas de dépôt pièce, pas
de n° de flux PA du document). `EReported` est un état **d'aboutissement** de la voie agrégée : comme
`Issued`, il n'a **aucune transition sortante** (l'anti-doublon et la traçabilité opèrent par journal +
liens gelés, jamais par transition ultérieure). Persisté en TEXTE (nom d'enum, `"EReported"`) comme les
autres états (lisibilité vérificateur, F06 §2).

### 2. Transition `ReadyToSend → EReported`, une seule, dans la machine à états

On ajoute à `DocumentStateMachine.AllowedTransitions` **exactement** le couple
`(ReadyToSend, EReported)`. La transition part de `ReadyToSend` (l'état RÉEL des documents B2C agrégés au
moment où le job les découvre — ils ne passent **jamais** par `Sending`) et **jamais** par `Issued`
(réservé à la voie document). `EReported` reste sans transition sortante. Le `<remarks>` de la machine et
`DocumentStateMachineTests` sont mis à jour en conséquence.

### 3. Point de transition UNIQUE : le hook d'émission partagé, après confirmation PA

La transition est déclenchée **au seul endroit** où l'agrégat B2C confirme l'émission par document :
`B2cReportingEmitter.EmitOneAsync`, dans le bloc `if (status == B2cMarginEmissionStatus.Issued)`
(actuellement l. 152-158), **dans la même boucle `foreach (var contribution in transaction.Contributions)`**
qui gèle déjà le lien reporting↔pièce (`FreezeReportingPieceLinkAsync(companyId, contribution.DocumentId,
…)`). On y ajoute, **à côté du gel** :

```csharp
await lifecycle.MarkEReportedAsync(contribution.DocumentId, emissionBatchId, cancellationToken);
```

Les **4 canaux** (marge TMA1, prix-total enchères TLB1, taxable ordinaire TLB1/TPS1, export TLB1/TNT1)
passent **tous** par `EmitAllAsync → EmitOneAsync` : **un seul hook couvre les quatre**. La transition
vient **après** la confirmation d'acceptation, **par contribution** (clé `document_id`), exactement comme
le gel de lien — l'export unitaire (1 doc = 1 transaction) comme l'agrégat.

### 4. Port `IDocumentLifecycle.MarkEReportedAsync` : non-throwant, idempotent, atomique, tenant-scopé

La transition passe par la **seule surface** de mutation d'état (`IDocumentLifecycle`, frontière
Contracts-only — le module Pipeline référence déjà `Documents.Contracts`, **aucune frontière franchie**,
CLAUDE.md n°6/14). On ajoute :

```csharp
Task MarkEReportedAsync(Guid documentId, Guid emissionBatchId, CancellationToken cancellationToken = default);
```

Contraintes **non négociables**, sur le modèle de `MarkIssuedAsync` et du gel de lien :

1. **Atomique** — état `EReported` **+** fait d'audit append-only `DocumentEReported` dans **la même
   transaction tenant-scopée** (la connexion EST le tenant). Comme toute transition du module.
2. **Non-throwante dans la boucle d'émission** — une exception **après** un POST ACCEPTÉ tomberait dans le
   `catch` de `EmitOneAsync` et retournerait `Technical` : ce serait un **mensonge inverse** (échec affiché
   sur une émission réussie, et documents re-découverts alors que le POST a eu lieu). La transition doit
   donc être **résiliente** comme le gel de lien (échec journalisé en Warning FR avec n° de document,
   CLAUDE.md n°12 — jamais propagé).
3. **Idempotente** — un rejeu (même `document_id` déjà `EReported`) est un **no-op réussi**, pas une
   exception (la machine refuserait `EReported → EReported`). Vérifiée sous le verrou d'état, retournée
   comme succès (motif : les rejeux d'émission et la réconciliation ci-dessous doivent converger sans
   erreur). Un document **hors** `ReadyToSend` (course) n'est **pas** forcé : l'écart est journalisé, pas
   transitionné (jamais de faux audit, pas de TOCTOU — même patron que les `*RecheckAsync`).

L'`emissionBatchId` est porté sur l'événement `DocumentEReported` (détail) : il devient la **source
persistée** du lien « Voir la déclaration » (fiche → `/emissions-marge-b2c/{batchId}`), **extrait de
l'événement DÉJÀ chargé** au read-time (aucune requête journal — l'état, lui, reste piloté par
`documents.state`). Cf. **Amendement 2** (le lien direct est rétabli).

### 5. Suppression de l'overlay read-time (le correctif `39174c9e` devient inutile)

Une fois l'état persisté, l'overlay est **retiré intégralement** (il n'apporte plus rien et entretient la
double-vérité) :

- `DocumentDetailConsoleQueryService` : suppression de `ResolveB2cReportedBatchAsync` + de son appel + de
  la dépendance `IB2cMarginEmissionQueries` au ctor (et de sa registration DI côté Host) ;
- `DocumentDetailViewModel.B2cReportedBatchId` : champ supprimé (l'état vient de `Document.State`) ;
- `DocumentDetailView.razor` : les 3 branches conditionnées par `Model.B2cReportedBatchId` (badge ligne
  État, badge onglet Contrôles, paragraphe `controls-ereported`) — remplacées par le badge d'état standard
  (`State = EReported`) ; le **lien direct** « Voir la déclaration » (fiche → `/emissions-marge-b2c/{batchId}`)
  est **re-sourcé depuis l'événement `DocumentEReported`** (§4) — l'`emissionBatchId` est extrait du `Detail`
  de l'événement déjà chargé (aucune requête journal), cf. **Amendement 2** ;
- `DocumentActionBar.razor` : `CanSend = IsReadyToSend && !IsB2cReported` → le second terme devient inutile
  (l'état n'est plus `ReadyToSend`, `IsReadyToSend` est déjà faux) ; suppression de `IsB2cReported` ;
- `PostgresB2cMarginEmissionQueries.GetIssuedEmissionBatchForDocumentAsync` (+ la méthode du contrat
  `IB2cMarginEmissionQueries`) : supprimée **si** le lien batch est re-sourcé par l'événement (sinon
  conservée uniquement pour ce lien — décision par défaut : re-sourcer par l'événement, supprimer la query).

### 6. Restitution console : `EReported` reflété partout GRATUITEMENT, un seul ajout d'affichage

La liste (`PostgresDocumentQueries.listSql` / `QueryStateCountsAsync`), le service de pagination
(`DocumentConsoleQueryService`) et le badge lisent `documents.state` **directement** : `EReported` y
apparaît **sans modification de requête** dès qu'il est persisté (colonne `state = text NOT NULL` **sans
contrainte CHECK** — aucune migration de contrainte). **Seul ajout requis** : une entrée `EReported` dans
`DocumentStateDisplay` — rang canonique (après `Issued`), libellé FR **« E-reporté »**, `Severity.Success`
— sinon le badge/compteur retombe sur le nom brut. Le bandeau de compteurs
(`DocumentCountsBanner`, clic-filtre inclus) en hérite. La navigation fiche → déclaration se fait par le
**lien direct** « Voir la déclaration » re-sourcé de l'événement (§4), doublé de la **navigation inverse**
(déclaration → document) existante. Cf. **Amendement 2**.

### 7. Migration de backfill : UPDATE légitime de `documents.state` (table NON WORM)

Une migration Documents (prochaine libre — un `V010` est déjà dupliqué dans `Documents/Migrations`, donc
**V012**) **rétro-corrige** les documents déjà reportés mais figés :

```sql
UPDATE documents.documents d
   SET state = 'EReported'
 WHERE d.state = 'ReadyToSend'
   AND EXISTS (SELECT 1 FROM pipeline.b2c_margin_emissions e
                WHERE e.document_id = d.id AND e.status = 'Issued');
```

`documents.documents` **n'est PAS une table append-only WORM** (contrairement à `document_events`,
`reporting_piece_links`, `archive_entries`) : un `UPDATE` de `state` y est **légitime** (c'est le mécanisme
normal de la machine à états). Attention : même base (database-per-tenant), **schémas distincts**
(`documents.*` / `pipeline.*`) → l'ordre d'exécution inter-modules doit garantir que le schéma `pipeline`
et la table `b2c_margin_emissions` **existent** au moment du backfill. Un **fait d'audit** de backfill peut
être inscrit par la même migration (traçabilité de la correction rétroactive) — à défaut, la migration
elle-même est la trace. **Aucune** ligne de journal n'est modifiée/supprimée (backfill = UPDATE d'état
seul).

### 8. Portée — état seulement ; aucune décision fiscale, aucune règle inventée

Cet ADR ne décide **que** le cycle de vie de l'**état** du document sur la voie agrégée. Il **n'invente
aucune** règle fiscale : la forme, les catégories (TMA1/TLB1/TNT1/TPS1), l'agrégation, la traçabilité sont
déjà fixées (F03 §2.5/§2.6, ADR-0015, B2C04). Il **ne modifie** ni le calcul, ni le mapping, ni l'émission,
ni l'anti-doublon (journal inchangé). Il **n'introduit aucun mécanisme transverse nouveau** : réutilise la
machine à états (F06 §3), `IDocumentLifecycle` (transition atomique tenant-scopée), le patron d'événement
append-only. **Aucun code `Stratum.*` vendored modifié** (CLAUDE.md n°11/20).

## Invariants

- **INV-DOC-ER-01** — Un document inclus dans une déclaration d'e-reporting B2C **acceptée** porte l'état
  **persisté** `EReported` (jamais `ReadyToSend` après émission acceptée). La liste, les compteurs et la
  fiche lisent **`documents.state`** — **aucune** superposition read-time, **une** seule source de vérité.
  Prouvé par test d'intégration (émission acceptée ⇒ `state = 'EReported'` en base ⇒ liste ET fiche
  concordent).

- **INV-DOC-ER-02** — `EReported` n'est atteignable que par la transition **`ReadyToSend → EReported`**,
  déclenchée **uniquement** au hook d'émission après confirmation PA (`status == Issued`), par contribution.
  La transition est **atomique** (état + événement `DocumentEReported` append-only, même transaction
  tenant-scopée), **non-throwante** (un échec de transition après un POST accepté ne fait jamais retomber
  l'émission en `Technical`) et **idempotente** (rejeu = no-op réussi). Prouvé par test unitaire (machine à
  états) + intégration (hook).

- **INV-DOC-ER-03** — `EReported` ≠ `Issued` : les deux voies restent distinctes. Un document B2C
  n'emprunte **jamais** `Sending` ni `Issued` ; un document voie-document n'atteint **jamais** `EReported`.
  La machine à états interdit tout autre chemin vers `EReported`.

## Conséquences

**Positif** : l'état persisté redevient **vrai** — liste, compteurs et fiche cohérents à partir d'**une**
donnée (`documents.state`), fin de la double-vérité (correction Karl : cohérent-mais-faux = faux) ; les
compteurs d'attente cessent d'être gonflés par des documents déjà reportés ; **un seul** hook couvre les
**4 canaux** (aucune logique dupliquée par canal) ; la voie agrégée gagne un état d'aboutissement **honnête**
distinct de la voie document ; l'overlay read-time (et sa dépendance cross-module `IB2cMarginEmissionQueries`
côté Host) **disparaît** ; aucune migration de contrainte (colonne `state` sans CHECK) ; aucun mécanisme
nouveau (machine à états + `IDocumentLifecycle` existants).

**À la charge du(des) lot(s) d'implémentation** : ajout de `DocumentState.EReported` + `Document.MarkEReported`
(miroir de `MarkIssued`) ; couple `(ReadyToSend, EReported)` dans `AllowedTransitions` + `<remarks>` +
`DocumentStateMachineTests` ; type d'événement `DocumentEReported` (append-only, portant `emissionBatchId`) ;
port `IDocumentLifecycle.MarkEReportedAsync` + implémentation `DocumentLifecycle` (non-throwante, idempotente,
atomique — **test de chaque branche** : `ReadyToSend`→`EReported` ok, rejeu `EReported`→no-op, hors-état→pas
de transition sans throw) ; appel dans `B2cReportingEmitter.EmitOneAsync` (résilient, comme le gel) avec
**test d'intégration** prouvant qu'un POST accepté laisse le document `EReported` **et** que l'échec de la
transition ne fait pas retomber l'émission en `Technical` ; entrée `EReported` dans `DocumentStateDisplay`
(rang + libellé FR + `Severity.Success`) ; libellé FR **« E-reporté »** de l'événement `DocumentEReported`
dans `DocumentEventDisplay` (piste d'audit, CLAUDE.md n°12) ; suppression **complète** de l'overlay
(5 fichiers Host/Pipeline + registration DI) — le lien direct « Voir la déclaration » est **abandonné**
(navigation inverse, cf. Amendement) ; migration **V012**
backfill (UPDATE `documents.state`, ordre inter-schémas garanti) avec test sur base réelle (documents
figés ReadyToSend + journal Issued ⇒ EReported après migration) ; **test d'isolation cross-tenant** (le
backfill/transition d'un tenant n'affecte pas un autre) ; test bUnit couvrant l'affichage `EReported` en
liste **et** en fiche (page Blazor sans test = P1, CLAUDE.md review n°19).

**Limite** : cet ADR corrige la voie **acceptée** (émission Issued ⇒ état). Il ne traite **pas** le sort des
agrégats **rejetés/en erreur** (cf. Points NON TRANCHÉS D1), ni le comportement du bouton « Envoyer » sur un
document B2C **avant** émission (D2). Ces deux points sont **antérieurs** au bug corrigé (l'overlay ne les
couvrait pas non plus) et distincts ; les ignorer en silence serait un faux-vert — ils sont **posés
explicitement** ci-dessous.

### Points NON TRANCHÉS (défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|-------|------------------------|-------|
| D1 | Sort des documents d'un agrégat **REJETÉ** par la PA (`status = RejectedByPa/Technical`) : ils restent `ReadyToSend` mais sont **exclus des runs suivants** par l'anti-doublon attempt-once (le journal porte une entrée, `GetHandledDocumentIdsAsync` les écarte) → document « coincé », ni reporté ni re-tenté. | **Hors périmètre de cet ADR** : distinct du bug corrigé (émission ACCEPTÉE mais état non transitionné). À traiter par un item/ADR dédié « gestion des rejets d'e-reporting agrégé » (transition vers un état d'échec reporté + re-tentabilité). Posé, non résolu ici. | Produit + Karl |
| D2 | Bouton « Envoyer » (voie document) affiché sur un document B2C **`ReadyToSend` avant émission** : l'ancien overlay ne le masquait qu'**après** émission. À vérifier : le `SendTenantJob` voie-document **exclut-il** déjà les documents document-driven-B2C (aiguillage) ? Si oui, le bouton est inerte ; sinon, risque d'envoi hors canal. | **À vérifier** avant de graver un masquage pré-émission (ne pas inventer un bug ni un fix). Le présent ADR ne change **rien** au pré-émission (il n'agit qu'après acceptation). Aiguillage : cf. principe document-driven. | Karl + implémentation |
| D3 | **Réconciliation récurrente** de l'état résiduel de la fenêtre de crash : si `MarkEReportedAsync` échoue (erreur de persistance / crash) **après** le POST accepté et l'écriture du journal `Issued`, le document reste `ReadyToSend` avec une entrée journal `Issued` → l'anti-doublon attempt-once l'exclut des runs suivants (comme D1), donc il reste affiché « À envoyer » **indéfiniment**. V012 est une réconciliation **one-shot** (déploiement), pas un filet permanent. | **Défaut défendable en build** : fenêtre rare, **aucune erreur fiscale** (obligation e-reporting remplie, pas de double-envoi — garde D1/aiguillage), log 7461 signale l'écart avec action support. À graver plus tard : un **job de réconciliation récurrent** rejouant `MarkEReportedAsync` (déjà idempotent/non-throwant) sur les documents `ReadyToSend` porteurs d'une entrée journal `Issued`. Non implémenté ici (scope distinct). | Produit + implémentation |

Aucun de ces points ne stalle le fix : la voie acceptée (le bug rapporté) est corrigible et testable
immédiatement ; D1, D2 et D3 sont des chantiers voisins, posés pour ne pas les perdre.

## Amendement (2026-07-01, revue adverse BUG-24)

Deux écarts entre la décision initiale et l'implémentation livrée, tranchés ici pour ne pas laisser de
divergence silencieuse (faux-vert) :

1. **Lien direct « Voir la déclaration » — d'abord abandonné, puis RÉTABLI (Amendement 2, retour recette
   Karl).** La 1re passe de revue avait abandonné le lien direct (fiche → `/emissions-marge-b2c/{batchId}`)
   au profit de la seule navigation inverse. La **recette a tranché** : cette navigation directe est utile et
   attendue → le lien est **rétabli**, conformément à l'intention initiale de §4/§6, **re-sourcé depuis
   l'événement `DocumentEReported` DÉJÀ chargé** dans la fiche (l'`emissionBatchId` est extrait du `Detail`,
   entre guillemets « … », côté service de présentation `DocumentDetailConsoleQueryService`). **Ni** requête
   read-time sur le journal (le pattern retiré par le fix), **ni** changement de schéma (pas de nouvelle
   colonne sur la table d'événements append-only) : présentation pure, graceful (lien absent si le batch n'est
   pas extractible, jamais une erreur). L'**état** reste piloté par `documents.state` (INV-DOC-ER-01 intact).
   La navigation inverse (`B2cMarginEmissionDetail` → `/documents/{id}`) subsiste en doublon. Aucun impact fiscal.
2. **Réconciliation récurrente = point ouvert D3** (ci-dessus), pas un filet permanent en V1. Le message
   opérateur 7461 et les commentaires de `B2cReportingEmitter` ont été **corrigés** pour ne plus promettre un
   « prochain backfill » automatique (V012 est one-shot) : ils décrivent désormais l'état réel (émission
   valide, réconciliation hors-ligne) avec action corrective (signalement support).

## Alternatives rejetées

- **Overlay read-time (le correctif actuel `39174c9e`)** : dérive l'état à la lecture depuis le journal
  d'émission → l'overlay n'était posé que sur la **fiche**, la **liste** et les **compteurs** restent faux,
  et l'**état persisté** reste menteur. Deux vues, deux vérités pour la même donnée fiscale.
  **Rejetée** — corriger l'état à la **source** (INV-DOC-ER-01), pas à l'affichage.
- **Option B — état distinct `PendingEReporting` routé au CHECK** (le document B2C ne serait jamais
  `ReadyToSend`, mais `PendingEReporting` dès le CHECK, puis `EReported`) : exigerait de **rejouer la
  résolution de canal** (pivot + mapping + résolveurs B2C) **au CHECK**, alors qu'elle vit **délibérément**
  au job (aiguillage document-driven : le canal est propriété du contenu, résolu au traitement). Duplication
  de la résolution + couplage `CHECK ↔ résolveurs reporting` + réécriture des 4 jobs (`GetByStateAsync`).
  Surface bien plus large pour un gain marginal (le pré-émission `ReadyToSend` n'est **pas** un mensonge :
  le document EST validé et en attente de traitement — le mensonge est de **rester** `ReadyToSend` **après**
  report, ce que l'option A corrige). **Rejetée** — pas le fix correct le plus simple (CLAUDE.md « Simplicity
  First » ; [[prefer-simplest-correct-architecture]]).
- **Router les documents B2C vers `Issued`** : `Issued` porte une **preuve de transmission pièce-à-pièce**
  (n° de flux PA du document) qui **n'existe pas** pour un document agrégé ; exigerait un passage `Sending`
  que le document n'emprunte **jamais** ; confondrait les deux voies dans les compteurs et l'audit.
  **Rejetée** — état dédié `EReported`, sémantique de report agrégé (INV-DOC-ER-03).
- **`MarkEReportedAsync` throwante dans la boucle d'émission** : une exception **après** un POST ACCEPTÉ
  retomberait dans le `catch` de `EmitOneAsync` → statut `Technical` → **mensonge inverse** (échec affiché
  sur une émission réussie, documents re-découverts). **Rejetée** — transition **non-throwante et
  idempotente**, comme le gel de lien (§4).
- **Transition dans chaque job (4 endroits) plutôt qu'au hook partagé** : dupliquerait la transition dans
  les 4 `*TenantJob`, avec risque de dérive (un canal oublié). **Rejetée** — hook **unique**
  `EmitOneAsync`, par contribution, à côté du gel qui a déjà chaque `document_id` (§3).

## Références

- Bug : `tasks/bugs-recette-encheres-b2c.md` (BUG-24, session recette 2026-07-01) ; plan d'implémentation :
  `tasks/plan-bug24-etat-ereported.md`.
- Conception fiscale (forme, **non modifiée** par cet ADR) : `docs/conception/F03-*` §2.5/§2.6 (e-reporting
  B2C enchères : marge TMA1, taxable/export TLB1/TNT1) ; F06 §2/§3 (machine à états, état = donnée d'audit
  persistée en TEXTE). ADR liés : `ADR-0015` (snapshot de ventilation TVA au passage `ReadyToSend`) ;
  `ADR-0012` (acquittement/réconciliation par statut) ; `ADR-0007` (lien reporting↔pièce / sérialisation
  pivot).
- Sources code réelles : `src/Modules/Documents/Domain/Entities/DocumentState.cs` (enum à étendre) ;
  `src/Modules/Documents/Domain/StateMachine/DocumentStateMachine.cs` (`AllowedTransitions` l. 29-49) ;
  `src/Modules/Documents/Domain/Entities/Document.cs` (`MarkIssued`, patron de `MarkEReported`) ;
  `src/Modules/Documents/Domain/Entities/DocumentEventType.cs` (ajouter `DocumentEReported`) ;
  `src/Modules/Documents/Contracts/Lifecycle/IDocumentLifecycle.cs` + `Infrastructure/Lifecycle/DocumentLifecycle.cs`
  (port + impl) ; `src/Modules/Pipeline/Infrastructure/B2cReporting/B2cReportingEmitter.cs` (hook
  l. 152-158) ; les 4 jobs `B2c{Margin,Taxable,PlainTaxable,Export}*TenantJob.cs` (découverte ReadyToSend) ;
  `src/Modules/Pipeline/Infrastructure/Persistence/PostgresB2cMarginEmissionStore.cs` (journal attempt-once) ;
  overlay à retirer : `Host/Liakont.Host/Documents/DocumentDetailConsoleQueryService.cs`,
  `DocumentDetailViewModel.cs`, `Components/DocumentDetailView.razor`, `Components/DocumentActionBar.razor`,
  `Components/DocumentStateDisplay.cs` (ajout `EReported`), `Components/DocumentCountsBanner.razor`,
  `src/Modules/Pipeline/Infrastructure/Queries/PostgresB2cMarginEmissionQueries.cs` (query overlay) ;
  correctif incomplet à remplacer : commit `39174c9e`.
- CLAUDE.md : n°2 (aucune règle fiscale inventée), n°3 (bloquer plutôt qu'affirmer faux — ici ne pas
  affirmer un état faux), n°4 (audit append-only — `document_events`/`reporting_piece_links` inchangés),
  n°6/14 (frontières : Pipeline → `Documents.Contracts` seulement), n°12 (messages FR), review n°19 (page
  Blazor sans test = P1).
