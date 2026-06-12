# F14 — Plug-in PA Super PDP (`IPaClient`)
### Document de conception — `src/PaClients/Liakont.PaClients.SuperPdp`

> Statut : 🟩 **plug-in livré (PAS02) + tests de contrat & sandbox livrés (PAS03)** — étude de conception
> initiale (PAS01) rédigée AVANT ouverture de la sandbox. Tout ce qui n'est pas vérifiable sur source
> publique ou sandbox reste marqué **« à confirmer sandbox/support »** et n'est JAMAIS affirmé (CLAUDE.md
> n°2 : aucune règle inventée).
> Dernière mise à jour : 2026-06-11 (PAS03).
>
> **État PAS03 (2026-06-11)** — ce qui est CONFIRMÉ vs ce qui reste OUVERT :
> - ✅ **Connexion sandbox OAuth confirmée par test réel** (lot `orchestration/items/PAS.yaml`, en-tête) :
>   `POST https://api.superpdp.tech/oauth2/token` (`grant_type=client_credentials`) → `200`, `token_type
>   bearer`, `expires_in 1799`. Lève **O1** pour le token-endpoint + la base URL sandbox (les scopes et la
>   base URL **prod** restent à confirmer). Identifiants en variables d'env (jamais versionnés).
> - ✅ **Suite de contrat héritée verte** + **suite sandbox réelle livrée** (§8), exclue de la CI.
> - 🟠 **AUCUNE réponse support obtenue** sur les points fiscaux/fonctionnels **O3, O4, O5, O7, O8, O9**
>   (flux paiement 10.2/10.4, endpoint de téléchargement, archivage NF Z42-013, modèle d'avoir, montage
>   marque grise, rectification). Les questions §10 restent **à envoyer/obtenir** (action humaine, comme
>   l'ouverture de sandbox). **Tant qu'une réponse n'est pas obtenue, la capacité correspondante reste
>   `false`** (§5) — aucune valeur n'est devinée (CLAUDE.md n°2). La mise à jour de F14 « avec les réponses
>   support » se fera quand ces réponses arriveront ; elle n'est pas un prérequis de la livraison des tests.
>
> **⚠️ Numérotation** : l'item PAS01 du backlog demande le fichier `F13-Plugin-SuperPdp.md`, mais
> le numéro **F13 est déjà pris** par [F13 — Installateur agent + profils intégrateur](F13-Installateur-Agent-Profils-Integrateur.md)
> (ajouté le 2026-06-03, manifest v8). Ce document prend donc le **prochain numéro libre, F14**.
> Les items PAS02/PAS03 qui référencent « F13 (PAS01) » désignent CE document. L'index de conception
> ([README-Index-Conception.md](README-Index-Conception.md)) pointe vers F14.
>
> **Sources** (rien n'est inventé) : `docs/market/DR17-Strategie-Multi-PA-Partenaires.md`
> (intégrabilité API Super PDP **vérifiée le 2026-06-02**), `docs/market/DR9-Business-Model-Pricing-Scenarios-CA.md`
> §3 (Offre Éco / TCO), `docs/conception/F05-Client-API-B2Brouter.md` (PA de référence, même contrat),
> `docs/architecture/ajouter-un-plugin-pa.md` (checklist d'ajout d'un plug-in PA), le contrat réel
> `Liakont.Modules.Transmission.Contracts.IPaClient` (livré par PAA01), et la documentation publique
> Super PDP (superpdp.tech — page de doc rendue en JavaScript, **non récupérable** statiquement au
> 2026-06-05, déjà constaté en DR17 ; seuls les faits corroborés par plusieurs sources publiques sont
> retenus comme « vérifiés »).

---

## 1. Objectif

Encapsuler **toutes** les interactions avec l'API Super PDP dans une assembly séparée
`Liakont.PaClients.SuperPdp` implémentant `IPaClient` (classe `SuperPdpClient`), exactement comme
le plug-in B2Brouter (F05) le fait pour eDocExchange. Aucun autre composant de la plateforme ne
connaît les URL, le format JSON ou les particularités de Super PDP : le produit s'adapte aux
**capacités déclarées** du plug-in (`PaCapabilities`), jamais à un `if (pa is SuperPdp)` ni à un
flag produit (blueprint.md §2 ; CLAUDE.md n°6/8/16).

**Enjeu commercial (DR9 §3, DR17)** : Super PDP est la PA de l'**« Offre Éco — conformité complète,
PA incluse, < 100 €/mois tout compris »**, montée en **marque grise grossiste** (l'intégrateur règle
Super PDP directement à ~0,01 €/facture et facture librement ses clients). Le plug-in est donc le
socle technique de cette offre. Il n'introduit **aucune** logique commerciale (tarification,
facturation client) : ça reste du paramétrage et de la console (hors plug-in).

Ce document est l'**étude PAS01** : il prépare l'implémentation (PAS02) et les tests de contrat
(PAS03). Il ne livre **pas de code** — il livre la conception, le mapping endpoint↔contrat, les
capacités provisoires et la liste de questions support.

---

## 2. Faits Super PDP (vérifiés DR17, 2026-06-02) vs à confirmer

| Fait | Détail | Statut | Source |
|---|---|---|---|
| Statut PA | PA immatriculée définitivement, ISO 27001 (LNE), Peppol AP + SMP, infra souveraine FR (réplication x4, 2 datacenters, stack Go/PostgreSQL propriétaire) | ✅ vérifié | DR17 §1 |
| Style d'API | REST/JSON + **OpenAPI** publiée, onboarding développeur **self-service**, **sandbox** disponible | ✅ vérifié | DR17 §1 ; superpdp.tech/openapi |
| **Authentification** | **OAuth 2.0 — client credentials** (quick start), ≠ clé statique B2Brouter | ✅ vérifié | DR17 §1 ; superpdp.tech/documentation |
| Cycle de vie | statuts, logs, notifications ; **webhooks/callbacks** de statuts | ✅ vérifié | DR17 §1 |
| E-reporting B2C (Flux 10.3) | supporté ; compté **« 1 facture par journée d'encaissements »** quel que soit le nb de transactions (≈ 0,01 € / journée de vente) | ✅ vérifié (comptage) / 🟠 schéma exact à confirmer | DR17 §1 ; DR9 §3 |
| Interopérabilité | **AFNOR XP Z12-013**, connexion annuaire **PPF**, **Peppol** | ✅ vérifié | DR17 §1 |
| Archivage | « Conservation des factures pendant 10 ans avec garantie d'intégrité » (a priori inclus) | 🟠 inclus à confirmer ; **NF Z42-013 / horodatage / sort en cas de résiliation = à clarifier (DR17-A4)** | DR17 §1 |
| KYC / annuaire | KYC, annuaire mono/multi-PA | ✅ vérifié | DR17 §1 |
| Pricing API | 0,01 €/facture (0–10 k/mois), 0,005 € (10–100 k), 0,0025 € (> 100 k) ; gratuité < 1 000/mois = **compte web manuel seulement** (l'API est payante) | ✅ vérifié | DR17 §1 ; DR9 §3 |
| Offre intégrateurs | **Marque grise grossiste** (l'intégrateur règle Super PDP, facture ses clients ; marque Super PDP visible à l'inscription du client) ; marque blanche sur demande | ✅ vérifié | DR17 §1 |
| **Flux paiement 10.2 / 10.4** | Couverture **NON documentée publiquement** (même point ouvert que B2Brouter, F09) | ❌ **à confirmer sandbox/support** | DR17 §1, §3 (A4) |
| **Téléchargement de la facture générée** (Factur-X/UBL/CII) | Endpoint de récupération du document généré par la PA | ❌ **à confirmer sandbox/support** | déduit du contrat `GetGeneratedDocumentAsync` |
| Base URL API | `https://api.superpdp.tech` (lue dans `SUPER_PDP_API_ENDPOINT` côté exemple) | 🟠 **confirmé public** (forum + exemple `pimeo/superpdp-nodejs-oauth-example`) ; **valeur recette à confirmer** dans le quick-start | forum Ubuntu-fr ; repo pimeo |
| Token-endpoint OAuth | `<base>/oauth2/token` → `POST grant_type=client_credentials` + `client_id`/`client_secret` → Bearer (aussi `/oauth2/authorize`, `/oauth2/revoke` pour le flux `authorization_code`, NON utilisé ici) | 🟠 **confirmé public** (forum + code pimeo) | forum ; repo pimeo |
| Préfixe de version + endpoints exacts (émission, statut, tax reports, settings) | Préfixe **`/v1.beta/`** ; émission = `POST /v1.beta/invoices` (XML CII/UBL ou PDF Factur-X, JAMAIS de JSON), conversion = `POST /v1.beta/invoices/convert`, statut = `GET /v1.beta/invoices/{id}` (§3.2/§3.4) | ✅ **confirmés** (OpenAPI officielle `api.superpdp.tech/openapi/superpdp.json` v1.24.0.beta + envois sandbox réels 2026-06-12) ; tax reports/settings restent 🟠 | OpenAPI officielle ; sandbox |
| Rectification (Flux RE) | Capacité de rectification d'une déclaration | ❌ **à confirmer sandbox/support** | déduit du contrat `SupportsReportRectification` |

> **Règle de lecture** : une cellule ✅ = corroborée par au moins une source publique (DR17/DR9 ou
> page Super PDP). 🟠 = partiellement connu, à préciser. ❌ = **inconnu, ne JAMAIS coder en dur** ;
> figé seulement après la sandbox (PAS02/PAS03) ou une réponse support.

---

## 3. Synthèse de l'API Super PDP (conception)

### 3.1 Authentification — OAuth 2.0 client credentials

Différence structurante avec B2Brouter (header `X-B2B-API-Key` statique, F05 §2) : Super PDP utilise
un flux **OAuth 2.0 client credentials**. Conséquences pour le plug-in :

- Le `SuperPdpClient` obtient un **jeton d'accès** (`POST <token-endpoint>` avec `client_id` /
  `client_secret`, `grant_type=client_credentials`) puis l'injecte en `Authorization: Bearer <token>`.
- Le jeton a une durée de vie : le plug-in **met en cache** le jeton et le **renouvelle** avant
  expiration (et sur `401`). Cette mécanique vit **dans le plug-in** — aucun type OAuth ne traverse
  `IPaClient` (frontière, §7).
- `client_id` / `client_secret` sont des **secrets** : chiffrés par tenant, résolus en interne par le
  plug-in via le coffre Identity/TenantSettings — **jamais en clair, jamais en log** (CLAUDE.md n°10).
  Le `PaAccountDescriptor` ne transporte que des identifiants non sensibles (account id, environnement,
  URL de base) ; les chemins **exacts** (token-endpoint, base URL) sont **à confirmer sandbox**.

### 3.2 Émission de document — contrat CONFIRMÉ (sandbox + OpenAPI officielle, 2026-06-12)

✅ **Confirmé contre l'OpenAPI officielle** (`https://api.superpdp.tech/openapi/superpdp.json`,
v1.24.0.beta) **et par des envois sandbox réels** (factures `72187`, `72208` créées le 2026-06-12).

