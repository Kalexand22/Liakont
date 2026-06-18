# Plan — Chantier 2 : Adaptateur Chorus Pro (tranche démo)

Plan issu d'une exploration read-only du socle PA + recherche doc officielle AIFE/PISTE/DGFiP.
Branche de référence : `feat/emitter-filled-by-platform`. À relire par Karl avant de coder.

> Règle de session : 1 lot = verify-fast (2 solutions) + tests + codex-review propre + commit/push.
> Aucune règle fiscale inventée, montants `decimal`, secrets chiffrés, frontières blueprint respectées.

> **Révision 2026-06-18 — intégration du redline de Karl.** Corrections de fond appliquées :
> A1 dépôt → `Sending` (jamais `Issued` sans consulterCR=Intégré, faute fiscale évitée) ; A2 endpoints
> `*.piste.gouv.fr` (les `*.aife.economie.gouv.fr` sont décommissionnés depuis le 30/09/2023) ; A3 plus
> d'idempotence par `numeroFluxDepot` (impossible) ; A4 `avecSignature` = décision, pas détail ; A5
> Partie A **non commencée** (vérifié : pas de V011/IPaAccountSecretStore, TenantSettings=V010) ; A6
> enum `OAuth2WithTechnicalAccount` ajoutée en CP1 ; C1 libellés `consulterCR` exacts ; + B/C/D (voir légende).
> **Arbitrages §2bis TRANCHÉS 2026-06-18** (avec investigation) : D7 = item pipeline **PR-PIPE** (support
> `Sending` async, avant tout envoi prod) ; D8 = pas de re-POST auto ; D9 = `avecSignature=false` ;
> C3 = `consulterCompteUtilisateur(email)` + cache ; B5 = Partie A mergée sur `main` d'abord, puis
> branche `feat/pa-chorus-pro`.
>
> **Légende des renvois** (issus du redline) : **A1** dépôt→`Sending` · **A2** endpoints `*.piste.gouv.fr` ·
> **A3** pas d'idempotence par `numeroFluxDepot` · **A4/D9** `avecSignature=false` · **A5** Partie A non
> commencée · **A6** enum `OAuth2WithTechnicalAccount` en CP1. **B1** resolver modèle = `GeneriqueAccountResolver`
> · **B2** câblage via bootstrap dédié (pas `AddConfiguredPaClients`) · **B4** n° de migration coordonné avec
> Partie A · **B5** branche/merge Partie A. **C1** libellés `etatCourantFlux` exacts · **C2**
> `Intégré partiellement`→`RejectedByPa` · **C3** résolution `idUtilisateurCourant` · **C4** traçabilité
> API/fiscale en F18 · **C5** aucun credential loggé · **C6** transport pur (aucun montant). _(B6/B7 du redline
> sont intégrés directement dans D3 et CP1 ; pas de label B3.)_
>
> **Re-revue 2026-06-18 (2) appliquée** : version de spec D8/C3 = V4.14-bis explicitée (à reconfirmer
> V5.00/Swagger en CP0) ; `Incidenté`→`RejectedByPa` (reprise opérateur) tranché ; `C3`/`D9` requalifiés
> « conditionnels » ; blueprint §7 = multi-tenancy (corrigé) ; énumération boundary = 5 gardes réelles ;
> sizing CP3 → (M/L).

---

## 0. Constat — où le socle nous porte (et le piège)

Le socle PA est **complet et PA-agnostique** (`IPaClient`, `PaCapabilities`, `IPaClientRegistry` par
`PaType`, `IPaClientFactory`, résultats typés). Deux plugins de référence existent :

| Axe Chorus Pro | Modèle à copier | Raison |
|---|---|---|
| Auth + transport HTTP | **`SuperPdp`** (`src/PaClients/Liakont.PaClients.SuperPdp/`) | OAuth2 `client_credentials`, jeton court **sans refresh**, cache + renouvellement proactif → proche de PISTE (nuances en CP2) |
| Corps de l'envoi | **`Generique`** (`src/PaClients/Liakont.PaClients.Generique/`) | Chorus Pro **transporte un Factur-X déjà scellé** (porté par `PaSendContext`/FX07) — il ne *construit pas* de payload depuis le pivot |

**Le `GeneriqueClient` n'est PAS un template générique** : c'est un plug-in concret « Essentiel » qui
transporte un Factur-X scellé. On lui emprunte la logique « bloque si artefact absent, ne régénère
jamais » (`GeneriqueClient.cs:58-62` → `BlockedMissingArtifact`).

### Deux faits décisifs relevés dans le code

1. **Aucun plug-in HTTP n'est encore câblé en prod.** `AddB2BrouterPaClient`/`AddSuperPdpPaClient`
   existent mais ne sont **jamais appelés** dans le Host, et **aucun `*AccountResolver` Host n'existe**
   pour eux (**seul `GeneriqueAccountResolver` existe**). Seuls `Generique` (+ `Fake` en dev) sont
   résolvables. Chorus Pro sera le **premier connecteur HTTP réellement branché bout-en-bout**.
