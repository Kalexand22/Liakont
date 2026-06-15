# F17 (proposition) — Workflow de validation/signature de document réutilisable

> **Statut : 🟨 NOTE DE CONCEPTION (2026-06-15).** Document prêt à être proposé comme F-spec Liakont (note d'orientation, **pas** spec figée). **Aucun code écrit.**
> **Légende des sources** (même registre que F15) : ✅ = sourcé sur texte primaire vérifié (citation + URL) ; 🔶 = lecture courante / à confirmer sur source ; ❓ NON TRANCHÉ = décision à prendre, **owner** nommé. Conformément à CLAUDE.md n°2, **aucune règle fiscale/juridique n'est inventée** : tout point sensible est cité ou marqué ❓.
> **Réalise la demande de Karl :** « concevoir un workflow de validation de document RÉUTILISABLE intégrant la signature électronique, en 2 volets — Yousign (à distance) et un client soft Wacom (sur place) ; creuser le technique ET la validité légale ».

---

## 0. Objet et périmètre

Concevoir, **avant tout lot de dev** :
1. une **abstraction de signature enfichable à capacités** (façon `IPaClient` / `IArchiveStore`) ;
2. un **workflow de validation de document GÉNÉRIQUE et réutilisable** ;
3. son **intégration au workflow d'acceptation d'auto-facture** déjà conçu (ADR-0024) — **sans modifier sa machine** (voir §4) ;
4. le **modèle d'hébergement** du client soft Wacom ;
5. le **niveau eIDAS proportionné** par besoin (recommandation, jamais obligation) ;
6. les **secrets** et le **RGPD biométrie** ;
7. l'**UI console de signature** (déclencher une demande, suivre le statut, consulter la preuve) — **DANS le périmètre F17** (logique déléguée aux handlers MediatR, tests bUnit/Playwright exigés, CLAUDE.md review n°19) ; maquettes/détail au dev.

**Hors périmètre :** toute décision fiscale/juridique non sourcée (owner EC/juridique/DPO) ; l'écriture de code ; le découpage orchestration (manifest).

---

## 1. Cadrage légal SOURCÉ

### 1.1 ✅ La signature électronique n'est PAS requise pour l'acceptation d'une auto-facture

Aucun texte primaire n'impose une signature (a fortiori électronique au sens eIDAS) comme forme de l'« acceptation formelle et expresse » de l'auto-facture sous mandat :

- **CGI art. 289 I-2 :** « Sous réserve de son acceptation par l'assujetti, chaque facture est alors émise en son nom et pour son compte. » — **aucune forme prescrite.**
  https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000048827413
- **BOFiP BOI-TVA-DECLA-30-20-10 (version courante confirmée par F15 §1.5 le 2026-06-15 : 13/08/2021) :** la doctrine décisive est que (a) sous **mandat tacite**, chaque facture exige une **acceptation formelle et expresse** ; (b) sous **mandat écrit et préalable**, les factures n'ont pas à être formellement authentifiées facture par facture ; (c) le contrat doit **stipuler le délai de contestation**. F15 §1.5 cite ce texte aux **§290 / §300 / §390** et marque l'**ancrage exact de ces ancres comme « à revérifier visuellement »**, la **règle étant inchangée et confirmée**.
  https://bofip.impots.gouv.fr/bofip/1525-PGP.html/identifiant=BOI-TVA-DECLA-30-20-10-20210813
  > 🔶 **CORRECTION D'UN FINDING (ne PAS rétrograder F15).** Une version antérieure de cette note prétendait que « la doctrine est aux §420/§430, version 25/09/2019 » et que « F15 cite l'ancienne numérotation 12/09/2012 ». **C'est faux et a été retiré :** F15 §1.5 confirme déjà la **bonne version 13/08/2021** (la 25/09/2019 est elle-même **superseded**) et cite §290/§300/§390. **On NE réécrit PAS l'ancrage de F15** tant que la revérification visuelle BOFiP exigée par F15 §1.5 n'est pas faite. ❓ **NON TRANCHÉ (owner juridique/EC) :** confirmer visuellement, **sur la version 13/08/2021**, le numéro de paragraphe exact et si la doctrine répond à trois questions ouvertes (formulées neutrement, **sans présumer du libellé** — F17 ne cite pas ces phrases) : les parties déterminent-elles librement les modalités d'acceptation ? l'accusé de réception électronique est-il admis comme modalité ? l'acceptation tacite est-elle admise et à quelles conditions ? La conclusion produit ci-dessous ne dépend **pas** du numéro de §.
- 🔶 **Dir. 2006/112/CE art. 224 :** acceptation déterminée par les deux parties, explicite OU implicite (à reconfirmer sur la version consolidée en vigueur si la citation est conservée dans la spec figée — owner juridique).
  https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32006L0112

**Conséquence produit.** Le **fait légal est solide** (✅) : **aucune signature électronique (eIDAS) n'est requise** — CGI 289 I-2 ne prescrit **aucune forme** ; la signature (Yousign/Wacom) est donc une **bonne pratique probatoire optionnelle, jamais une obligation** (la coder en gate Blocking « parce que la loi l'exige » violerait CLAUDE.md n°2/3). **Position produit (défendable, stade build — Karl 2026-06-15) :** une **acceptation enregistrée EXPLICITE** (geste opérateur/mandant tracé append-only) — ou la modalité que le tenant configure (jusqu'à l'accusé de réception horodaté) — constitue l'« acceptation formelle et expresse » qui **ouvre le gate**. Le produit fournit le **mécanisme** (expresse/tacite, ADR-0024), ne **prétend pas** qu'une modalité précise est LA forme légale, et **n'invente aucune règle fiscale** : la modalité est un **paramétrage tenant**, que le **client confirme avec SON expert-comptable** au déploiement (Liakont ne sollicite pas d'EC en build). **Plancher défendable (CLAUDE.md n°3) :** jamais de 389 émis sans **une** acceptation tracée (l'absence laisse le document `Blocked`).

> 🔶 **Modalité paramétrable (le client tranche, pas Liakont) :** l'accusé de réception électronique horodaté **peut** être retenu par un tenant comme modalité d'acceptation expresse (le BOFiP admet plusieurs modalités) — c'est un **paramétrage tenant**, confirmé avec SON EC au déploiement. Le produit ne le pose pas comme fait légal universel ; il l'**offre comme option défendable**, à côté du geste d'acceptation explicite (défaut). Position défendable juridiquement et techniquement à l'instant T, jamais inventée.

### 1.2 ✅ Niveaux eIDAS et valeur probatoire (règlement UE 910/2014)

- SES (art. 3 §10), AES (art. 3 §11 → **art. 26 : 4 exigences cumulatives** : (a) liée au signataire de manière univoque, (b) permet de l'identifier, (c) **créée avec des moyens sous son contrôle exclusif**, (d) liée aux données signées de sorte que toute modification ultérieure soit détectable), QES (art. 3 §12 : AES + dispositif qualifié + certificat qualifié).
  https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:02014R0910-20240520
- **Art. 25 §1 :** non-discrimination — une signature électronique (même non qualifiée) est **recevable comme preuve**. **Art. 25 §2 :** seule la **QES** a l'effet d'une signature manuscrite.
- Droit FR : **C. civ. art. 1366** (équivalence de l'écrit électronique sous réserve d'identification + intégrité) https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000032042461 ; **art. 1367 al. 2** (présomption de fiabilité réservée par décret) https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000032042456 ; **décret n° 2017-1416 art. 1** : présomption réservée à la **seule QES**.
  https://www.legifrance.gouv.fr/jorf/id/JORFTEXT000035676246

**Lecture :** seule la **QES** renverse la charge de la preuve ; SES/AES restent **recevables**, leur fiabilité s'apprécie au cas par cas par le juge. **Aucun niveau n'est imposé pour nos besoins** ⇒ choix proportionné au risque (owner EC).

### 1.3 🔶 Signature Wacom sur place — qualification et RGPD

- La capture manuscrite Wacom **relève a priori de la signature électronique simple au sens art. 3(10) eIDAS** (lecture industrielle courante, **🔶 à confirmer**). Sa **force probante reste faible** et appréciée au cas par cas par le juge (art. 25 §1, **aucune présomption hors QES** — décret 2017-1416), d'autant que **l'identification du signataire et le scellement conditionnent son utilité probatoire** : une signature manuscrite simplement dessinée/scannée n'identifie pas clairement son auteur.
- Elle **peut viser l'AES** si **les 4 exigences de l'art. 26** sont tenues — y compris **art. 26 c (moyens de création sous le contrôle exclusif du signataire)**, qui est précisément le **point faible sur un pad partagé en salle des ventes** (voir §6) — atténué par le fait que le **signataire (le mandant) est présent et identifié en personne** (sert l'art. 26 b ; cf. §10 #8 tranché). **Jamais la QES** (Wacom seul ≠ dispositif + certificat qualifiés, art. 3 §12).
- **RGPD :** la dynamique (pression/vitesse/timing) est une donnée **biométrique comportementale** (art. 4.14), mais ne relève de **l'art. 9 §1 QUE si elle est traitée « aux fins d'identifier une personne de manière unique »** (gabarit de comparaison). Conception **sobre = pas de gabarit** ⇒ **hypothèse de conception « hors art. 9 » À CONFIRMER PAR LE DPO** (voir §6.2), **pas un acquis produit**.

---

## 2. Abstraction de signature enfichable à capacités (→ ADR-0027)

Nouveau module **`Liakont.Modules.Signature`** (pattern Stratum `Contracts/Domain/Application/Infrastructure/Web` + `MODULE.md`/`INVARIANTS.md`). Contrat calqué **exactement** sur `IPaClient`/`PaCapabilities`/`PaCapabilityNotSupportedResult`/`IPaClientFactory` :

- `interface ISignatureProvider` : `SignatureProviderCapabilities Capabilities { get; }` (**seule source de vérité du comportement**) + `RequestSignatureAsync`, `GetSignatureStatusAsync`, `DownloadProofAsync`, `HandleWebhookAsync`.
- `record sealed SignatureProviderCapabilities` :
  - `ProviderName`
  - `Mode` (`[Flags] enum SignatureMode { None = 0, Remote = 1, OnSite = 2 }`) — **localisation** de la signature. ⚠️ valeurs en **puissances de deux distinctes, `None = 0`** : un `[Flags]` avec `Remote = 0` rendrait `HasFlag(Remote)` **toujours vrai** (bug C# classique).
  - `CompletionTransport` (`[Flags] enum CompletionTransport { None = 0, Synchronous = 1, Webhook = 2, Polling = 4 }`) — **comment** la complétion est signalée, **axe ORTHOGONAL à `Mode`** et **COMBINABLE** : un provider peut déclarer `Webhook | Polling` (webhook primaire + **polling de réconciliation** en secours), un distant *polling-only* `Polling`, un capteur sur place `Synchronous`. `HandleWebhookAsync` est pertinent **ssi le flag `Webhook` est positionné** ; le flag `Polling` autorise un job de réconciliation `GetSignatureStatusAsync`.
  - `SupportedLevels` (`[Flags]` sur `SignatureLevel { None=0, Recorded=1, SES=2, AES=4, QES=8 }`) — **ENSEMBLE des niveaux RÉELLEMENT activés** sur le compte, **jamais un max ordonné** : un compte peut offrir `QES` sans `AES`, ou `SES | QES` ; déduire « AES dispo car niveau ≥ AES » ferait demander un niveau non licencié (la capacité reste la source de vérité). `Recorded` (acceptation enregistrée **sans** signature, **défaut conforme ADR-0024**) est **toujours** implicitement disponible. `Supports(level)` = test d'appartenance à l'ensemble.
  - `SupportsSignerIdentityVerification` (pré-vérif d'identité ; **capacité technique, jamais un gate imposé** — voir §7)
  - `SupportsDocumentHashBinding` (scellement art. 26 d)
  - `SupportsBiometricCapture`
  - `SupportsBiometricTemplateMatching` (**OPT-IN, `false` par défaut** — bascule RGPD art. 9, voir §6.2)
  - `MaxDocumentSizeBytes?`
  - méthodes `Supports(SignatureLevel)` / `Supports(SignatureMode)` **centralisant le test** (modèle `PaCapabilities`).

  > **Choix de conception — `Mode` (localisation) et `CompletionTransport` (transport de complétion) sont ORTHOGONAUX.** Une esquisse antérieure dérivait le webhook de `Mode` (`OnSite` ⇒ synchrone, `Remote` ⇒ webhook) : **faux pour une abstraction générique** (un provider distant *polling-only* ou un capteur sur place asynchrone existent — la capacité rapporterait alors un comportement erroné, proche de CLAUDE.md n°8). Le transport est donc modélisé **explicitement**, jamais déduit de la localisation. À graver dans ADR-0027.

- **Capacité/niveau absent → `SignatureRequestResult.NotSupported(SignatureCapabilityNotSupportedResult)`** — message **opérateur FR** (CLAUDE.md n°12), typé, journalisable, **JAMAIS d'exception ni de blocage produit** (modèle `PaSendResult.NotSupported`). Un provider **sans le flag `Webhook`** dans `CompletionTransport` (ex. capteur Wacom sur place, `Synchronous`) renvoie `NotSupported` sur `HandleWebhookAsync`.
- `ISignatureProviderFactory { string ProviderType; ISignatureProvider Create(SignatureProviderAccount account); }` + registre indexé par type. **Sélecteur au composition root** (`AppBootstrap`, modèle IdP), avec une **différence essentielle vs l'IdP : la signature est OPTIONNELLE** (un tenant en `Recorded` n'a aucun provider). Donc `ValidateConfiguration()` **ne bloque le démarrage QUE pour un provider effectivement CONFIGURÉ mais MALFORMÉ** ; **l'absence de tout provider n'est jamais une erreur de démarrage** (la capacité reste indisponible, le défaut `Recorded` fonctionne). Bloquer la plateforme entière faute de signature configurée serait un durcissement non justifié (CLAUDE.md n°3). **Aucun `if (provider is Yousign)`.**
- **Frontière P1 :** un plug-in ne référence que `Signature.Contracts` + Common (NetArchTest) ; **aucun type HTTP ne traverse l'interface** (le payload provider vit dans le plug-in).

---

## 3. Workflow de validation de document générique (→ ADR-0028)

### 3.1 Nom du module (collision résolue)

> **CORRECTION D'UN FINDING P2 (collision de nommage).** Le module **`Liakont.Modules.Validation` existe déjà** (règles métier EN16931 : `src/Modules/Validation/{Contracts,Domain,Infrastructure,Tests.Unit}`). Nommer le nouveau module `Validation.Workflow` l'imbriquait sous le préfixe racine `Liakont.Modules.Validation.*` (risque concret : règle NetArchTest ou filtre DI ciblant `Liakont.Modules.Validation*` qui attrape le mauvais module). **Décision retenue : module racine `Liakont.Modules.DocumentApproval`** (non collisionnant), tranchée **dans ADR-0028 avant gravure**. Tous les noms ci-dessous utilisent `DocumentApproval`.

### 3.2 Agrégat et machine d'état

Agrégat **distinct** `DocumentValidation`, clé `(company_id, document_id, validation_purpose, attempt)` — l'`attempt` (entier ≥ 1) **autorise une nouvelle tentative** après un échec terminal sans jamais muter l'historique (voir « Reprise » ci-dessous ; pour self-billing `attempt` reste **1**). **Machine FERMÉE** (liste close, test produit cartésien, aucun retour depuis un état terminal — règle « machine à états » du repo) :

```
PendingValidation ─┬─▶ Validated             (complétion synchrone : Recorded, ou preuve rattachée en direct)
                   ├─▶ TacitlyValidated      (bascule TenantJobRunner)
                   ├─▶ Rejected / Contested  (deux états DISTINCTS : Rejected = purposes signature ; Contested = self-billing / avoir 261)
                   ├─▶ Expired               (purposes signature uniquement)
                   └─▶ ValidationInProgress  (OPTIONNEL ; purposes signature ASYNCHRONES uniquement)
ValidationInProgress ─┬─▶ Validated / TacitlyValidated   (complétion / bascule)
                      └─▶ Rejected / Expired             (refus / délai)
```

> **Légende (compte d'états — anti-ambiguïté).** Le graphe compte **7 états DISTINCTS** : `PendingValidation`, `ValidationInProgress` (optionnel, purposes async), `Validated`, `TacitlyValidated`, **`Rejected`** (terminal de refus des purposes signature), **`Contested`** (terminal de refus du self-billing, sens fiscal avoir 261), `Expired`. ⚠️ `Rejected` et `Contested` sont **deux états séparés**, **pas** un même état renommé (§4 : « pas de fusion par renommage ») ; **aucun purpose n'utilise les deux**. Sous-graphe self-billing = **4** (`PendingValidation`, `Validated`, `TacitlyValidated`, `Contested`).

> **CORRECTION D'UN FINDING P1 (machine inatteignable).** Une esquisse n'avait qu'une seule arête sortante de `PendingValidation` (vers `ValidationInProgress`), état lui-même **interdit au self-billing** (§4) et limité aux issues d'échec — si bien qu'un document `Recorded`/self-billing **ne pouvait JAMAIS atteindre un état ouvrant le gate**. Corrigé : `PendingValidation` a des **arêtes DIRECTES vers les états terminaux** ; `ValidationInProgress` est un **intermédiaire OPTIONNEL**, jamais un passage obligé.

- `PendingValidation` : état initial. **Arêtes directes vers les terminaux** = chemin du `Recorded` (acceptation enregistrée, sans demande externe) et de toute complétion synchrone.
- `ValidationInProgress` : **uniquement quand la demande est réellement EN COURS** — déterminé par le **RÉSULTAT** de `RequestSignatureAsync` (`Pending` vs `Completed`), **jamais** par une égalité d'enum (`CompletionTransport` étant un `[Flags]` combinable, `Synchronous | Polling` satisfait `!= Synchronous` sans être « purement asynchrone »). Typiquement Yousign *ongoing* / session Wacom ouverte. Transitions vers `Validated`/`TacitlyValidated` (complétion) ou `Rejected`/`Expired` (refus/délai) ; ⚠️ **PAS de retour à `PendingValidation`** (voir §4).
- `Validated` : validation **expresse** enregistrée (`Recorded` OU preuve SES/AES/QES rattachée). ⚠️ **N'ouvre PAS le gate inconditionnellement** : l'ouverture obéit à la **Règle de gate de §4** (niveau de preuve requis par le tenant + forme validée EC pour le self-billing).
- `TacitlyValidated` : bascule par `TenantJobRunner` (SOL06) au-delà de `DeadlineUtc`, **seulement si la politique du `purpose` l'autorise** — **rend le gate éligible, sous réserve de la Règle de gate §4** (pas une ouverture inconditionnelle).
- `Rejected` / `Contested` / `Expired` : **ferment le gate, terminaux** ; correction = document compensatoire, **jamais retour arrière**. ⚠️ `Rejected` (purposes signature) et `Contested` (self-billing, sens fiscal avoir 261) sont **deux états DISTINCTS** — **pas un renommage** (§4 : « pas de fusion par renommage ») ; aucun purpose n'utilise les deux.

> **Reprise après un échec terminal (correction P1).** `Expired`/`Rejected` étant **terminaux et immuables**, on ne « rouvre » jamais une tentative : une nouvelle demande crée un **nouvel `attempt`** (nouvelle ligne `DocumentValidation`, historique précédent intact — append-only). Invariant : **au plus UNE tentative NON terminale à la fois** par `(document_id, purpose)` (index unique partiel **`(company_id, document_id, validation_purpose)` filtré sur les états non terminaux** — tenant-scopé comme la clé d'agrégat ; gouverne la **création**). **Le gate lit la tentative la PLUS RÉCENTE (`attempt` max), indépendamment de sa terminalité** ; que cet état soit `Validated`/`TacitlyValidated` est une **condition NÉCESSAIRE, non suffisante** — l'ouverture effective obéit à la **Règle de gate §4 (3 conditions)**. (« Lire la tentative active » serait faux : après un succès terminal, aucune tentative non terminale n'existe.) ⚠️ **Garde anti-race :** la création de l'`attempt` N+1 est conditionnée, **dans la même transaction**, à ce que l'`attempt` N soit un **échec terminal** (`Expired`/`Rejected`) — sinon (N `Pending` complété en `Validated` par un webhook concurrent **au moment même** où l'on crée N+1) un N+1 non terminal **masquerait le succès N et refermerait le gate à tort**. Test de concurrence requis. Une nouvelle tentative ne naît que si la dernière est un **échec terminal de purpose SIGNATURE** (`Expired`/`Rejected`) — jamais après un succès, jamais en concurrence d'une tentative non terminale. ⚠️ **Le purpose self-billing est EXCLU de ce ré-essai** : son terminal `Contested` est **définitif** (ADR-0024) — une auto-facture contestée n'est **jamais** rouverte par un nouvel `attempt`, la correction passe par **avoir 261 + nouvelle facture (nouveau `document_id`)**. Sans `attempt`, la clé interdirait tout ré-essai après expiration d'une demande Yousign sauf à remplacer le document. **Le self-billing n'a pas d'`Expired`** (sous-graphe 4 états : 1 initial + 3 terminaux) : sa reprise reste la voie ADR-0024 (avoir 261 + nouvelle facture = nouveau `document_id`), **inchangée**.

`validation_purpose` typé (`enum ValidationPurpose`) **déclare un SOUS-GRAPHE AUTORISÉ explicite** du graphe complet (garde de purpose testée) et fixe (a) le **port de couplage** et (b) la **politique de bascule tacite**. ⚠️ Le graphe ci-dessus est l'**union** de tous les purposes ; **aucun purpose n'accède à toutes les arêtes**. Sous-graphe **self-billing** = `{PendingValidation, Validated, TacitlyValidated, Contested}` (= machine ADR-0024 exacte ; `ValidationInProgress`/`Expired`/`Rejected` **hors sous-graphe**, `Contested` = terminal de rejet). C'est ce sous-graphe (et non « le graphe est inchangé ») qui garantit INV-ACCEPT-4. Journal append-only `document_approval_log` : **mutation + ligne de journal dans la MÊME transaction** (UoW gabarit `TvaMapping` / `self_billed_acceptance_log`), immuabilité par **double trigger base** (`BEFORE UPDATE OR DELETE` + `BEFORE TRUNCATE`), **tenant-scopé** (`company_id NOT NULL`).

### 3.3 ⚠️ Cette machine générique N'EST PAS appliquée telle quelle au self-billing

La machine ci-dessus (**7 états distincts**, cf. légende §3.2) **diffère** de la machine ADR-0024 (1 initial + 3 terminaux). **Le purpose `SelfBilledAcceptance` n'utilise PAS `ValidationInProgress`, `Expired` ni `Rejected`** (il utilise l'état distinct `Contested`) — voir §4 pour la résolution complète du conflit (c'était un finding P1).

### 3.4 Réutilisations (la demande clé de Karl)

| Purpose | Port | Politique de bascule tacite | Niveau eIDAS |
|---|---|---|---|
| `MandateSignature` (signature du **contrat de mandat**, ADR-0022 §3) | `IMandateSignatureGate` | aucune (signature expresse attendue) | ❓ tenant (reco §7) |
| `CreditNoteAcceptance` (acceptation de l'**avoir 261**) | `ICreditNoteAcceptanceGate` (exposé par `Mandats.Contracts`, consommé par `Pipeline`) | ❓ NON TRANCHÉ (F15 §6.5 : le 261 ré-entre-t-il dans un cycle ? — owner EC) | ❓ tenant — **présuppose que ce purpose existe (dépend du ❓ #9, owner EC)** |
| `ProgressStatementApproval` (**relevé d'avancement** chantier) | `IReleasableDocumentGate` | selon contrat | ❓ tenant |
| `MultiTierAccountingApproval` (circuit **comptable multi-paliers**) | `IMultiTierApprovalGate` | selon politique | s.o. |
| `MultiPartySignature` (document **co-signé** N parties) | `IMultiPartySignatureGate` (module exposeur/consommateur à nommer en ADR-0028) | timeout `TenantJobRunner` | ❓ tenant |

> **MISSING comblé — agrégation multi-parties/multi-paliers (SLOTS idempotents, jamais un compteur).** Pour `MultiPartySignature` / `MultiTierAccountingApproval`, `Validated` n'est atteint **qu'à complétude**. ⚠️ **PAS un compteur `ReceivedApprovals++`** : un webhook rejoué ou une double approbation d'un même signataire l'incrémenterait et **ouvrirait le gate prématurément**. On modélise un **ensemble fixe de slots identifiés** (`ApprovalSlot { SignerId/TierId, State, ProofId? }`, défini à la création), chaque slot **idempotent par `SignerId`** (une 2ᵉ preuve du même signataire **ne remplit rien de plus**). Complétude = **tous les slots DISTINCTS remplis**, jamais un total d'événements. Test : N-1 slots remplis + un **doublon** d'un signataire déjà compté **n'ouvre jamais** le gate. **Niveau de preuve PAR SLOT (corr.) :** la condition 2 du gate (§4) s'évalue **par slot** (« **tous** les slots ≥ niveau requis »), jamais sur un niveau agrégé (sinon un slot sous-niveau passe = faux-vert) ; test dédié. **Terminaison négative (à trancher) :** un slot **refusé** → bascule immédiate en `Rejected` **ou** attente du timeout ? — règle + test à acter. **Chaîne NetArchTest = les 5 purposes** (§3.4), pas seulement le self-billing : nommer pour chacun le module exposeur et le consommateur du port. À spécifier dans ADR-0028.

---

## 4. Intégration au self-billing (ADR-0024 **amendé**, machine **inchangée**)

> **CORRECTION D'UN FINDING P1 (iso-morphisme faux + extension d'une machine gelée).** La version initiale prétendait que `SelfBilledAcceptance` reste « strictement iso-morphe, AUCUN nouvel état » **tout en** héritant de la machine générique à 7 états — **contradictoire**. ADR-0024 §2/§5 et **INV-ACCEPT-4** figent une machine **fermée** à `PendingAcceptance → {Accepted, TacitlyAccepted, Contested}`, **interdisent tout retour arrière**, et donnent à `Contested` un **sens fiscal précis** (contestation dans le délai → avoir 261). Fusionner `Contested`↔`Rejected` par renommage et ajouter `ValidationInProgress`/`Expired` au purpose self-billing **violait** la règle « machine à états » et INV-ACCEPT-1/4.

**Résolution retenue (option (a) du finding) — la machine self-billing reste EXACTEMENT celle d'ADR-0024 ; le générique se DÉRIVE d'elle, pas l'inverse :**

1. **La machine `SelfBilledAcceptance` d'ADR-0024 est la SOURCE de vérité et reste inchangée :** `PendingAcceptance → {Accepted, TacitlyAccepted, Contested}`, **4 états (1 initial + 3 terminaux)**, **sans** `ValidationInProgress` ni `Expired`.
2. **`DocumentApproval` est conçu comme une GÉNÉRALISATION dont `SelfBilledAcceptance` est une PROJECTION restreinte**, pas une instanciation 1-pour-1. Pour `purpose=SelfBilledAcceptance` :
   - les états `ValidationInProgress` et `Expired` sont **explicitement HORS du purpose** (interdits par une **garde de purpose** testée — toute transition vers eux pour ce purpose est rejetée) ;
   - le vocabulaire fiscal d'ADR-0024 est **conservé** : `Validated` ≡ `Accepted`, `TacitlyValidated` ≡ `TacitlyAccepted`, **`Rejected` n'est PAS utilisé** — le purpose self-billing garde **`Contested`** (sens fiscal : avoir 261). **Pas de fusion de deux notions fiscales par renommage.**
3. **Conséquence sur les invariants ADR-0024 :** INV-ACCEPT-1..4 et 6 **restent inchangés** ; **INV-ACCEPT-5 est formellement AMENDÉ** — le journal du purpose self-billing devient `document_approval_log` (mêmes garanties : append-only en transaction, double trigger base, tenant-scopé `company_id NOT NULL`), qui **remplace** `self_billed_acceptance_log` : **pas de double journalisation**. ⚠️ **L'amendement doit couvrir TOUS les endroits où `self_billed_acceptance_log` est nommé dans ADR-0024** (§6 « Décision », « À la charge des lots d'implémentation », **et** INV-ACCEPT-5) — sinon un lot de dev créerait la migration de l'ancien journal d'après les sections non amendées (faux-vert). L'amendement ADR-0024 (voir §9) **n'ajoute aucun état au purpose self-billing** ; il acte seulement que `SelfBilledAcceptance` est **implémenté via** le module `DocumentApproval` avec **garde de purpose** qui **exclut** `ValidationInProgress`/`Expired` et **conserve** `Contested`. Le **test produit cartésien d'INV-ACCEPT-4 est re-prouvé sur le purpose** (4 états : 1 initial + 3 terminaux, aucun retour arrière).
4. **`ValidationInProgress`/`Expired` sont propres aux purposes signature** (`MandateSignature`, `MultiPartySignature`, …) où une demande externe peut être en cours puis expirer. Ils n'entrent **jamais** dans le périmètre self-billing.

**Port et frontières (chaîne NetArchTest) — inchangés :**
Le **contrat prévu par ADR-0024** — port `ISelfBilledGate` (Mandats.Contracts) interrogé par le pipeline avant l'envoi (INV-ACCEPT-2) — **reste la frontière retenue** ; ⚠️ il est **encore à bâtir** (ni le port ni le module `Mandats` n'existent à ce jour — ADR-0024 les liste « à la charge des lots d'implémentation ») : « conservé / inchangé » désigne **la décision d'archi, pas un acquis implémenté**. En interne, `Mandats` implémentera `ISelfBilledGate` en déléguant à `DocumentApproval` **par ses Contracts**. La bascule `TacitlyValidated/TacitlyAccepted` reste conditionnée **`EstEcrit=true` ET `ContestationDelay` non null** (INV-ACCEPT-3).

```
Documents/Pipeline ──▶ ISelfBilledGate            (jamais Mandats/DocumentApproval/Signature concrets)
Mandats            ──▶ DocumentApproval.Contracts  (jamais le Domain)
DocumentApproval   ──▶ Signature.Contracts         (jamais Yousign/Wacom concret)
Transmission       ──▶ (aucun plug-in signature)
Plug-in signature  ──▶ Signature.Contracts + Common (jamais un autre module ni un autre plug-in)
Job de drain WORM  ──▶ Archive.Contracts (IArchiveService)  (jamais Archive.Domain/IArchiveStore ni backend concret)
```

> **MISSING comblé — stratégie de test de frontière concrète (LE garde-fou P1 du lot).** Assertions **NetArchTest** à écrire dans `src/Modules/<module>/Tests.Unit/ArchitectureTests.cs` (et un test agrégé dans la solution) :
> - **Règle générale = la vraie frontière : aucun module ne dépend des couches `Domain`/`Application`/`Infrastructure` d'un autre module ; seuls les `*.Contracts` sont autorisés.** ⚠️ **NE PAS** écrire un `NotHaveDependencyOn("Liakont.Modules.Mandats")` global — il rejetterait la référence **légitime** à `Mandats.Contracts` qui EST l'architecture voulue.
> - **Le consommateur de `ISelfBilledGate` est `Liakont.Modules.Pipeline`** (pas `Documents`) : `Pipeline` peut dépendre de `Mandats.Contracts`, **jamais** de `Mandats.Domain`/`.Application`/`.Infrastructure`.
> - `Types().That().ResideInNamespace("Liakont.Modules.Mandats").Should().NotHaveDependencyOnAny("Liakont.Modules.DocumentApproval.Domain", "Liakont.Modules.DocumentApproval.Application", "Liakont.Modules.DocumentApproval.Infrastructure")` (Contracts seul autorisé).
> - `Types().That().ResideInNamespace("Liakont.Modules.DocumentApproval").Should().NotHaveDependencyOnAny("Liakont.Modules.Signature.PlugIns.*")`.
> - Pour chaque plug-in : `…PlugIns.Yousign`/`…OnSiteCapture` → dépendances limitées à `Signature.Contracts` + `Liakont.Common.*` (et `NotHaveDependencyOn("Liakont.Modules.Notification")` — voir §5).
> - `Transmission` → `NotHaveDependencyOnAny("Liakont.Modules.Signature.PlugIns.*")`.
> - **Arête WORM (oubli comblé) :** le rapatriement coffre passe par `Archive.Contracts` (`IArchiveService` — surface publique), **jamais** `Archive.Domain` (`IArchiveStore`) ni un backend concret ; il est porté par le **job de drain**, jamais par un plug-in signature. Assertion : le job `NotHaveDependencyOnAny("Liakont.Modules.Archive.Domain", "…Stores.*")`.

**Règle de GATE (définitive — corrections P1).** Un gate de purpose (dont `ISelfBilledGate`) **ouvre** pour un document **ssi**, sur sa **tentative la plus récente**, les **trois** conditions sont réunies :
> 1. l'état ∈ `{Validated, TacitlyValidated}` ; **ET**
> 2. la preuve attachée satisfait le **niveau requis CONFIGURÉ par le tenant** pour ce purpose : `Recorded` (défaut) n'exige aucune preuve ; si le tenant exige `SES`/`AES`/`QES`, un `SignatureProof.Level` **≥ niveau requis** doit être attaché — **un `Recorded` nu NE satisfait PAS une exigence AES/QES** (sinon l'exigence annoncée serait contournable), et une bascule `TacitlyValidated` (sans preuve) ne satisfait que `Recorded` ; **ET**
> 3. **pour le purpose self-billing, et UNIQUEMENT sur une transition `Validated` EXPRESSE** : il faut une **acceptation enregistrée EXPLICITE** selon la **modalité configurée par le tenant** (défaut : geste d'acceptation opérateur/mandant tracé append-only — §1.1). Le gate **ouvre** sur cette acceptation ; **le produit ne bloque PAS « en attendant un EC Liakont »** (position défendable, stade build — le client confirme la modalité avec SON EC au déploiement). **Plancher CLAUDE.md n°3 :** jamais de 389 émis **sans** une acceptation tracée (l'absence d'acceptation laisse le document `Blocked`). ⚠️ **La condition 3 NE s'applique PAS à `TacitlyValidated`** : l'acceptation **tacite** (mandat écrit + non-contestation au délai) n'est par nature **jamais** « expresse » — elle est régie par ses **propres gardes** (INV-ACCEPT-3 : `EstEcrit = true` **ET** `ContestationDelay` non null **ET** délai écoulé), pas par la forme expresse. Lui appliquer la condition 3 **bloquerait à tort** une acceptation tacite valide (contradiction ADR-0024 / INV-ACCEPT-3).

Le durcissement du point 2 vient du **CHOIX du tenant** (paramétrage), **jamais** d'une obligation légale produit (§1.1).

**POINT NON NÉGOCIABLE (CLAUDE.md n°2/3) :** la signature est le **moyen** d'atteindre `Validated/Accepted` quand le tenant l'active (renforcement probatoire) ; **elle ne durcit jamais le gate au nom de la LOI** (§1.1). On **NE durcit PAS** `ISelfBilledGate` en gate de signature *par défaut produit* (affaiblissement inversé = règle inventée). Un document non validé reste `Blocked`, **même « signé »**.

> **MISSING comblé — réconciliation webhook tardive vs append-only / terminalité.** Si `GetSignatureStatusAsync` ou un webhook arrive **après** un état terminal (`Expired`/`Rejected`/`Contested`/`Validated`) : (1) **idempotence par `event_id`** → un événement déjà traité est **ignoré** (pas de nouvelle ligne de journal) ; (2) un événement qui **tenterait** une transition interdite depuis un terminal est **rejeté** par la machine fermée et **journalisé comme tentative rejetée** (le journal append-only enregistre la tentative, **sans** muter l'état terminal) — donc « pas de transition sans ligne de journal » ET « pas de retour arrière » coexistent. À spécifier dans ADR-0028/0029.

---

## 5. Volet à distance — plug-in Yousign (→ ADR-0029)

**Server-side** (.NET 10), API REST **Yousign Public API v3** (sandbox `api-sandbox.yousign.app/v3`, prod `api.yousign.app/v3`), niveaux **SES/AES/QES DÉCLARÉS comme capacités** (jamais supposés ; 🔶 à contractualiser — voir §8). Cycle : draft → upload (**multipart binaire**) → signers/fields → (AES/QES) **pré-vérification d'identité** → activate → webhook.

**Webhook — sécurité (corrections P1) :**
- **Routage tenant AVANT vérification (corrections P1).** Le secret HMAC étant **par tenant**, on ne peut pas vérifier la signature sans d'abord savoir QUEL tenant — et déterminer le tenant depuis le corps serait une **requête cross-tenant interdite** (CLAUDE.md n°9, blueprint §6). L'URL de webhook porte donc un **identifiant OPAQUE et non devinable** (`/webhooks/signature/yousign/{opaqueRef}`). ⚠️ **La couche de routage globale ne résout QU'UN HANDLE DE TENANT** (`{opaqueRef}` → `tenant`, **pur aiguillage, AUCUNE donnée métier**) : elle **n'accède PAS** au `SignatureProviderAccount` (le résoudre pré-scope serait justement un **lookup métier cross-tenant**, interdit). Séquence : `{opaqueRef}` → **handle de tenant** → **ouverture du scope tenant** → **chargement du `SignatureProviderAccount` + secret HMAC depuis la base DE CE tenant** → **vérification HMAC**. L'`opaqueRef` n'est pas un secret (le HMAC reste exigé) ; il route **sans aucun scan ni lookup métier cross-tenant**. Le registre `{opaqueRef}` → `tenant` est un **catalogue système d'infra** (modèle `ICompanyTenantLookup`, contrainte `UNIQUE` sur l'`opaqueRef`), **hors requête métier** (aiguillage d'infra, pas une vue cross-tenant interdite).
- Vérification **HMAC-SHA256 sur le RAW body** (en-tête `X-Yousign-Signature-256`).
- > **CORRECTION D'UN FINDING P1 (frontière plug-in → module vendored).** **NE PAS réutiliser `WebhookSignature.Compute`** : il vit dans `Stratum.Modules.Notification.Domain.Services` (vendored, **couche Domain d'un autre module**). Un plug-in qui le référence viole CLAUDE.md n°6 (plug-in → autre module métier) **et** atteint un Domain vendored. **Décision : le plug-in calcule son HMAC en interne** avec `System.Security.Cryptography` (zéro dépendance inter-module), ou via un helper neutre dans un namespace **`Liakont.*` non-vendored EXPLICITE** (ex. `Liakont.Common.Crypto`), **jamais mêlé au code `Stratum.*` du même assembly** (`Liakont.Common` héberge déjà du `Stratum.*` — le helper doit être identifiable hors socle). Frontière vérifiée par NetArchTest sur le plug-in.
- > **CORRECTION D'UN FINDING P1 (socle vendored / provenance).** Aucune extension du code `Stratum.*` n'est faite. **Si** une primitive devait être ajoutée au socle, elle serait **consignée dans `docs/architecture/provenance-socle-stratum.md`** (CLAUDE.md n°11). ADR-0029 acte explicitement « **aucune modification du socle vendored** ».
- > **CORRECTION D'UN FINDING P1 (comparaison non timing-safe).** Vérification **à temps constant** : `CryptographicOperations.FixedTimeEquals` sur les **octets** du HMAC (jamais `string.Equals` sur l'hex). Le pattern existe déjà au repo (`src/Host/Liakont.Host/FleetApi/FleetApiKeyValidator.cs`). **Test obligatoire :** une signature falsifiée est **rejetée** avant tout traitement.
- **Durabilité (correction P1) :** le handler webhook fait le **strict minimum SYNCHRONE** = vérifier le HMAC puis **persister l'événement brut — authentifié et idempotent (`event_id`) — dans une FILE DURABLE** (`signature_webhook_inbox`, tenant-scopée) **AVANT** de répondre **2xx (< 1 s)**. ⚠️ Ni traitement inline (le rapatriement preuve + documents est lent → dépasse la deadline), ni « 2xx d'abord, traiter ensuite » (un crash **perdrait l'événement**). Le traitement lourd est **asynchrone** : un job (`TenantJobRunner`) **draine l'inbox**, idempotent par `event_id`.
- **Idempotence par clé `(company_id, provider_type, event_id)`** (jamais `event_id` seul — deux tenants/providers peuvent partager un `event_id`), à l'inbox ET au traitement ; **backoff exponentiel + jitter sur 429** sur les appels sortants Yousign.

Sur `signature_request.done` (drainé depuis l'inbox, en asynchrone) : **rapatriement systématique de la preuve + documents signés dans le coffre WORM Liakont** — ⚠️ c'est le **job de drain** qui écrit, **via `Archive.Contracts` (`IArchiveService`)**, **jamais le plug-in Yousign** (qui ne référence que `Signature.Contracts` + Common, §4) ni `Archive.Domain`/un backend concret. Indépendance backend (CLAUDE.md n°6) ; rapatriement **même si** Yousign archive 10 ans.

**Frontière P1 :** référence **uniquement** `Signature.Contracts` + Common ; aucun type HTTP ne traverse `ISignatureProvider`. Capacité QES absente sur une offre → `NotSupported`, jamais d'exception.

---

## 6. Volet sur place — client soft Wacom (→ ADR-0030)

### 6.1 Modèle d'hébergement retenu et frontière physique

**Modèle retenu : desktop-companion (option A).** Exécutable Windows autonome (.NET Framework 4.8, **Wacom Ink SDK for signature**, pad **STU** par USB), son propre installeur. Options **B** (navigateur WebHID — Chromium desktop only) et **C** (service local SigCaptX — empreinte lourde, fragile) **écartées en V1** ; le SDK étant **natif**, le navigateur seul est exclu. Justification : modèle desktop .NET 4.8 + DPAPI **déjà maîtrisé** (l'agent), faisabilité maximale, zéro dépendance navigateur.

> **CORRECTION D'UN FINDING P1 (frontière agent — motif rectifié).** Le client soft ne vit **pas** sous `agent/`. ⚠️ **Le motif initial était faux** : il invoquait « le client doit référencer un contrat plateforme, or `AgentProjectReferenceTests` l'interdit » — mais ce test n'inspecte que les `ProjectReference`, et le client est **purement HTTP** (il parle au proxy `OnSiteCapture` **sans** `ProjectReference` vers un contrat plateforme) : il ne serait donc **pas** bloqué par ce test, même placé sous `agent/`. **Le vrai motif** (qui tient) : **isoler le SDK Wacom natif** (USB, .NET Framework) et poser une **frontière physique** nette (le capteur n'est pas l'agent d'extraction ; aucune logique métier). **Décision gravée dans ADR-0030 :**
> - le client soft vit dans une **racine de solution DISTINCTE** (`clients/OnSiteSignature/` avec sa propre `.sln`), **jamais sous `agent/`** ;
> - **test de pureté symétrique** : le client ne référence **ni** `Liakont.Agent.Contracts` **ni** un module métier (pur capteur) ;
> - **garde au niveau `PackageReference` déclaratif.** ⚠️ **Correction factuelle :** `AgentBoundaryTests` **existe déjà** (`agent/tests/Liakont.Agent.Core.Tests/AgentBoundaryTests.cs`) et opère **au niveau IL** (`GetReferencedAssemblies()`, liste blanche fermée) — il couvre donc déjà les `ProjectReference` ET les `PackageReference` **exercés**. Le **vrai trou résiduel est plus étroit** : sa liste d'assemblies inspectées est **figée (5 entrées)** et un `PackageReference` purement **déclaratif non exercé** (aucun type référencé → absent de `GetReferencedAssemblies`) lui échappe. ⇒ à faire : **ajouter une inspection déclarative des `<PackageReference>`** des `.csproj` sous `agent/` (y interdire le SDK Wacom), **pas** « écrire un test IL inexistant ».
> - **verify-fast doit builder/tester la 3ᵉ solution (faux-vert P2).** `tools/verify-fast.ps1` ne build aujourd'hui que `src/Liakont.sln` + `agent/Liakont.Agent.sln`. Une racine `clients/OnSiteSignature/*.sln` non ajoutée serait **ni buildée ni testée** → le « test de pureté » du client serait **écrit-mais-jamais-lancé**. ADR-0030 + lot orch doivent **ajouter la 3ᵉ solution à verify-fast**, avec **garde bootstrap** (échec si la `.sln` attendue disparaît, sur le modèle `Test-SolItemPending`).

**Frontière P1 (CLAUDE.md n°6) :** le client soft **N'EST PAS l'agent**. Pur **capteur** (geste + horodatage + binding hash) qui POST un objet immuable `{FSS chiffré + image PNG + hash du Factur-X signé + identité opérateur DÉCLARÉE (indicative, NON probante — voir ci-dessous)}` vers un **endpoint plateforme dédié** (le proxy `OnSiteCapture` du module Signature ; HTTPS, **auth derrière l'abstraction IdP** — Keycloak = une impl, jamais d'appel IdP-spécifique ; **tenant-scopé**), **jamais d'accès base**. **AUCUNE logique métier** : toute décision (transition `DocumentValidation`, bascule tacite, ouverture du gate) reste côté plateforme/`TenantJobRunner`.

> **MISSING comblé — tenant-scoping de l'endpoint proxy (CLAUDE.md n°9).** Le proxy `OnSiteCapture` **re-vérifie côté serveur l'appartenance `document_id → company_id`** du caller (clé API/tenant scopé) et **lève `NotFound` sinon** — sur le modèle de `TestFireWebhookHandler` (`subscription.CompanyId == request.CompanyId`). Aucune confiance dans le `company_id` envoyé par le client. **Identité — distinguer le DÉPOSANT du SIGNATAIRE (corrections P1) :** le principal **authentifié** de l'appel proxy (session/clé API IdP) identifie le **DÉPOSANT** (poste / opérateur de la salle des ventes qui téléverse), **PAS la personne qui tient le stylet** (le **mandant** — ✅ décidé §10 #8 : c'est le mandant qui signe en personne, cas vente aux enchères). On enregistre donc **deux champs distincts** : (a) `UploaderPrincipal` = le principal authentifié (fiable, **jamais** le payload) ; (b) `SignerIdentity` = le signataire, qui exige un **mécanisme de liaison VÉRIFIÉ** (art. 26 b eIDAS, « identifier le signataire ») — **jamais** dérivé du déposant, **jamais** cru depuis le payload brut. **Tant que ce mécanisme n'est pas défini, `SignerIdentity` n'est PAS prouvée** et le niveau reste `SES` (cf. §10 #8 ✅ tranché : le **mandant signe en personne** en salle des ventes ; le mécanisme de liaison vérifié = **identification en personne par la SVV** au guichet, à spécifier ADR-0030). À spécifier dans ADR-0030 + test cross-tenant + **test d'usurpation** dont l'oracle est « **`SignerIdentity` n'est JAMAIS dérivée du déposant ni du payload brut** » (et **non** « déposant ≠ signataire » : le cas nominal — opérateur qui téléverse, mandant qui signe — est légitime).

**Niveau eIDAS (correction).**
> **Niveau de départ prudent (AES non prouvée).** Le `SupportedLevels` du plug-in `OnSiteCapture` **ne contient que `{ SES }` au départ** et **n'inclut `AES` qu'après audit documenté** du procédé : scellement **PAdES/CAdES** côté plateforme (art. 26 d) + **identité vérifiée** (art. 26 b) + **contrôle exclusif du moyen de création (art. 26 c)** — ce dernier étant **le point faible d'un pad partagé en salle des ventes** et **explicitement à auditer** (MISSING comblé) ; à noter que le **mandant signataire est présent et identifié en personne** (§10 #8), ce qui sert l'art. 26 b mais ne lève pas à lui seul l'exigence 26 c. **Jamais AES par défaut, jamais QES.** Source de l'audit à référencer dans ADR-0030. *(Nota : l'étiquette `SES` elle-même reste 🔶 (§1.3) ; le choix conservateur n'en dépend pas — seule l'étiquette pourrait évoluer, et **jamais à la hausse sans audit art. 26**.)*

La plateforme calcule un **hash de binding du Factur-X**, le client signe ce hash, la plateforme **vérifie le binding** (re-hash = hash signé) et enregistre la preuve en **WORM** + `DocumentEvent` **append-only**.

> **Primitive de binding = décision PROPRE à ADR-0030 (correction P1).** ⚠️ **NE PAS attribuer ce hash à ADR-0023** : ADR-0023 ne définit **aucun** hash (il ne porte que le scellement PDF/A-3 + le sérialiseur CII + XMP + validation). Le **seul** hash du lot Factur-X est **F16 §7/FX06** = *hash de l'artefact transmis pour la journalisation append-only*, **pas** une primitive de binding calculable. ADR-0030 doit donc acter explicitement : **quels octets exacts** (l'artefact Factur-X scellé transmis, **octet pour octet, sans re-canonicalisation côté client**), **quel algorithme** (SHA-256), et le **même flux d'octets côté client ET plateforme** (sinon « re-hash = hash signé » échoue ou devient contournable).

### 6.2 RGPD biométrie (conclusion rendue conditionnelle)

> **CORRECTION D'UN FINDING P2 (ton sur-affirmatif « hors art. 9 »).** Reformulé en **hypothèse de conception à valider DPO** :

Conception **sobre par défaut, gravée dans la capacité** : `SupportsBiometricTemplateMatching = false`. On capture le **tracé + horodatage + binding hash** comme **preuve d'intégrité et de consentement**, **sans extraire ni stocker de gabarit** de comparaison et **sans** vérification d'identité par la dynamique. La donnée stylet n'est de toute façon **pas exposée en clair** par le SDK (réservée à un *Forensic Document Examiner* sous NDA). **Invariant testable :** *aucun composant ne dérive un feature-vector / gabarit du FSS tant que `SupportsBiometricTemplateMatching = false`* (le flag gouverne le **matching**, jamais la **capture brute**).

**Posture produit (défendable, stade build — Karl 2026-06-15) :** ⚠️ la CNIL liste expressément « la dynamique de signature » parmi les techniques biométriques — on **ne prétend donc PAS** « hors art. 9 » comme acquis. Le **défaut défendable est conservateur** : pas de gabarit (`SupportsBiometricTemplateMatching=false`), finalité strictement limitée à la preuve d'intégrité/consentement, sans identification unique. La qualification fine (art. 9 / AIPD / rétention / droit à l'oubli vs conservation fiscale / consentement B2B) relève du **DPO du client au déploiement** — Liakont ne sollicite pas de DPO en build. **Aucune communication commerciale « sans AIPD »** tant que le client ne l'a pas validé.

**Réserves explicites (NON TRANCHÉ) :**
- la qualification « hors art. 9 » dépend **entièrement** du maintien strict de l'absence de finalité d'identification, **contestable par un DPO** ;
- une **AIPD au titre d'AUTRES critères** (couplage données fiscales : n° facture/mandant/montants ; échelle) peut être requise **indépendamment** de la biométrie — owner DPO.

**Si** `BiometricTemplateMatching` était un jour activée (OPT-IN, isolée derrière le port) : bascule **art. 9** ⇒ consentement explicite (art. 9 §2 a), **AIPD** (art. 35), minimisation, chiffrement du gabarit à clé individuelle / support détenu par la personne (doctrine CNIL biométrie) — **désactivée par défaut, jamais en dur**.

❓ **NON TRANCHÉ (owner juridique/DPO) :** durée de rétention de la preuve ; articulation **droit à l'oubli vs conservation fiscale + append-only WORM** ; validité du consentement en contexte **B2B mandant/mandataire**.

---

## 7. Niveau eIDAS proportionné par besoin (recommandation, jamais obligation)

| Besoin | Recommandation | Base | Statut |
|---|---|---|---|
| Acceptation d'auto-facture (fort volume) | `Recorded` **suffit** (conforme ADR-0024) ; AES « légère » possible si le tenant veut durcir la preuve ; **QES par facture = disproportionnée/coûteuse en général (recommandation, NON impérative) — reste un choix tenant possible si requis** | BOFiP (acceptation libre) + art. 25 §1 | ❓ EC |
| Signature du **contrat de mandat** | AES recommandée, QES possible (opposabilité ; présomption décret 2017-1416 ; équivalence manuscrite art. 25 §2) | art. 25 §2, décret 2017-1416 | ❓ EC/Karl |

> **CORRECTION D'UN FINDING P2/P3 (durcissement masqué par niveau eIDAS).** **Aucun niveau eIDAS n'est imposé par un texte** pour nos besoins. Gravé **noir sur blanc dans ADR-0028/0030 :**
> - le **niveau eIDAS exigé par purpose est un PARAMÉTRAGE TENANT**, jamais un défaut produit ni une obligation codée (y compris « mandat papier scanné importé » comme alternative valide) ;
> - **le gate VÉRIFIE le niveau de preuve attaché contre ce paramétrage** (Règle de gate §4) : un `Recorded` nu ne franchit pas une exigence `AES`/`QES`, sinon l'exigence annoncée serait **contournable** ;
> - **aucun gate n'est conditionné à un niveau eIDAS « parce que la loi l'exige »** ;
> - l'absence de capacité d'un provider retourne le **résultat typé `NotSupported`**, jamais un blocage produit ;
> - `SupportsSignerIdentityVerification` et `requestedLevel` sont des **capacités/paramètres techniques**, **jamais** la justification d'un gate Blocking.
> - **Test explicite :** un tenant en `Recorded` **n'est jamais bloqué** par l'indisponibilité de la signature.

---

## 8. Secrets et RGPD (rappel synthétique)

**Secrets (CLAUDE.md n°10) :** clé API + secret webhook Yousign = **secrets PAR TENANT, chiffrés en base, jamais en clair**. Réutilisation du patron `PaAccount` / `DataProtectionSecretProtector` (module TenantSettings) : entité `SignatureProviderAccount { CompanyId, ProviderType, Environment, AccountIdentifiers (non secrets : workspace, niveau défaut), EncryptedApiKey?, EncryptedWebhookSecret? }`. ⚠️ **L'URL de base N'EST PAS un champ tenant libre (anti-SSRF, correction P1) :** elle est **dérivée d'une allowlist par provider × `Environment`** (ex. Yousign sandbox `api-sandbox.yousign.app` / prod `api.yousign.app`, définies au plug-in/environnement et validées) ; le tenant ne choisit qu'**entre des endpoints CONNUS** (`Environment`), **jamais** une adresse arbitraire. Sinon un admin tenant ferait émettre des appels **authentifiés (porteurs de la clé API)** vers une adresse interne/arbitraire = **SSRF + fuite de la clé API**. Purpose DataProtection dédié versionné (`Liakont.Signature.ProviderAccount.ApiKey.v1`). Le descripteur passé à la fabrique **ne porte aucun secret en clair** ; la clé est résolue **en interne** au moment de l'appel ; `Authorization Bearer` et HMAC webhook construits **en mémoire, jamais journalisés**. Rotation sans redéploiement.
**Client soft :** la clé API plateforme et le cert/clé FSS sont protégés par **DPAPI** (+ entropie applicative) — **jamais en clair**. ⚠️ **NE PAS référencer `ISecretProtector`/`DpapiSecretProtector` de l'agent** (`agent/src/Liakont.Agent.Core`) : le client soft doit rester **pur** (§6.1 — il ne référence ni `Agent.Contracts` ni un module). Deux voies propres, à trancher en ADR-0030 : (a) une **implémentation DPAPI locale au client** (même technique `ProtectedData`, ~30 lignes, zéro dépendance inter-projet) ; (b) **extraire la primitive** `ISecretProtector`/`DpapiSecretProtector` dans un **paquet neutre partagé** (hors `agent/`, ex. `Liakont.Common.Secrets`) réutilisable par l'agent ET le client. ⚠️ **Option (b) — frictions à acter** : un tel paquet consommé par l'agent **net48** devrait **multi-cibler `net48;net10`**, et **s'il est référencé depuis `agent/` il ferait échouer `AgentBoundaryTests`** (liste blanche = `Liakont.Agent.*` + tierces déclarées) ⇒ **préférer l'option (a)** (DPAPI locale au client), ou acter explicitement le multi-targeting **+** l'ajout du paquet à la liste blanche. **`DataProtectionScope` à GRAVER (décision de sécurité, ADR-0030) :** l'agent utilise `LocalMachine` (justifié : service `LocalSystem` + CLI = deux comptes du même poste) ; un **poste de criée partagé** voudrait plutôt **`CurrentUser`** — trancher le scope retenu **et** sa justification. Canal pad→hôte déjà chiffré par le SDK (RSA+AES, anti-rejeu).
**Aucune donnée client dans le code (CLAUDE.md n°7) :** tout en `deployments/<client>/` ou exemples fictifs dans `config/exemples/`.

---

## 9. Plan d'ADR-filles et amendements

**ADR-filles à graver** (filles d'ADR-0022, sœurs d'ADR-0024) :
- **ADR-0027** — Abstraction de signature à capacités : `ISignatureProvider` + `SignatureProviderCapabilities` (`Mode` en **bits distincts `None=0`** ; `CompletionTransport` **orthogonal** à `Mode`, en **flags combinables** `Webhook|Polling`) + `SignatureLevel` + `SignatureCapabilityNotSupportedResult` + module `Signature` ; **`ValidateConfiguration()` ne bloque le démarrage que pour un provider configuré malformé, jamais pour l'absence de provider** (signature optionnelle). Calqué sur `IPaClient`.
- **ADR-0028** — Workflow générique **`Liakont.Modules.DocumentApproval`** (nom non collisionnant tranché) : agrégat `DocumentValidation`, machine fermée à **arêtes directes `PendingValidation`→terminal** (chemin `Recorded`/synchrone) + intermédiaire **optionnel** `ValidationInProgress` (purposes async), **sous-graphe autorisé explicite par purpose** (self-billing = 4 états ADR-0024 : 1 initial + 3 terminaux ; `ValidationInProgress`/`Expired`/`Rejected` hors sous-graphe), agrégation N-parties **par slots identifiés idempotents (jamais un compteur)**, **ré-essai par nouvel `attempt` réservé aux purposes SIGNATURE (self-billing EXCLU : `Contested` définitif, correction par avoir 261 + nouveau document)**, journal append-only en transaction + double trigger, **index unique partiel `(company_id, document_id, purpose)` sur les non-terminaux**, **garde anti-race à la création d'un `attempt`**, **niveau de preuve évalué PAR SLOT** (multi-parties) + **règle de terminaison négative N-parties à acter**, assertions NetArchTest de la chaîne **sur les 5 purposes** (consommateur self-billing = `Pipeline` → `Mandats.Contracts`, jamais une règle anti-`Mandats` globale ; **+ arête WORM job de drain → `Archive.Contracts`, jamais `Archive.Domain`**).
- **ADR-0029** — Plug-in Yousign : niveaux déclarés, **HMAC interne (pas de réutilisation du Domain Notification vendored), comparaison `FixedTimeEquals`, aucune modif socle**, **routage par handle de tenant opaque (aiguillage seul, AUCUN lookup métier pré-scope) → scope tenant → chargement compte + secret depuis la base du tenant → HMAC**, **file durable `signature_webhook_inbox` persistée AVANT 2xx + drain asynchrone par job (download → WORM)**, **idempotence `(company_id, provider_type, event_id)`** (jamais `event_id` seul), multipart binaire, backoff 429, **rapatriement WORM par le JOB DE DRAIN via `Archive.Contracts` (jamais le plug-in)**, secrets par tenant, **URL de base dérivée d'une allowlist par `Environment` (anti-SSRF), jamais un champ tenant libre**, **catalogue système `ICompanyTenantLookup` pour `{opaqueRef}`→tenant (infra, hors requête métier)**, **helper HMAC en namespace `Liakont.*` non-vendored explicite**.
- **ADR-0030** — Client soft Wacom : desktop-companion en **racine `clients/OnSiteSignature/` (jamais sous `agent/`)**, test de pureté symétrique **+ garde PackageReference**, **secrets protégés par DPAPI LOCALE au client OU primitive extraite dans un paquet neutre hors `agent/` (jamais une référence au code de l'agent)**, binding hash + scellement PAdES/CAdES, **`SupportedLevels` = {SES} au départ, AES ajouté seulement après audit (dont art. 26 c)**, **déposant = principal authentifié ; signataire = mécanisme de liaison vérifié séparé (jamais le déposant, jamais le payload)**, **primitive de hash de binding PROPRE à ADR-0030 (octets exacts de l'artefact scellé, SHA-256, même flux client/plateforme — PAS ADR-0023)**, **ajout de la 3ᵉ solution `clients/OnSiteSignature` à verify-fast (garde bootstrap)**, **inspection déclarative des `<PackageReference>` agent** (AgentBoundaryTests existe déjà en IL — combler le trou déclaratif), **`DataProtectionScope` tranché (LocalMachine vs CurrentUser poste partagé)**, **UI console signature (bUnit/Playwright) dans le périmètre**, RGPD sobre **conditionnel DPO**, tenant-scoping serveur du proxy.
- **ADR de package** (Post-Dev Checklist — un par nouveau paquet) : **SDK Wacom Ink** ; **client/HTTP Yousign** éventuel ; **lib PAdES/CAdES** de scellement. À inventorier avant dev.

**Amendements :**
- **F15 §1.5 :** **NE PAS rétrograder** la version (rester 13/08/2021). Ajouter seulement : ❓ revérifier visuellement, **sur 13/08/2021**, l'ancre exacte des paragraphes et le libellé « acceptation libre / accusé de réception admis / acceptation tacite admise » (owner juridique/EC). **Ne pas réécrire l'ancrage tant que la vérification n'est pas faite.**
- **F15 nouveau §1.9 :** constat légal explicite — **la signature électronique n'est PAS requise** pour l'acceptation d'une auto-facture (bonne pratique probatoire, jamais obligation ; interdiction de la coder en gate Blocking).
- **F15 §6/§7 :** questions ouvertes signature (niveau eIDAS par besoin = décision tenant/EC ; identité signataire ; rétention biométrie ; offre Yousign ; audit Wacom).
- **ADR-0024 « Amendement 2026-06-15 » :** `SelfBilledAcceptance` est **implémenté via** `DocumentApproval` avec **garde de purpose** ; **la machine self-billing reste à 4 états (1 initial + 3 terminaux)** (`ValidationInProgress`/`Expired` exclus, `Contested` conservé — pas de fusion avec `Rejected`) ; **INV-ACCEPT-1..4 et 6 inchangés ; INV-ACCEPT-5 amendé** (journal = `document_approval_log`, mêmes garanties, **remplace** `self_billed_acceptance_log`, sans double journalisation) — **amendement à répercuter sur TOUTES les mentions du journal dans ADR-0024 (§6 « Décision » + « À la charge des lots d'implémentation » + INV-ACCEPT-5)**, sinon un lot créerait la migration de l'ancien journal ; test cartésien re-prouvé sur le purpose ; la signature est un **moyen optionnel** d'atteindre `Accepted`, jamais un gate imposé.
- **F06 §4 (anti-doublon) :** **sans objet** ici (concerne ADR-0025) — **NE PAS toucher**.

---

## 10. Points ouverts — défaut défendable pris, le client tranche (stade build)

> **Principe (décision Karl, 2026-06-15) :** Liakont ne sollicite/paie **AUCUN expert-comptable ni DPO** à l'instant T. Pour chaque point, on prend la **meilleure position défendable** (sourcée juridiquement + techniquement), exposée en **paramétrage tenant** ; le **client final tranche avec SON EC / DPO au déploiement**. On **ne bloque QUE** là où aucune position défendable n'existe **et** où un mauvais défaut transmettrait du faux (CLAUDE.md n°2/3). **Aucun de ces points ne stalle le dev** : ce sont des **défauts paramétrables**, pas des gates ; éditeurs et premiers clients affineront par verticale.

| # | Point | Défaut défendable PRIS | Affinage |
|---|---|---|---|
| 1 | Niveau eIDAS par besoin | `Recorded` pour l'acceptation (signature non requise, sourcé) ; niveau du mandat **configurable** (AES recommandée) | tenant + son EC |
| 2 | Signature du mandat | ✅ **DÉCIDÉ (Karl) : choix tenant** (élec OU papier scanné ; `ISignatureProvider` optionnel) | tranché |
| 3 | Ancrage BOFiP 13/08/2021 / art. 224 dir. 2006/112 | la conclusion produit **ne dépend pas** du n° de § ; doctrine confirmée inchangée (F15 §1.5) | re-vérif visuelle si besoin (non bloquant) |
| 4 | « Acceptation expresse » = quelle forme | **acceptation enregistrée EXPLICITE** (geste opérateur/mandant tracé append-only) ; modalité paramétrable (jusqu'à l'accusé de réception horodaté) | tenant + son EC |
| 5 | AES Wacom (art. 26 c) + détenteur FSS | **SES** ; AES seulement après **audit technique** du procédé | investigation tech |
| 6 | Niveaux/offre/limites Yousign | capacités **DÉCLARÉES** au niveau réellement vérifié (sandbox) ; AES/QES = activation au déploiement | investigation tech (coût → Karl) |
| 7 | RGPD biométrie (art. 9 / AIPD / rétention / consentement B2B) | **sobre : pas de gabarit** (`SupportsBiometricTemplateMatching=false`), posture conservatrice | DPO du client au déploiement |
| 8 | Identité du signataire sur place | ✅ **DÉCIDÉ (Karl) : le mandant signe en personne**, vente aux enchères ; liaison = identification en personne par la SVV | tranché |
| 9 | Le 261 ré-entre-t-il dans un cycle d'acceptation | **défaut : oui** (le 261 est self-billed → même discipline d'acceptation que le 389, conservateur ; aucune valeur fiscale inventée) | tenant + son EC |

---

## 11. Garde-fous P1 à tester (rappel)

Chaque garde-fou est **cochable** (1 garde-fou ↔ 1 test clé ↔ 1 ADR-fille) — à reprendre tel quel dans les tests des lots de dev.

| ☐ | Garde-fou | Test clé | ADR |
|---|---|---|---|
| ☐ | Frontières inter-modules (chaîne §4) + arête WORM | Contracts seuls ; job drain → `Archive.Contracts`, jamais `Archive.Domain` ; les **5** purposes | 0028 |
| ☐ | Capacité absente = résultat typé, jamais exception ni blocage | appel sans la capacité → `NotSupported` | 0027 |
| ☐ | Aucun `if (provider is X)` | comportement piloté par capacités seules | 0027 |
| ☐ | `SignatureMode` bits distincts (`None=0`) | `HasFlag(Remote)` faux pour un provider OnSite | 0027 |
| ☐ | Signature OPTIONNELLE — absence de provider ne bloque pas le démarrage | tenant `Recorded` démarre sans provider | 0027 |
| ☐ | `CompletionTransport` flags combinables (≠ déduit de `Mode`) | `Webhook` + `Polling` coexistent | 0027 |
| ☐ | `SupportedLevels` = ensemble explicite (pas un max ordonné) | jamais demander un niveau non licencié | 0027 |
| ☐ | Consommateur `ISelfBilledGate` = `Pipeline` → `Mandats.Contracts` | pas de règle anti-`Mandats` globale | 0028 |
| ☐ | Machine fermée + arêtes directes Pending→terminal + sous-graphe self-billing **4 états** | produit cartésien, aucun retour arrière | 0028 |
| ☐ | Ré-essai par nouvel `attempt` (gate = plus récente ; ≤1 non terminale ; garde anti-race ; **self-billing exclu, `Contested` définitif**) | concurrence complétion/reprise | 0028 |
| ☐ | Journal append-only en transaction + double trigger base | rejet UPDATE/DELETE/TRUNCATE | 0028 |
| ☐ | Tenant-scoping (≥2 bases) + re-vérif `document_id↔company_id` du proxy | test cross-tenant | 0028/0030 |
| ☐ | Identité : déposant = principal authentifié ; signataire = liaison vérifiée séparée | usurpation : `SignerIdentity` jamais dérivée du déposant ni du payload | 0030 |
| ☐ | HMAC webhook : interne (`Liakont.Common.Crypto`), `FixedTimeEquals`, raw body | signature falsifiée rejetée avant traitement | 0029 |
| ☐ | Webhook : handle tenant opaque (catalogue `ICompanyTenantLookup`) → scope → compte+secret → HMAC ; inbox durable AVANT 2xx ; idempotence `(company_id,provider_type,event_id)` ; drain async | crash après 2xx ne perd pas l'événement ; pas de lookup métier pré-scope | 0029 |
| ☐ | Agrégation multi-parties : slots idempotents ; niveau de preuve PAR slot ; terminaison négative | doublon signataire / slot sous-niveau n'ouvre pas le gate | 0028 |
| ☐ | Socle vendored `Stratum.*` non modifié (provenance si touché) | socle-baseline | 0029 |
| ☐ | Client soft hors `agent/` ; pureté `ProjectReference` + inspection déclarative `<PackageReference>` | SDK Wacom n'entre jamais sous `agent/` | 0030 |
| ☐ | `SupportedLevels` OnSite = {SES} tant que l'AES n'est pas auditée | jamais AES/QES par défaut | 0030 |
| ☐ | Secrets jamais en clair ; URL provider en allowlist (anti-SSRF) ; `DataProtectionScope` tranché | aucun secret ni URL libre côté tenant | 0029/0030 |
| ☐ | RGPD : aucun gabarit dérivé du FSS quand `SupportsBiometricTemplateMatching=false` | capture ≠ matching | 0030 |
| ☐ | verify-fast build+teste la 3ᵉ solution `clients/OnSiteSignature` (garde bootstrap) | sinon test de pureté écrit-mais-jamais-lancé | 0030 + lot orch |
| ☐ | UI console signature (déclencher/statut/preuve) : bUnit/Playwright, logique aux handlers MediatR | CLAUDE.md review n°19 | 0028 + lot dev |
| ☐ | **Règle de gate §4** : état nécessaire ∧ niveau ≥ exigence tenant (par slot) ∧ forme self-billing validée EC | `Recorded` nu ne franchit pas AES/QES ; self-billing `Blocked` tant qu'EC n'a pas validé | 0028 |
| ☐ | Gate jamais affaibli **ni durci** au nom d'une obligation inexistante | tenant `Recorded` jamais bloqué *du seul fait de l'absence de provider* ; gate self-billing reste actif | 0028 |

> **Encarts « CORRECTION D'UN FINDING » du corps :** gardés ici comme **note vivante** (anti-rechute) ; **à la gravure des ADR, n'en transposer que la DÉCISION**, pas le récit de l'erreur.