- `POST /v1.beta/invoices` n'accepte **AUCUN JSON propriétaire** : content types admis =
  `application/xml` (facture **CII ou UBL**), `application/pdf` (**Factur-X**), `multipart/form-data`
  (un seul fichier XML/PDF). La PA transporte des factures **déjà formées** (EN 16931), elle ne les
  construit pas (≠ B2Brouter). Le premier payload JSON imaginé par la conception initiale répondait
  `400 {"message":"unknown format"}` — c'est ce défaut qui a invalidé le premier passage de gate.
- **Chemin retenu pour le plug-in** (validé bout en bout en sandbox — facture `72272` émise PAR le
  plug-in le 2026-06-12, cycle `api:uploaded` → `api:validated` → `api:sent`) :
  1. pivot → JSON **`en16931`** (schéma `en_invoice` de l'OpenAPI : `number`, `issue_date`,
     `type_code` 380 [UNTDID 1001, **NOMBRE JSON** — une chaîne est rejetée « cannot unmarshal JSON
     string into Go model.InvoiceTypeCode »], `currency_code`, `process_control.specification_identifier`
     `urn:cen.eu:en16931:2017` [BT-24], `seller`, `buyer`, `totals` [acompte BT-113 émis, BT-115 par
     l'identité BR-CO-16], `vat_break_down`, `lines`) ;
  2. `POST /v1.beta/invoices/convert?from=en16931&to=cii` (`application/json`) → XML CII — le
     converter applique les **règles de validation EN 16931 officielles** (`BR-*`, ex. BR-S-02 :
     catégorie S ⇒ n° TVA vendeur obligatoire) et répond `400 {"message":"[BR-…]…"}` sinon ;
  3. `POST /v1.beta/invoices?external_id=<clé>` (`application/xml`) → `200 {id, company_id,
     created_at, events:[{status_code:"api:uploaded",…}], direction:"out", external_id}`.
     L'envoi est **asynchrone** (file d'attente) : un `200` signifie « téléversée », JAMAIS « émise ».
- **Contrôles serveur constatés à l'envoi** (messages d'erreur en français) :
  - le **vendeur de la facture doit être l'entreprise liée à la session OAuth** (« L'entreprise (X)
    liée à cette session ne correspond pas au vendeur de la facture (Y) ») ;
  - le **buyer doit porter une adresse électronique d'annuaire** (« missing buyer electronic
    address ») : adressage par SIREN scheme `0002` validé en sandbox (`legal_registration_identifier`
    + `electronic_address` = SIREN/0002). **Conséquence de périmètre : `POST /invoices` ne couvre PAS
    le B2C anonyme** — un pivot sans `Customer` identifié (SIREN) dégrade en **rejet local typé AVANT
    tout appel** (message opérateur français) ; le B2C anonyme relève de l'e-reporting (§3.3, hors V1).