2. **`SendTenantJob.cs:122`** (module **Pipeline** : `src/Modules/Pipeline/Infrastructure/Send/SendTenantJob.cs`)
   construit `new PaAccountDescriptor(active.PluginType, tenantId)` **sans `Settings`**. Le plug-in
   **dépendra** donc de son resolver Host pour relire/déchiffrer les secrets. ⚠️ Au futur : le seul
   resolver existant (`GeneriqueAccountResolver`) lit aujourd'hui `descriptor.Settings`, et la lecture
   du coffre `tenantsettings.pa_accounts` via une abstraction **`IPaAccountSecretStore`** reste **À
   CONSTRUIRE** (Partie A, cf. ci-dessous — sa docstring dit « à venir avec le câblage de production »).

### ⛔ Dépendance interne forte : SuperPDP option-1 « Partie A » (générique) — NON COMMENCÉE

Le plan `tasks/plan-superpdp-option1.md` introduit l'**infra OAuth générique partagée**, dont Chorus
Pro a besoin et qu'il ne faut PAS dupliquer. **État réel vérifié dans le repo** (`tasks/todo.md` §2
marque les slices 2-4 `[ ]` ; la NOTE NUIT dit « laissées prêtes à finir en session dédiée ») :

- ✅ Slice 1 (mergé sur `feat/emitter-filled-by-platform`) : `PaAuthMode` (`ApiKey | OAuth2ClientCredentials`),
  `IPaClientFactory.AuthMode`, `IPaClientRegistry.DescribeAuthModes()`.
- ⛔ Slice 2 (**non commencé** — vérifié : pas de migration `V011`, **TenantSettings = V010**, pas de
  `encrypted_client_id`, pas de champ `ClientId` sur `PaAccount`) : migration OAuth + `PaAccount` +2
  champs + commandes/handlers (Protect) + UoW + `PaAccountDto` (`HasClientId/HasClientSecret`).
- ⛔ Slice 3 (**non commencé** — vérifié : `IPaAccountSecretStore` **ABSENT** du repo) : abstraction
  `IPaAccountSecretStore.GetActiveAsync(companyId, pluginType)` (TenantSettings) + impl Postgres + resolver Host.
- ⛔ Slice 4 (**non commencé**) : form générique multi-secrets `ComptesPaView` piloté par **`AuthMode`**.