- Mode « créé sans envoi » : **non exposé par Super PDP** (le POST crée ET met en file). Conformément
  au principe déjà acté ici, `sendAfterImport=false` retourne un `PaSendResult` typé refusant
  l'opération (aucun appelant produit ne l'utilise ; le défaut du contrat est `true`).
- **Idempotence** : `external_id` (query, ≤ 36 caractères, renvoyé par la ressource `invoice`) porte
  le numéro de document (BT-1, clé d'idempotence du pivot) ; la relecture anti-doublon liste
  `GET /v1.beta/invoices` et matche `external_id` (la liste n'a PAS de filtre par numéro).
- **Pré-validation** : `POST /v1.beta/validation_reports` existe pour valider sans envoyer (évite la
  plupart des `api:invalid`) — amélioration future, non requise V1.
- **Limitation V1 — factures à échéance non soldées (BR-CO-25, constatée sandbox 2026-06-12)** : quand
  le montant dû (BT-115) est **positif**, EN 16931 exige la date d'échéance (BT-9) ou les conditions de
  paiement (BT-20) — que le PIVOT ne porte pas encore. Le converter rejette alors avec le message
  `[BR-CO-25]…`, conservé intact pour l'opérateur (on ne FABRIQUE jamais une échéance — CLAUDE.md n°2).
  Les factures SOLDÉES (acompte BT-113 = TTC, le cas dominant du paiement comptant aux enchères)
  passent. Levée : étendre `PivotDocumentDto` avec `paymentDueDate` (BT-9, additif) + `CanonicalJson` +
  adaptateurs — item d'orchestration dédié (§12 O11).
- **Avoirs** : inchangé — DR17 liste « avoirs » seulement **« au panel des capacités à vérifier »**
  → capacité `SupportsCreditNotes` **`false`** (§5), passée à `true` uniquement une fois le modèle
  d'avoir (lien avoir→facture amendée, `type_code` 381 + `preceding_invoice_references`) exercé en
  sandbox.

### 3.3 E-reporting B2C (Flux 10.3)

- Vérifié : Super PDP fait l'e-reporting B2C, **compté à la journée d'encaissements**. Le schéma
  d'agrégation (ledger quotidien, format, endpoint) est **à confirmer sandbox**.
- Flux paiement **10.2 (international)** et **10.4 (domestique)** : **non documentés** → capacités
  provisoirement **false** (§5), question support prioritaire (§10).

### 3.4 Statuts — modèle d'ÉVÉNEMENTS confirmé (polling V1)

✅ **Confirmé OpenAPI + sandbox (2026-06-12)** : la ressource `invoice` porte un tableau `events[]`
(`{id, invoice_id, status_code, status_text, created_at, details[]}`). L'OpenAPI précise : **« There
is no formal state machine »** — la PRÉSENCE d'un événement signale qu'il a eu lieu, ce n'est pas un
état exclusif. Trois familles de `status_code` (énumération fermée, documentée dans l'OpenAPI) :

- **`api:*`** (internes Super PDP) : `api:uploaded` (entrée universelle, contrôles syntaxiques
  passés), `api:validated` (Schematron), `api:sent`, `api:invalid` (erreur AVANT transmission),
  `api:rejected` (rejet asynchrone par l'autre point d'accès), `api:received` / `api:acknowledged` /
  `api:accepted` (réception/AR optionnels).
- **`fr:*`** (statuts officiels du cycle de vie français) : `fr:200` Déposée … `fr:213` Rejetée,
  `fr:501` Inadmissible (liste complète dans l'OpenAPI ; cycle aval observé en sandbox en ~2 s :
  `api:uploaded` → `fr:200` → `fr:201` Émise par la plateforme → `fr:202` Reçue).
- **`ppf:*`** : accusés des dépôts de fichiers au PPF (suivi de flux, sans impact `PaSendState`).

- **Polling** ✅ : `GET /v1.beta/invoices/{id}` (id **numérique** attribué par la PA) → la ressource
  avec ses `events[]`. C'est l'implémentation V1 de `GetDocumentStatusAsync`.
- **Webhooks** : toujours envisageables plus tard (pattern obligatoire inchangé : endpoint de
  réception dans le **projet Web du plug-in**, jamais dans `Transmission`) — **non requis V1**, le
  polling confirmé suffit au pipeline existant.

### 3.5 Tax reports / settings

- `ListTaxReportsAsync` / `GetTaxReportAsync` : récupération des déclarations agrégées (équivalent des
  `tax_reports` B2Brouter, F05 §2). Endpoints **à confirmer sandbox**.
- `GetTaxReportSettingAsync` / `EnsureTaxReportSettingAsync` : réglage de déclaration (idempotent).
  Les champs requis (`NafCode`, `StartDate`, `TypeOperation`, `EnterpriseSize`, `CinScheme`) viennent
  du **paramétrage du tenant (CFG02)**, jamais du code — **aucune valeur fiscale inventée** (CLAUDE.md
  n°2/7). La forme exacte côté Super PDP est **à confirmer sandbox**.

### 3.6 Récupération de la facture générée (archivage TRK05)

- `GetGeneratedDocumentAsync(paDocumentId)` → Factur-X PDF/A-3 / UBL / CII pour le coffre d'archive.
  L'existence et le format de l'endpoint Super PDP sont **à confirmer sandbox/support** (§10) →
  capacité `SupportsDocumentRetrieval` provisoirement **false** tant que non vérifié.

---

## 4. Mapping `IPaClient` ↔ API Super PDP

Le contrat (livré par PAA01) que `SuperPdpClient` doit implémenter, méthode par méthode. La colonne
« API Super PDP » est une **cible de conception** : tout ce qui n'est pas ✅ est **à confirmer sandbox**.

| Membre `IPaClient` | Rôle | API Super PDP (cible) | Statut |
|---|---|---|---|
| `Capabilities` | Capacités déclarées (source de vérité du comportement) | objet statique du plug-in (§5) | ✅ défini ici (provisoire) |
| `SendDocumentAsync(PivotDocumentDto, sendAfterImport, ct)` → `PaSendResult` | Émission de facture (destinataire identifié) | `POST /v1.beta/invoices/convert?from=en16931&to=cii` puis `POST /v1.beta/invoices?external_id=` (§3.2) | ✅ confirmé sandbox 2026-06-12 |
| `SendPaymentReportAsync(PaymentReportPeriod, ct)` → `PaSendResult` | E-reporting paiement (flux 10.2/10.4) | flux paiement non documenté | ❌ → renvoie `CapabilityNotSupported` tant que capacité false |
| `GetDocumentStatusAsync(paDocumentId, ct)` → `PaDocumentStatus` | Relecture d'état (polling) | `GET /v1.beta/invoices/{id}` → `events[]` (§3.4) | ✅ confirmé sandbox 2026-06-12 |
| `ListTaxReportsAsync(since, ct)` → `IReadOnlyList<PaTaxReport>` | Liste des déclarations | `GET <base>/tax_reports` | 🟠 endpoint à confirmer |
| `GetTaxReportAsync(taxReportId, ct)` → `PaTaxReport` | Détail déclaration (+ XML si dispo) | `GET <base>/tax_reports/{id}` | 🟠 endpoint à confirmer |
| `GetAccountInfoAsync(ct)` → `PaAccountInfo` | Consommation / limites | `GET <base>/account` | 🟠 endpoint à confirmer |
| `GetTaxReportSettingAsync(ct)` → `PaTaxReportSetting` | Lecture réglage déclaration | `GET <base>/tax_report_settings` | 🟠 endpoint à confirmer |
| `EnsureTaxReportSettingAsync(PaTaxReportSettingRequest, ct)` | Réglage idempotent (GET puis POST/PATCH si écart) | `POST`/`PATCH <base>/tax_report_settings` | 🟠 endpoint à confirmer |
| `GetGeneratedDocumentAsync(paDocumentId, ct)` → `PaGeneratedDocument` | Facture générée (Factur-X) pour archivage | endpoint de téléchargement | ❌ existence à confirmer (§10) |

### 4.1 Mapping des familles d'erreur et des événements → `PaSendState`

✅ Conventions réelles confirmées (OpenAPI + sandbox 2026-06-12). Le format d'erreur Super PDP est
`{"http_status_code": <int>, "message": "<texte, souvent en français>"}` (PAS de tableau `errors[]`
comme B2Brouter) ; le message est conservé **intact** dans `PaError`.

| Réponse Super PDP | `PaSendState` |
|---|---|
| HTTP 4xx (`unknown format`, règle `BR-*` du converter, vendeur ≠ compte, buyer non adressable…) | `RejectedByPa` (message **intact**, pas de retry) |
| HTTP 401/403 (auth/config OAuth) | `TechnicalError` re-tentable (jamais un rejet métier figé) |
| Réseau / 5xx / timeout | `TechnicalError` (re-tentable) |
| Capacité non déclarée (avoir, flux paiement) ou pré-condition locale (buyer non identifié, `sendAfterImport=false`) | résultat **typé** (`CapabilityNotSupported` / `RejectedByPa` local), **jamais** d'exception |
| HTTP 200 → classement par les **`events[]`** (le « piège silencieux » réel est l'ASYNCHRONIE : 200 = téléversée, pas émise) | voir ci-dessous |

Classement des `events[]` (présence d'événement, pas machine à états — §3.4) :

| Événements présents | `PaSendState` |
|---|---|
| Un événement d'échec : `api:invalid`, `api:rejected`, `fr:213` (Rejetée), `fr:501` (Inadmissible) | `RejectedByPa` (prioritaire sur tout le reste — prudence fiscale) |
| Sinon, un événement d'émission ou postérieur : `api:sent`, `fr:201` (Émise) … `fr:212` | `Issued` |
| Sinon (`api:uploaded`, `api:validated`, `fr:200` seulement) — et tout code **inconnu** | `Sending` — **jamais** `Issued` par défaut |

> `fr:210` (Refusée par le destinataire) et `fr:207` (En litige) sont POSTÉRIEURS à l'émission : la
> facture EST émise fiscalement → `Issued` pour l'état d'ENVOI ; le suivi du refus métier est un
> processus aval (hors `PaSendState`), entièrement tracé dans `RawResponse`.

- **Idempotence par `external_id`** = numéro de document (BT-1), à DEUX niveaux :
  1. **Anti-doublon SERVEUR** (✅ constaté sandbox 2026-06-12) : Super PDP **refuse de recréer une
     facture au même numéro** — `400 {"message":"La facture est déjà existante (id N)"}` (même avec un
     `external_id` différent). Une double émission fiscale est donc impossible côté PA.
  2. **Raccrochage CLIENT** : sur erreur transitoire à l'émission, ET sur tout rejet 4xx **sans
     identifiant** (possiblement le refus anti-doublon ci-dessus), le plug-in relit
     `GET /v1.beta/invoices?direction=out&order=desc&limit=1000&expand[]=events` (la liste n'a pas de
     filtre numéro ; `expand[]=events` est indispensable — sans lui la liste ne porte pas les événements)
     et matche `external_id` : trouvée → l'état RÉEL est rattaché (jamais un faux « rejeté » sur une
     facture créée) ; absente → NON CONCLUANT (pagination par curseur) : le transitoire reste re-tentable,
     le rejet est rendu avec son message intact (l'opérateur tranche).
- **`RawResponse`** conservée systématiquement (piste d'audit F06/DR6).
- Montants : **`decimal`** uniquement (CLAUDE.md n°1).

---

## 5. Capacités déclarées (`PaCapabilities`) — PROVISOIRES

Valeurs cibles pour `SuperPdpClient.Capabilities`. Elles seront **figées en sandbox (PAS02)** : ce
tableau documente l'état provisoire et son statut. **Aucune capacité n'est déclarée `true` sans
vérification** — une capacité incertaine reste `false` (le produit dégrade en résultat typé, jamais
de faux positif d'envoi).

La colonne **« Valeur provisoire »** est celle que **PAS02 code TANT QUE la sandbox n'a rien confirmé**
— elle applique déjà le principe « incertain = `false` ». La colonne « Cible une fois confirmé »
indique seulement vers quoi la valeur évoluera **après** vérification sandbox/support (elle n'autorise
**pas** à coder `true` avant).

| Flag | Valeur provisoire (codée PAS02) | Cible une fois confirmé | Justification / statut |
|---|---|---|---|
| `PaName` | `"Super PDP"` | — | fixe (messages opérateur FR) |
| `SupportsB2cReporting` | **true** | (reste true) | capacité B2C ✅ **vérifiée** DR17 — seul le schéma de fil reste à mapper en sandbox ; ce n'est PAS une incertitude de capacité (≠ « à confirmer ») |
| `SupportsDomesticPaymentReporting` (10.4) | **false** | true si support confirme | flux paiement non documenté (DR17 §3 A4) — point ouvert O3 |
| `SupportsInternationalPaymentReporting` (10.2) | **false** | true si support confirme | idem (O3) |
| `SupportsB2bInvoicing` | **false** | (reste false en V1) | phase 2 (hors périmètre V1, comme B2Brouter) |
| `SupportsCreditNotes` | **false** | true une fois le modèle d'avoir mappé | avoirs seulement « au panel à vérifier » DR17 → **non confirmé** → `false` par défaut (O7) |
| `SupportsTaxReportRetrieval` | **false** | true une fois les endpoints confirmés | tax reports **attendus mais non confirmés** → `false` par défaut (O2) |
| `SupportsDocumentRetrieval` | **false** | true si l'endpoint existe | existence de l'endpoint de téléchargement non vérifiée (O4, §10) |
| `SupportsReportRectification` (flux RE) | **false** | true si support confirme | non documenté — à vérifier sandbox (PIP04, O9) |
| `MaxDocumentsPerRequest` | **null** | valeur déclarée si limite | pas de limite déclarée connue ; à confirmer |

> Principe (`ajouter-un-plugin-pa.md`) : **« une capacité incertaine = `false` »**. La colonne
> « Valeur provisoire » ci-dessus respecte ce principe : tout ce qui n'est pas ✅ vérifié y vaut
> `false`. Quand Super PDP activera/confirmera un flux, **SEULE cette déclaration changera** — aucun
> autre code produit n'est impacté (CLAUDE.md n°8). C'est le test décisif d'une bonne abstraction PA.

---

## 6. Modèle marque grise / grossiste (implications produit)

DR17 §1 (vérifié) : Super PDP propose la **marque grise avec tarification grossiste** — l'intégrateur
(Liakont / le revendeur) règle Super PDP directement et facture librement ses clients ; la marque
Super PDP **apparaît à l'inscription du client**. Implications pour le plug-in et le paramétrage :

1. **Compte PA de tenant (CFG02)** : chaque tenant a un compte Super PDP. Selon le montage retenu,
   soit (a) un compte Super PDP **par client final**, soit (b) un compte intégrateur **mutualisé**
   avec sous-comptes. **Le montage exact (création de comptes via l'API ? KYC self-service ? un
   compte par SIREN ?) est à confirmer sandbox/support (§10)**. Le `PaAccountDescriptor` portera les
   identifiants non sensibles (account id, environnement) ; les secrets OAuth restent chiffrés.
2. **Création de comptes clients** : si l'onboarding d'un client passe par l'API (KYC, annuaire), ce
   flux est un **service d'administration** (console / OPS), **pas** une responsabilité de `IPaClient`
   (qui ne fait que émettre/lire des documents). À ne **pas** mettre dans le plug-in d'émission.
3. **Facturation grossiste** : la refacturation au client final est de la **logique commerciale**
   (abonnement Passerelle, Offre Éco) — **hors plug-in, hors produit technique**. Le plug-in ne
   connaît ni prix ni facturation : il ne dépend pas de l'offre commerciale (CLAUDE.md n°8).
4. **Marque visible** : l'apparition de la marque Super PDP à l'inscription est un fait à intégrer au
   discours Offre Éco (DR9), sans impact code.

---

## 7. Frontières et conformité (non négociables)

- **Assembly séparée** `src/PaClients/Liakont.PaClients.SuperPdp` — référence **UNIQUEMENT**
  `Liakont.Modules.Transmission.Contracts` (+ Common si besoin). **Jamais** un autre plug-in, **jamais**
  un module métier, **jamais** `Transmission.Infrastructure` (module-rules §6 ; garde NetArchTest, P1).
- **Aucun type Super PDP ni HTTP ne fuit hors de l'assembly** : seuls `IPaClient` et les DTOs pivot/PA
  neutres sont visibles. DTO propriétaires Super PDP + client OAuth = `internal` (NetArchTest, comme
  `FakePaClientBoundaryTests`).
- **HTTP** : `IHttpClientFactory` (.NET 10), client nommé par compte, **TLS 1.2/1.3**.
- **Secrets** (`client_id`/`client_secret`) chiffrés par tenant, résolus en interne, jamais loggés
  (CLAUDE.md n°10). **Montants `decimal`** (CLAUDE.md n°1).
- **Aucune règle fiscale inventée** : tout (NAF, type d'opération, taille d'entreprise, schéma SIREN)
  vient du paramétrage tenant (CFG02) ou de `docs/conception/` (CLAUDE.md n°2).
- **Multi-compte** : un `SuperPdpClient` = un compte (OAuth creds + account id + URL). N tenants = N
  instances (via la `IPaClientFactory`).

### 7.1 Enregistrement (registre de TYPES)

- `SuperPdpClientFactory : IPaClientFactory` avec `PaType = "SuperPdp"` et
  `Create(PaAccountDescriptor)`.
- Extension `AddSuperPdpPaClient(this IServiceCollection)` via
  `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaClientFactory>(…))` — **même patron** que
  `FakePaClientRegistration` (PAA02). Le `IPaClientRegistry` l'indexe par `PaType` : résolution **par
  la clé**, jamais `if (type == "SuperPdp")` (PAA01 §5).

---

## 8. Stratégie de test (préparée pour PAS03)

- **Suite de contrat héritée** ✅ **livrée (PAS03)** : `SuperPdpPaClientContractTests`
  (`src/PaClients/Liakont.PaClients.SuperPdp.Tests.Unit/`) dérive `PaClientContractTests`
  (`tests/Liakont.PaClients.Contract.Tests/`) et pilote `SuperPdpClient` par un **handler HTTP mocké**
  (`RoutedHttpMessageHandler` — les PA réelles n'ont pas de mode mémoire). La suite commune vérifie envoi
  valide, avoir, rejet, erreur silencieuse, timeout, idempotence, conservation `RawResponse`, et — surtout —
  que **toute capacité absente dégrade en résultat typé, jamais une exception** (déterminant pour Super PDP
  qui ne déclare que B2C en V1, §5). Exemple vivant : `FakePaClientContractTests`.
- **Garde de frontière NetArchTest** (cf. `FakePaClientBoundaryTests`) : le plug-in ne référence que
  `Transmission.Contracts` (déjà couverte par `SuperPdpBoundaryTests`, PAS02).
- **Suite sandbox réelle** ✅ **livrée (PAS03), durcie (2026-06-12)** : `SuperPdpSandboxTests`
  (`[Trait("Category","Sandbox")]`), **exclue de `verify-fast`, `run-tests`, `run-e2e` et de la CI**
  (testing-strategy §8.2). Authentification **OAuth 2.0 `client_credentials`** (§3.1) : identifiants locaux
  via les variables d'env **`SUPERPDP_SANDBOX_CLIENT_ID`** / **`SUPERPDP_SANDBOX_CLIENT_SECRET`**, **jamais
  committées** (CLAUDE.md n°10). La suite construit la fixture pivot depuis les **companies sandbox réelles**
  (seller = `GET /v1.beta/companies/me`, buyer extrait d'une facture `generate_test_invoice` — aucun
  identifiant sandbox codé en dur), envoie via le plug-in puis relit le statut. **Elle EXIGE le succès**
  (`PaDocumentId` non vide, état `Sending`/`Issued`, jamais `RejectedByPa`) : leçon du faux passage de gate
  du 2026-06-12 — un test sandbox qui n'exige que « pas d'erreur technique » laisse passer un payload
  rejeté (`unknown format`) et donc une gate invalide.

---

## 9. Écarts notables vs B2Brouter (F05)

| Axe | B2Brouter (F05) | Super PDP (cible) | Impact plug-in |
|---|---|---|---|
| Auth | clé statique `X-B2B-API-Key` | **OAuth 2.0 client credentials** (jeton + refresh) | gestion de jeton interne au plug-in (§3.1) |
| Statuts | polling (on-premise, F05 §8) | **webhooks** + polling de repli | endpoint webhook dans le projet Web du plug-in (§3.4) |
| Versionnement | header `X-B2B-API-Version` | à confirmer (header / chemin versionné ?) | à figer sandbox |
| Tarification | à l'acte + ledgers | **à la journée d'encaissements** (e-reporting) | aucun (hors plug-in) |
| Flux paiement 10.2/10.4 | « planned for a future release » | non documenté | capacités `false` des deux côtés |

> **Confirmation de l'abstraction** : ces écarts sont précisément ce que `IPaClient` doit absorber
> sans qu'AUCUN concept Super-PDP-spécifique (OAuth, webhook, journée d'encaissements) ne fuite dans
> le produit. C'est la grille DR17-A2 (« aucun concept spécifique d'une PA ne fuit ») appliquée à la
> 2ᵉ PA — la preuve que le framework multi-PA tient.

---

## 10. Questions à poser au support / sandbox Super PDP (DR17-A4)

Liste prête à envoyer (à joindre à l'ouverture de la sandbox — **action humaine, prérequis PAS02**) :

1. **Flux de paiement** : Super PDP couvre-t-il le **Flux 10.4** (e-reporting paiement domestique B2C,
   marquage « encaissée ») et le **Flux 10.2** (international) ? Si oui, endpoints + schémas + cadence ?
2. **Archivage** : la conservation 10 ans est-elle **incluse** dans le prix API ? Est-elle certifiée
   **NF Z42-013** ? Y a-t-il **horodatage qualifié** ? Que devient l'archive **en cas de résiliation**
   (réversibilité, export) ?
3. **Téléchargement de la facture générée** : existe-t-il un endpoint pour récupérer le **Factur-X /
   UBL / CII généré** par Super PDP (pour notre archivage TRK05) ? Format(s) et chemin ? → fixe
   `SupportsDocumentRetrieval`.
4. **Erreurs silencieuses** : une réponse **HTTP 200 peut-elle contenir des erreurs métier** (comme le
   piège VATEX de B2Brouter) ? Comment les distingue-t-on d'un succès ?
5. **Création sans envoi** : existe-t-il un équivalent de `send_after_import: false` (créer un brouillon
   non facturable) ?
6. **Avoirs** : modèle exact (champ `is_credit_note`, lien vers la facture amendée, annulation partielle) ?
7. **Comptes & marque grise** : montage grossiste — un compte **par client final** ou **sous-comptes**
   d'un compte intégrateur ? L'**onboarding/KYC** d'un client passe-t-il par l'API ? Au niveau **SIREN**
   (schéma 0002) comme B2Brouter ?
8. **Rectification** (Flux RE) : Super PDP permet-il de rectifier une déclaration émise ? → fixe
   `SupportsReportRectification`.
9. **OAuth** : URL du token-endpoint, durée de vie du jeton, scopes, base URLs **sandbox vs prod** ?
10. **OpenAPI** : récupérer le **fichier OpenAPI** (JSON/YAML) pour générer/valider les DTO internes.

---

## 11. Prérequis humain et statut des items aval

**⚠️ DR17-A4 — action humaine requise** : ouvrir une **sandbox Super PDP** (~1–2 jours) et obtenir
les réponses du §10. **Sans sandbox ouverte** :

- **PAS01 (ce document)** : réalisable sans sandbox (étude/conception ✅) — c'est l'objet de cet item.
- **PAS02 / PAS03** : à marquer **`blocked`** au moment de leur prise, motif **« sandbox Super PDP non
  ouverte — action humaine DR17-A4 requise »** (cf. en-tête de `orchestration/items/PAS.yaml`). Aucune
  implémentation devinée à partir de la seule doc publique (CLAUDE.md n°2).

---

## 12. Récapitulatif des points ouverts (à figer avant code)

| # | Point ouvert | Levée par |
|---|---|---|
| O1 | ✅ token-endpoint OAuth + base URL **sandbox** confirmés par test réel (2026-06-11, PAS03) ; base URL **prod** + scopes restent à confirmer | sandbox (§10 q.9) |
| O2 | ✅ **Émission + statut LEVÉS** (2026-06-12, OpenAPI v1.24.0.beta + envois réels — §3.2/§3.4 : `convert` + `POST /invoices` + `GET /invoices/{id}`) ; tax reports / settings / compte restent à confirmer (`/ereportings`, `/companies/me` existent — mapping à faire) | sandbox / OpenAPI (§10 q.10) |
| O3 | Couverture flux paiement 10.2/10.4 | support (§10 q.1) |
| O4 | Endpoint de téléchargement de la facture générée (→ `SupportsDocumentRetrieval`) | support (§10 q.3) |
| O5 | Archivage : inclus ? NF Z42-013 ? horodatage ? réversibilité ? | support (§10 q.2) |
| O6 | ✅ **LEVÉ** (2026-06-12) : pas de `200 + errors[]` ; le piège réel est l'ASYNCHRONIE (200 = téléversée, l'échec arrive en `events[]` : `api:invalid`/`api:rejected`/`fr:213` — §4.1) | sandbox (§10 q.4) |
| O7 | Modèle d'avoir à exercer ; création sans envoi : ✅ **LEVÉ** — non exposée (§3.2, résultat typé) | sandbox (§10 q.5, q.6) |
| O8 | Montage marque grise (comptes/KYC/SIREN) | support (§10 q.7) |
| O9 | Rectification (Flux RE) → `SupportsReportRectification` | sandbox (§10 q.8) |
| O10 | ✅ **LEVÉ** (2026-06-12) : **polling V1** (`GET /v1.beta/invoices/{id}` confirmé — §3.4) ; webhooks = amélioration future | sandbox (PAS02, §3.4) |
| O11 | **NOUVEAU (2026-06-12)** : le pivot ne porte pas la date d'échéance (BT-9) → les factures à montant dû positif sont rejetées par BR-CO-25 (message intact — §3.2, limitation V1) ; étendre `PivotDocumentDto`/`CanonicalJson`/adaptateurs | item d'orchestration dédié |

> Tant qu'un point Oₙ n'est pas levé, la capacité correspondante reste **`false`** et le code ne
> contient **aucune valeur Super-PDP devinée**. PAS02 figera ces points en sandbox ; PAS03 mettra ce
> document à jour avec les réponses support obtenues.