PISTE = OAuth2 `client_credentials` avec `client_id`/`client_secret` ⇒ **mappera** les colonnes de la
migration OAuth de Partie A. **Conséquence : CP5/CP6 (et tout le câblage bout-en-bout) sont BLOQUÉS
derrière un chantier non démarré.** CP1→CP4 (plug-in pur, mockable) sont codables en parallèle sans le
toucher. **Décision préalable (B5)** : Partie A et Chorus Pro doivent partager une **branche** explicite
(slice 1 est sur `feat/emitter-filled-by-platform`, aucune branche option-1 dédiée n'existe) — Karl
tranche le nom de branche **avant CP1**, sinon conflit de merge sur `Transmission.Contracts`/`TenantSettings`.

---

## 1. L'API Chorus Pro / PISTE (sources officielles — endpoints à RE-VÉRIFIER au catalogue/Swagger PISTE)

> ⚠️ La Spécification Externe API publique consultée est la **V5.00 (2020)** ; les hôtes `aife.economie.gouv.fr`
> qui y figurent sont **décommissionnés depuis le 30/09/2023** (→ `*.piste.gouv.fr`). Tout endpoint
> ci-dessous est à verrouiller sur le **catalogue/Swagger PISTE courant** + tenir compte de la réforme 2026.

- **Auth = DOUBLE, à chaque appel** :
  `Authorization: Bearer <token>` (PISTE, `client_credentials`) **ET**
  `cpro-account: base64(loginTechnique:motDePasse)` (compte technique Chorus Pro, **distinct** du compte
  PISTE). Endpoint jeton qualif : **`https://sandbox-oauth.piste.gouv.fr/api/oauth/token`**, corps
  `application/x-www-form-urlencoded` : `grant_type=client_credentials&client_id&client_secret&scope=openid`
  (le `scope=openid` est un ajout PISTE). Durée du jeton : **piloter sur le `expires_in` réel renvoyé**
  (ne pas figer « 3600 s / pas de refresh_token » comme un fait — à confirmer au Swagger).
- **Émission** : service **`deposerFluxFacture`** (c'est le nom du SERVICE, **pas** un segment REST). Base
  API qualif = **`https://sandbox-api.piste.gouv.fr/cpro/...`** ; le chemin REST réel est **versionné**
  (ex. `/cpro/factures/v1/...`) → **à verrouiller sur le Swagger PISTE, ne pas hardcoder**. Payload JSON :
  `{ idUtilisateurCourant, fichierFlux (Factur-X PDF/A-3 en base64), nomFichier, syntaxeFlux, avecSignature }`.
  Factur-X ⇒ **`syntaxeFlux = IN_DP_E2_CII_FACTURX`**. Retour : `codeRetour`, `libelle`, `numeroFluxDepot`
  = **accusé de RÉCEPTION du flux, PAS preuve d'intégration** (cf. A1).
  `idUtilisateurCourant` se **résout via un appel transverse en amont** (service du chapitre « Cross-functional
  and reference services » de l'Annexe V5.00 — **nom exact à identifier/sourcer, sinon bloquant**, cf. §2bis C3).
- **Statut** : service `consulterCR` (compte rendu d'intégration). Le champ `etatCourantFlux` de la Spec
  V5.00 a **9 valeurs accentuées/casse mixte** (à mapper EXACTEMENT, défaut fail-safe sur valeur inconnue) :
  `Reçu` · `Traité SE CPP` · `En attente de traitement` · `En cours de traitement` · `Incidenté` ·
  `Rejeté` · `En attente de retraitement` · **`Intégré`** · **`Intégré partiellement`**.
- **Factur-X accepté** : OUI (`IN_DP_E2_CII_FACTURX`). Profil/version exacts à verrouiller sur Spécifications
  Externes + validateur FNFE-MPE avant prod (D4) ; un mismatch profil FX07 ↔ `syntaxeFlux` déclarée = `Rejeté`.
- **Qualif** : compte PISTE + app SANDBOX + raccordement déclaré côté portail Chorus Pro + compte
  technique de qualif (login/mdp) rattaché à un SIRET de test + souscription API (CGU).

---

## 2. Décisions

| # | Sujet | Décision | Statut |
|---|---|---|---|
| **D1** | Stockage des secrets | **Étendre le schéma** : réutiliser `encrypted_client_id`/`encrypted_client_secret` (migration OAuth de Partie A, **pas encore dans le repo — TenantSettings=V010**) pour PISTE + **migration suivante** pour le mot de passe du compte technique (coordonner le n° avec Partie A, cf. B4). | ✅ tranché (Karl) |
| **D2** | e-reporting via Chorus Pro | **Hors périmètre du plug-in** : capacités e-reporting = `false` → résultat typé. (a) aucune API e-reporting Chorus Pro publiée (seul `deposerFluxFacture`/e-invoicing existe) ; (b) e-reporting B2B **privé** via PA/PDP. **Nuance** : pour un **émetteur public**, la doctrine (FAQ + art. 123 LF 2026) route le **B2C/G2C** par Chorus Pro — mais sans API et rattachement CMP non vérifié (note CMP). | ✅ tranché (plug-in) |
| **D3** | Tests de contrat | **Reco** : tests unitaires dédiés (patron `Generique`/`RoutedHttpMessageHandler`), **pas** l'héritage de `PaClientContractTests`. **Vraie raison** (corrigée) : la suite envoie **sans `PaSendContext`** → une PA *transport-only* bloque sur artefact absent → échoue le cas `Issued` nominal (ce n'est PAS une histoire de « construction de payload » : `SuperPdp` construit un payload et hérite bien de la suite). | reco — à valider |
| **D4** | Profil Factur-X | **Reco** : la démo transporte un Factur-X **déjà scellé/validé en amont** ; verrouiller profil/version sur Spécifications Externes + FNFE-MPE **avant prod**. Tracer la cohérence FX07 ↔ `syntaxeFlux` (mismatch = `Rejeté`). | reco — à valider |
| **D5** | Mapping état du dépôt | **CORRIGÉ (A1)** : dépôt accepté (`codeRetour` OK) → **`PaSendState.Sending`** (jamais `Issued`). `Issued` posé **uniquement** quand `consulterCR` = `Intégré`. Patron existant : `SuperPdpResponseMapper.cs:214,220-228,249` (« JAMAIS émis par défaut », CLAUDE.md n°3). Sous-problème pipeline → §2bis D7. | ✅ corrigé |
| **D6** | 3ᵉ secret générique | **Reco** : surfacer le couple compte technique via une **forme d'auth générique réutilisable** — nouvelle valeur `PaAuthMode` `OAuth2WithTechnicalAccount` (décrit « OAuth2 + compte technique », pas « Chorus Pro » : tout futur PA au même schéma la réutilise). Form/store réagissent **par AuthMode**, jamais par `if (pa is ChorusPro)` (**blueprint §2 règles 2 et 4** + §6 « Rôles et frontières des modules » ; CLAUDE.md §6 — **PAS** « blueprint §8 » qui = montants, ni « blueprint §7 » qui = multi-tenancy). Login = identifiant ; mot de passe = secret chiffré. | reco — à valider |

> **Note CMP (Crédit Municipal de Paris — établissement public faisant du B2C).**
> Le CMP fait de l'e-reporting B2C (ventes aux enchères, flux **10.3**) + e-reporting de paiement (part
> frais acheteur, flux **10.4**) **potentiellement dû selon l'exigibilité** (`vatOnDebits` — paramètre non
> tranché ; cf. F09 + PIP03a qui suspend les bordereaux Mixte). Question fiscale ouverte : ce B2C transite-t-il par **Chorus Pro** (doctrine émetteur public,
> circuit **Hélios** auquel le CMP — banque à comptabilité propre — n'appartient pas de façon vérifiée)
> ou par une **PDP** de droit commun ? Verdict investigation = **`unresolved-needs-arbitration`**.
> ⚠️ **Provenance à valider** : le backlog d'orchestration **suppose** B2Brouter pour le CMP
> (`orchestration/items/CMP.yaml` CMP02/CMP04, défaut hérité de DR17 « B2Brouter = PA #1 »), **sans
> décision tracée spécifique au CMP** (aucun ADR). À **acter explicitement par Karl** — ce n'est pas une
> décision établie. Quoi qu'il en soit, **ce point ne concerne pas l'adaptateur Chorus Pro** (dépôt
> Factur-X B2G) ; c'est une **arbitrage fiscal CMP** (DGFiP/AIFE + expert-comptable ; garde-fou PIP01b
> bloque l'envoi tant que la table TVA n'est pas validée). Ne PAS coder de routage sur hypothèse.

### 2bis. Points ouverts — TRANCHÉS (2026-06-18, avec investigation)

- **D7 (pipeline asynchrone) — TRANCHÉ : item pipeline d'abord (→ PR-PIPE).** Vérifié :
  `SendTenantJob.cs:590-634` n'a pas de `case Sending` → `Sending` tombe dans le `default` →
  `MarkTechnicalErrorAsync` (EventId 7208) → réémission au cycle suivant. Pour Chorus Pro (asynchrone,
  sans idempotence) = **double dépôt**. SuperPDP a la **même lacune latente** (jamais exercée, non câblé).
  → Ouvrir un **item plateforme PR-PIPE** : `case Sending` qui **persiste le `PaDocumentId`**, garde le
  doc en attente, confirme via `consulterCR`/`GetDocumentStatusAsync`, finalise `Issued` **uniquement**
  sur `Intégré`, **ne re-dépose jamais**. Bénéficie aussi à SuperPDP. **Chorus Pro n'envoie pas en prod
  avant PR-PIPE.**
- **D8 (idempotence/timeout) — TRANCHÉ : pas de re-POST auto.** Investigué sur la **Spec Externe Annexe
  Services API V4.14-bis** (faute de section équivalente consultée en V5.00 — **à reconfirmer V5.00/Swagger
  en CP0/F18** ; pagination non vérifiée) : aucune clé d'idempotence client, aucun `idExterne` ; dédup
  serveur = **métier par n° de facture**, à
  l'intégration (pas une idempotence de flux). → timeout/réseau = **`TechnicalError` SANS re-POST
  automatique**, reprise opérateur (sinon double dépôt = double facture, CLAUDE.md n°3). Test « timeout
  sans `numeroFluxDepot` ne re-POST jamais ». Cohérent avec PR-PIPE (qui supprime la réémission aveugle).
- **D9 (`avecSignature`) — décision interne TRANCHÉE (`false`) ; 1 vérif externe résiduelle bloquante.**
  Vérifié côté code : l'artefact « scellé » = PDF/A-3 conforme **non signé** (`FacturXBuilder.Seal`
  n'applique **aucune** signature ; `PaSendContext` = `ReadOnlyMemory<byte>` opaque, aucune métadonnée ;
  module `Signature`/Yousign disjoint). → **`avecSignature = false`** (constante = reflet de notre artefact).
  ⚠️ **Reste à confirmer en CP0/F18** : que Chorus Pro **accepte** un Factur-X non signé (si une signature
  est exigée, la décision ET la démo cassent). Future facture **signée** → étendre `PaSendContext` (drapeau
  alimenté par le build, **impact contrat**).
- **C3 (`idUtilisateurCourant`) — méthode TRANCHÉE, déclencheur CONDITIONNEL (sous réserve Swagger).**
  Vérifié : pas de service « utilisateur courant » ; `consulterCompteUtilisateur` (entrée = email de
  connexion, `adresseEmailConnexionUtilisateur`) / `RechercherUtilisateursValides` fournissent
  `idUtilisateur`. ⚠️ **Cardinalité de `idUtilisateurCourant` NON confirmée** (évolutive selon le
  service/la version — raisonnement V4.x) → **vérifier au Swagger PISTE pour la version ciblée**. **Si
  requis** : réglage compte « email de connexion du compte technique » + résolution via
  `consulterCompteUtilisateur(email)` **mise en cache par compte** ; **sinon** l'omettre du payload.
  Sourcer le service ET la cardinalité dans F18.
- **B5 (branche) — TRANCHÉ : Partie A mergée d'abord.** Livrer Partie A (slices 2-4) comme item/branche
  dédié → **merger sur `main`**, **puis** brancher `feat/pa-chorus-pro` sur `main`. CP1-CP4 peuvent
  démarrer en parallèle si l'ajout de l'enum `OAuth2WithTechnicalAccount` (CP1) est coordonné (un seul
  chantier édite `PaAuthMode.cs`).

---

## 3. Prérequis EXTERNES (à lancer en parallèle du dev — délai d'obtention)

Bloquants pour CP8 (envoi réel) uniquement ; CP1→CP7 sont codables avec mocks.

1. Compte portail **PISTE** (`piste.gouv.fr` / `developer.aife.economie.gouv.fr`) + application **SANDBOX**
   (→ `client_id`/`client_secret` qualif).
2. **Raccordement API** déclaré côté portail Chorus Pro qualif (rattacher l'app SANDBOX).
3. **Compte technique de qualification** (login/mdp → `cpro-account`) rattaché à un **SIRET de test**.
4. Souscription aux **API Chorus Pro** (CGU cochées sur l'app PISTE).

---

## 4. Plan ordonné (lots, sizing repris de la tranche démo)

### PR-INT — Prérequis interne #1 : SuperPDP option-1 « Partie A » (générique) — ⛔ NON COMMENCÉ
Slices 2-4 de `plan-superpdp-option1.md` : migration OAuth, `IPaAccountSecretStore`, form multi-secrets
piloté par `AuthMode`. **Non démarrée** (vérifié §0). **Item/branche dédié → mergé sur `main` AVANT de
brancher `feat/pa-chorus-pro`** (B5). **Bloque CP5/CP6** ; CP1→CP4 avancent en parallèle.

### PR-PIPE — Prérequis interne #2 : support PA asynchrone dans le pipeline (D7) — ⛔ NON COMMENCÉ
`case Sending` dans `HandleSendResultAsync` (`SendTenantJob.cs:590-634`) : **persiste le `PaDocumentId`**,
garde le doc en attente de confirmation, la reprise relit via `consulterCR`/`GetDocumentStatusAsync` et
finalise `Issued` **uniquement** sur `Intégré`, **sans jamais re-déposer** un `Sending` (supprime la
réémission aveugle, cohérent D8). **Item plateforme séparé** (à seeder dans le backlog d'orchestration),
**bénéficie aussi à SuperPDP**. **Prérequis de l'envoi RÉEL en prod / de la démo e2e** ; CP1→CP4 + tests
unitaires/sandbox au niveau plug-in n'en dépendent PAS.

### CP0 — Tracer les sources API/fiscales (CLAUDE.md n°2) — (S)
Créer **`docs/conception/F18-Plugin-ChorusPro.md`** citant les pages de la Spec Externe V5.00 + Swagger
PISTE : `syntaxeFlux` (`IN_DP_E2_CII_FACTURX`), **libellés `etatCourantFlux` exacts**, `scope=openid`,
format `cpro-account`, codes `codeRetour`, service de résolution `idUtilisateurCourant`. Sans source
interne tracée, le code viole CLAUDE.md n°2 (les chaînes sont authentiques, pas inventées — c'est un
**défaut de procédure** à combler avant de coder les constantes).

### CP1 — Squelette du plug-in `Liakont.PaClients.ChorusPro` — (M)
- Projet `.csproj` : **ProjectReference UNIQUE** `Transmission.Contracts` ; packages = `DI.Abstractions`
  + `Extensions.Http` (versions centralisées) ; `InternalsVisibleTo` vers `.Tests.Unit`.
- **Ajouter ici la valeur d'enum `PaAuthMode.OAuth2WithTechnicalAccount`** (A6 — elle vit dans
  `Transmission.Contracts`, **aucune dépendance** à Partie A/V011/store ; la consommer en CP1 sans la
  définir = ne compile pas).
- `ChorusProDefaults` (PaType `"ChorusPro"`, PaName `"Chorus Pro"`, URLs `*.piste.gouv.fr` sandbox/prod,
  en-têtes, timeouts) — **endpoints depuis F18/Swagger, pas en dur**.
- `public sealed ChorusProClientFactory : IPaClientFactory` — `PaType => Defaults.PaTypeKey`,
  `AuthMode => PaAuthMode.OAuth2WithTechnicalAccount` (D6), `Create(PaAccountDescriptor)` résout via
  `IChorusProAccountResolver`.
- `ChorusProPaClientRegistration` : `AddChorusProPaClient(this IServiceCollection)`
  (`AddHttpClient` TLS1.2/1.3 + `TryAddEnumerable(Singleton<IPaClientFactory, ChorusProClientFactory>)`).
- `internal sealed ChorusProClient : IPaClient` (squelette des **9 méthodes** — `SendDocumentAsync`,
  `SendPaymentReportAsync`, `GetDocumentStatusAsync`, `ListTaxReportsAsync`, `GetTaxReportAsync`,
  `GetAccountInfoAsync`, `GetTaxReportSettingAsync`, `EnsureTaxReportSettingAsync`,
  `GetGeneratedDocumentAsync` — + la **propriété** `Capabilities`). Capacité absente → résultat typé
  `PaSendResult.NotSupported`/`PaGeneratedDocument.NotSupported`, jamais d'exception.
- `IChorusProAccountResolver` (interface **publique** dans le plug-in) + `ChorusProAccountConfig`
  (record, secrets en clair en mémoire seulement, `ToString` caviardé).
- **Test `ChorusProBoundaryTests`** (NetArchTest, patron `SuperPdpBoundaryTests.cs:20,29,39,52,73` — **les
  5 gardes réelles**) : (1) **aucune réf IL** vers un autre plug-in `Liakont.PaClients.*` ; (2) **aucune réf
  IL** vers un autre module `Liakont.Modules.*` hors `Transmission.Contracts` ; (3) **pas** de dépendance
  (NetArchTest) sur `Transmission.Infrastructure` ; (4) scan `.csproj` : `ProjectReference` =
  `Transmission.Contracts` uniquement ; (5) types Wire/Client/TokenProvider propriétaires **non exportés**
  hors assembly.

### CP2 — Auth PISTE — (M)
- `IChorusProTokenProvider` + `ChorusProTokenProvider` (interne) calqués sur `SuperPdpTokenProvider.cs` :
  `POST <oauth>/api/oauth/token` form-urlencoded `…&scope=openid` ; jeton **mis en cache** (record
  immuable) + **renouvellement proactif** (`expires_in` réel − skew). Le provider **ne fait QUE** cache +
  renouvellement.
- **Le retry 401 ×1 vit dans le CLIENT**, pas dans le provider (patron `SuperPdpClient.cs:379-391`
  `SendWithAuthAsync` : sur 401 → `forceRefresh` + retente une fois).
- En-tête **`cpro-account: base64(login:motDePasse)`** ajouté à **chaque** requête métier (constant par
  compte, fourni par le resolver) — distinct du Bearer.
- **Sécurité (C5)** : `base64` n'est **pas** du chiffrement. Garde explicite : **aucun logging** des
  en-têtes `cpro-account` **et** `Authorization` (Chorus Pro = 1er connecteur HTTP réellement en prod) ;
  test d'assertion « aucun credential dans les logs » + vérifier que `RawResponse` ne contient pas de credential.
- **Tests** : `StubTokenProvider` (patron SuperPdp) ; renouvellement, retry 401 (côté client), présence
  simultanée Bearer + `cpro-account`.

### CP3 — `SendDocumentAsync` (dépôt du Factur-X scellé) — (M/L)
- **Gardes typées AVANT tout HTTP** (patron `Generique`) : artefact Factur-X (`PaSendContext`/FX07)
  présent et non vide, sinon `Bloqué` (code `*_ARTEFACT_REQUIS`, `TechnicalError`, message FR) — **ne
  régénère jamais** l'artefact (CLAUDE.md n°6). **Le plug-in ne calcule ni n'arrondit aucun montant**
  (le payload `deposerFluxFacture` ne porte aucun montant : base64 + métadonnées) — transport pur.
- Résoudre **`idUtilisateurCourant`** via **`consulterCompteUtilisateur(email du compte technique)`**,
  **mis en cache par compte** (C3) — **2ᵉ appel HTTP** (pèse sur le sizing CP3). Cardinalité **non
  confirmée** : vérifier au Swagger PISTE si requis pour la version ciblée ; **si requis** l'inclure au
  payload, **sinon** l'omettre.
- Construire le payload `deposerFluxFacture` : `fichierFlux` = **base64 du PDF/A-3** scellé, `nomFichier`,
  `syntaxeFlux = IN_DP_E2_CII_FACTURX`, **`avecSignature = false`** (constante — notre Factur-X est PDF/A-3
  **non signé**, D9). JSON Chorus Pro selon F18.
- Classer la réponse via `ChorusProResponseMapper` : **`codeRetour` OK → `PaSendResult` état `Sending`**
  (`PaDocumentId = numeroFluxDepot`, **A1/D5 — jamais `Issued` au dépôt**) ; 4xx métier → `Rejected`
  (erreurs PA **intactes** `PaError[]`) ; 5xx/401/403/**timeout** → `Technical` (re-tentable).
  **`RawResponse` conservée** (sans credential, C5).
- **Idempotence (A3/D8)** : **pas** de raccrochage par `numeroFluxDepot` (inconnu sur timeout). Timeout
  → `TechnicalError` **sans re-POST automatique**. Test obligatoire : « timeout sans `numeroFluxDepot` ne
  re-POST jamais ».

### CP4 — `GetDocumentStatusAsync` (compte rendu d'intégration) — (S)
- `consulterCR` (param `numeroFluxDepot`) → mapper sur les **libellés `etatCourantFlux` EXACTS** (C1, via
  F18) : `Intégré` → **`Issued`** (le seul chemin vers `Issued`, A1) ; `Rejeté` → `RejectedByPa` (erreurs
  intactes) ; **`Intégré partiellement` → `RejectedByPa`** (C2 — prudent : intégration incomplète ≠ émis,
  `Errors`+`RawResponse` intacts) ; **`Incidenté` → `RejectedByPa`** (la doc `monitoring-flows` le définit
  « flux NON traité, à rejouer entièrement » → **reprise opérateur, jamais de re-dépôt automatique**
  (cohérent D8) ; surtout **pas** `Technical` re-tentable qui re-déposerait à l'aveugle — à confirmer en
  F18) ; états d'attente/en-cours → `Sending` ; **valeur inconnue → fail-safe, JAMAIS `Issued`**.
  Transitoire = retry backoff (lecture idempotente). Renvoie `PaDocumentStatus { PaDocumentId, State, Errors, RawResponse }`.
- Test : « statut `consulterCR` inconnu → jamais `Issued` » ; « les 9 `etatCourantFlux` mappés, seul `Intégré` → `Issued` ».

### CP5 — Schéma 3ᵉ secret + AuthMode générique — (M) — *dépend PR-INT (Partie A) + D1 + D6*
- **Migration** `Vnnn__add_pa_technical_account_secret.sql` (numéro **après** la migration OAuth de Partie
  A — **coordonner** car TenantSettings=V010 et la V011 OAuth n'est pas encore dans le repo, B4) :
  `ADD COLUMN IF NOT EXISTS encrypted_technical_password text;` (nullable, idempotent patron V008). Login
  technique → `accountIdentifiers` (non secret).
- `PaAuthMode.OAuth2WithTechnicalAccount` est **déjà ajoutée en CP1** ; ici on la **câble** au form/store.
- Étendre `IPaAccountSecretStore` (tuple +`encryptedTechnicalPassword`/`technicalLogin` +`technicalEmail`
  pour résoudre `idUtilisateurCourant`, C3), `PaAccount` (+champs+setters), `Add/UpdatePaAccountCommand` +
  handlers (**Protect** le mot de passe), UoW, `PaAccountDto` (+`HasTechnicalPassword`, jamais le secret,
  jamais loggé — INV-TENANTSETTINGS-003).
- Form `ComptesPaView` : champs conditionnels rendus **par AuthMode** (jamais par nom de plug-in) —
  pour `OAuth2WithTechnicalAccount` : client_id/secret + login + mot de passe technique (`type=password`).
  **bUnit** (règle 19 = P1).

### CP6 — Resolver Host + câblage via bootstrap dédié — (S) — *dépend CP5*
- `src/Host/Liakont.Host/PaDelivery/ChorusProAccountResolver.cs` (interne, **modèle
  `GeneriqueAccountResolver` — seul resolver Host existant**, B1) : lit via `IPaAccountSecretStore`,
  **déchiffre** (`ISecretProtector.Unprotect`), mappe l'environnement, construit `ChorusProAccountConfig`.
  **Bloque si secret absent** (jamais d'envoi sans auth).
- **Câblage (B2)** : créer un **`ChorusProPaDeliveryBootstrap.AddChorusProPaDelivery`** sur le modèle de
  `GeneriquePaDeliveryBootstrap.AddGeneriquePaDelivery` (`TryAddSingleton<IChorusProAccountResolver, …>` +
  `AddChorusProPaClient()`), **appelé depuis le bloc de composition d'`AppBootstrap.cs:332`** (à côté de
  `AddGeneriquePaDelivery`) — **pas** dans `PaClientBootstrap.AddConfiguredPaClients` qui ne câble que
  `Fake`. (NB : la docstring « seul endroit autorisé à référencer un plug-in PA concret » figure dans
  **chaque** bootstrap dédié — `PaClientBootstrap` ET `GeneriquePaDeliveryBootstrap` — pas seulement dans AppBootstrap.)
  → Chorus Pro devient résolvable par `registry.Resolve`.

### CP7 — Capacités — (S)
`ChorusProCapabilities.Value` (record `PaCapabilities`) — déclarer **explicitement**, rien d'inventé :

| Capacité | Valeur | Justification |
|---|---|---|
| `PaName` | `"Chorus Pro"` | message opérateur |
| `SupportsFacturXTransmission` | **true** | transport d'un Factur-X scellé (`IN_DP_E2_CII_FACTURX`). ⚠️ déclenche la génération Factur-X amont et **fait SAUTER le diagnostic `tax_report_setting`** au SEND (`SendTenantJob.cs:130,734`) — voulu pour une PA B2G de transport, **à confirmer explicitement** |
| `SupportsCreditNotes` | **false en démo** | bascule à `true` **seulement sur confirmation explicite** (sandbox + Spec) — **jamais déduite d'un test vert** (patron `SuperPdpCapabilities.cs:31`) |
| `SupportsB2cReporting` / `SupportsDomesticPaymentReporting` / `SupportsInternationalPaymentReporting` | **false** | e-reporting hors Chorus Pro (D2) → résultat typé |
| `SupportsB2bInvoicing` | **false** | Chorus Pro = B2G ; flux B2B via PA/PDP |
| `SupportsTaxReportRetrieval` / `SupportsDocumentRetrieval` | **false** | non couvert tranche démo |
| `SupportsReportRectification` / `SupportsSelfBilling` | **false** | non couvert |
| `MaxDocumentsPerRequest` | **null** | dépôt unitaire (dépôt par lot = fast-follow) |

### CP8 — Tests — (S/M) — *dépend CP3+CP4 ; sandbox dépend des prérequis externes*
- **Unitaires** (D3) : `RoutedHttpMessageHandler` scriptant token PISTE + `deposerFluxFacture` +
  `consulterCR` (patron `SuperPdpTestData`). Couvrir : **dépôt accepté → `Sending` (jamais `Issued`)** ;
  `consulterCR=Intégré` → `Issued` ; `Rejeté`/`Intégré partiellement` → `RejectedByPa` ; **statut inconnu
  → jamais `Issued`** ; erreur silencieuse (200 + errors) ; 5xx/timeout → `Technical` ; **« timeout sans
  numeroFluxDepot ne re-POST jamais »** ; **« échec de résolution `idUtilisateurCourant` → `TechnicalError`,
  aucun dépôt »** + « cache `idUtilisateurCourant` réutilisé » ; RawResponse conservée **sans credential** ;
  capacité absente → résultat typé + message FR ; renouvellement/retry OAuth ; présence `cpro-account` ;
  **aucun credential loggé** (C5).
- **Boundary** : `ChorusProBoundaryTests` (CP1, les 5 gardes).
- **Sandbox réel** : `ChorusProSandboxTests` `[Trait("Category","Sandbox")]` (patron `SuperPdpSandboxTests`),
  secrets via env, **EXIGE un parcours dépôt→`Sending`→`consulterCR=Intégré`→`Issued` réel en qualif**
  (leçon faux-vert 2026-06-12), **exclu de CI**.

### CP9 — Seed/config + doc — (S)
- Exemple **FICTIF** dans `config/exemples/tenant-seed/pa-accounts.json` (pluginType `"ChorusPro"`,
  environment `"Staging"`, accountIdentifiers fictifs, **aucun secret** — saisi via console).
- Note d'exploitation : procédure de raccordement qualif (PISTE app SANDBOX + compte technique) dans
  `deployments/README.md` ou doc dédiée. Aucune donnée client dans le code.

---

## 5. Graphe de dépendances & chemin critique

```
PR-INT (Partie A, ⛔) ─┐  (mergé sur main AVANT feat/pa-chorus-pro, B5)
                       ├─> CP5 ──> CP6 ─┐
CP0 ─> CP1 ─> CP2 ─> CP3 ─> CP4 ────────┴─> CP8(unit) ─> CP8(sandbox plug-in*)
                        CP7 ─────────────┘
                        CP9 (transverse)
                                            PR-PIPE (async, ⛔) ──> [envoi RÉEL prod / démo e2e]
   (* sandbox plug-in dépend des prérequis externes §3 ; n'a PAS besoin de PR-PIPE)
```
**Chemin critique démo :** CP0 → CP1 → CP2 → CP3 → CP4 → CP8(unit) → CP8(sandbox plug-in). **Partie A**
(mergée d'abord, B5) débloque CP5 → CP6 (câblage Host). **PR-PIPE** (D7) débloque l'**envoi RÉEL en prod /
la démo e2e**, indépendamment des autres lots. CP7/CP9 finalisent.

---

## 6. Definition of Done (par lot + global)

- `verify-fast.ps1` vert sur **les 2 solutions** (plateforme .NET 10 + agent net48). **CP1 compile**
  (enum `OAuth2WithTechnicalAccount` définie au même lot qu'elle est consommée).
- Boundary NetArchTest vert (**5 gardes** ; plug-in → `Transmission.Contracts` uniquement, `IPaClient` interne).
- Aucun `if (pa is ChorusPro)` ni flag produit doublonnant une capacité (tout par `PaCapabilities`/`AuthMode`).
- Capacité absente → résultat typé, **jamais** d'exception ; validations Blocking jamais affaiblies.
- **Dépôt accepté → `Sending`, jamais `Issued`** ; `Issued` uniquement sur `consulterCR=Intégré` (A1) ;
  **les 9 `etatCourantFlux` mappés** (`Incidenté` → reprise opérateur, jamais re-dépôt) ; statut inconnu →
  fail-safe (C1).
- **Timeout → `TechnicalError` sans re-POST** (A3/D8) ; pas de double dépôt.
- **Envoi RÉEL en prod / démo e2e conditionnés à PR-PIPE** (support `Sending` async sans re-dépôt, D7) ;
  sinon démo **sandbox/mock plug-in** seulement.
- `avecSignature = false` (D9) ; `idUtilisateurCourant` résolu via `consulterCompteUtilisateur` + caché (C3).
- Aucun montant calculé/arrondi dans le plug-in (transport pur, C6) ; règle `decimal` globale conservée.
- **Aucun credential loggé** (`cpro-account`/`Authorization`, C5) ; secrets chiffrés au repos.
- Endpoints `*.piste.gouv.fr` (pas `aife.economie.gouv.fr`), chemins depuis Swagger/F18 (A2).
- Sources API/fiscales tracées dans **`docs/conception/F18-Plugin-ChorusPro.md`** (C4, CLAUDE.md n°2).
- Migration ajoutant une colonne config (pas d'audit/WORM touché) ; idempotente ; n° coordonné avec Partie A (B4).
- Pages console modifiées → **bUnit** (règle 19).
- **Sandbox prouve un `Issued` réel en qualif** (pas de faux-vert).
- Boucle `codex-review.ps1` propre sur l'arbre de travail courant ; merge humain (Karl).

---

## 7. Hors périmètre (fast-follow / à arbitrer)
- e-reporting (relève des plugins PA/PDP, pas de Chorus Pro — D2) ; routage e-reporting CMP = arbitrage fiscal.
- Dépôt **par lot** (`submit a batch`) pour gros volumes.
- Récupération du document généré / tax reports (capacités `false` en démo).
- Profil/version Factur-X exact exigé par Chorus Pro (verrouillage prod — D4).

> Le traitement `Sending` first-class dans le pipeline n'est PLUS « hors périmètre » : c'est désormais le
> prérequis **PR-PIPE** (§4, décision D7), à seeder comme item d'orchestration.

## 8. Sources (officielles — endpoints à re-vérifier au catalogue/Swagger PISTE courant)
- Décommissionnement URLs PISTE historiques (30/09/2023) : `piste.gouv.fr/decommissionnement-des-url-piste-historiques`
- Spécifications API Chorus Pro V5.00 (2020, hôtes périmés) : `communaute.chorus-pro.gouv.fr/wp-content/uploads/2020/04/External_Specifications_API_Appendix_V5.00.pdf`
- Raccordement OAuth2 PISTE : `communaute.chorus-pro.gouv.fr/chorus-pro-piste-comment-reussir-son-raccordement-api-oauth2/`
- Dépôt de facture / suivi des flux : `communaute.chorus-pro.gouv.fr/submit-flow-invoice/` · `.../monitoring-flows/`
- e-reporting B2B privé via PA : `impots.gouv.fr/facturation-electronique-et-plateformes-agreees`
- Chorus Pro = PPF/secteur public : `impots.gouv.fr/actualite/chorus-pro-restera-la-plateforme-de-reference-pour-la-facturation-electronique-du-secteur`
- Calendrier réforme : décret 2024-266 + art. 123 LF 2026 (Légifrance)
