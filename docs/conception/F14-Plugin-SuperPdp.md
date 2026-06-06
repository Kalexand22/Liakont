# F14 — Plug-in PA Super PDP (`IPaClient`)
### Document de conception — `src/PaClients/Liakont.PaClients.SuperPdp`

> Statut : 🟨 **étude de conception (PAS01)** — rédigée AVANT ouverture de la sandbox Super PDP.
> Tout ce qui n'est pas vérifiable sur source publique ou sandbox est marqué **« à confirmer
> sandbox/support »** et n'est JAMAIS affirmé (CLAUDE.md n°2 : aucune règle inventée ; PAS01).
> Dernière mise à jour : 2026-06-05.
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
| Base URLs (sandbox / prod) | Domaines + chemins exacts de l'API | ❌ **à confirmer sandbox** (page doc JS non récupérable) | DR17 §1, §3 |
| Endpoints exacts (émission, statut, tax reports, settings) | Méthodes + chemins | ❌ **à confirmer sandbox/OpenAPI** | — |
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

### 3.2 Émission de document (facture B2C / avoir)

Conception cible (chemins **à confirmer sandbox/OpenAPI**) :

- `POST <base>/invoices` (ou `/documents`) avec le payload Super PDP (DTO **interne** à l'assembly).
- Mode « créé sans envoi » vs « créé ET envoyé » : aligner sur le paramètre `sendAfterImport` du
  contrat (équivalent du `send_after_import` B2Brouter) — **mécanisme exact à confirmer** (param,
  endpoint dédié, ou statut). Si Super PDP n'expose pas la création sans envoi, le plug-in retourne
  un `PaSendResult` cohérent et documente l'écart (jamais d'invention).
- **Avoirs** : DR17 liste « avoirs » seulement **« au panel des capacités à vérifier »** (non
  confirmé) → capacité `SupportsCreditNotes` provisoirement **`false`** (§5), passée à `true`
  uniquement une fois le modèle d'avoir (lien avoir→facture amendée) mappé en sandbox.

### 3.3 E-reporting B2C (Flux 10.3)

- Vérifié : Super PDP fait l'e-reporting B2C, **compté à la journée d'encaissements**. Le schéma
  d'agrégation (ledger quotidien, format, endpoint) est **à confirmer sandbox**.
- Flux paiement **10.2 (international)** et **10.4 (domestique)** : **non documentés** → capacités
  provisoirement **false** (§5), question support prioritaire (§10).

### 3.4 Statuts — webhooks ET/OU polling

- Super PDP **pousse des webhooks/callbacks** de statuts (vérifié). La plateforme Liakont étant
  centralisée (et non plus on-premise comme dans l'hypothèse polling de F05 §8), un **endpoint de
  réception de webhook** est envisageable. **Pattern obligatoire** (ajouter-un-plugin-pa, PAS.yaml) :
  l'endpoint de réception vit dans le **projet Web du plug-in** (le plug-in expose un `MapEndpoints`
  enregistré par le Host) — **jamais** dans le module `Transmission`.
- **Repli polling** : le contrat expose `GetDocumentStatusAsync(paDocumentId)`. Le plug-in DOIT
  supporter le polling (relecture de statut) même si les webhooks sont privilégiés, pour rester
  robuste (perte de webhook, rejeu). **Décision webhook vs polling à figer en sandbox (PAS02).**

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
| `SendDocumentAsync(PivotDocumentDto, sendAfterImport, ct)` → `PaSendResult` | Émission facture B2C / avoir | `POST <base>/invoices` (payload interne) | 🟠 endpoint à confirmer |
| `SendPaymentReportAsync(PaymentReportPeriod, ct)` → `PaSendResult` | E-reporting paiement (flux 10.2/10.4) | flux paiement non documenté | ❌ → renvoie `CapabilityNotSupported` tant que capacité false |
| `GetDocumentStatusAsync(paDocumentId, ct)` → `PaDocumentStatus` | Relecture d'état (polling) | `GET <base>/invoices/{id}` (+ webhook en push) | 🟠 endpoint à confirmer |
| `ListTaxReportsAsync(since, ct)` → `IReadOnlyList<PaTaxReport>` | Liste des déclarations | `GET <base>/tax_reports` | 🟠 endpoint à confirmer |
| `GetTaxReportAsync(taxReportId, ct)` → `PaTaxReport` | Détail déclaration (+ XML si dispo) | `GET <base>/tax_reports/{id}` | 🟠 endpoint à confirmer |
| `GetAccountInfoAsync(ct)` → `PaAccountInfo` | Consommation / limites | `GET <base>/account` | 🟠 endpoint à confirmer |
| `GetTaxReportSettingAsync(ct)` → `PaTaxReportSetting` | Lecture réglage déclaration | `GET <base>/tax_report_settings` | 🟠 endpoint à confirmer |
| `EnsureTaxReportSettingAsync(PaTaxReportSettingRequest, ct)` | Réglage idempotent (GET puis POST/PATCH si écart) | `POST`/`PATCH <base>/tax_report_settings` | 🟠 endpoint à confirmer |
| `GetGeneratedDocumentAsync(paDocumentId, ct)` → `PaGeneratedDocument` | Facture générée (Factur-X) pour archivage | endpoint de téléchargement | ❌ existence à confirmer (§10) |

### 4.1 Mapping des familles d'erreur → `PaSendState`

Identique au contrat (cf. `ajouter-un-plugin-pa.md` §1, F05 §4.1) — à valider sur les conventions
réelles de l'API Super PDP en sandbox :

| Réponse Super PDP | `PaSendState` |
|---|---|
| Émis / accepté | `Issued` |
| 4xx + `errors[]` | `RejectedByPa` (erreurs **intactes**, pas de retry) |
| **200 + erreurs métier** (erreur silencieuse, si Super PDP en produit) | `RejectedByPa` — **jamais** `Issued` (à vérifier sandbox : Super PDP a-t-il ce piège comme B2Brouter ?) |
| Réseau / 5xx / timeout | `TechnicalError` (re-tentable) |
| Capacité non déclarée (ex. flux paiement) | `CapabilityNotSupported` (résultat typé, **jamais** d'exception) |

- **Idempotence par numéro de document** (BT-1) : un numéro déjà émis n'est jamais ré-émis ; en cas
  de timeout sur émission, **relire** (GET liste) avant de retenter. Mécanique de relecture exacte à
  confirmer sandbox.
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

- **Suite de contrat héritée** : dériver `PaClientContractTests`
  (`tests/Liakont.PaClients.Contract.Tests/`) dans le projet de test du plug-in
  (`Liakont.PaClients.SuperPdp.Tests.Unit`), en pilotant `SuperPdpClient` par un **handler HTTP
  mocké** (les PA réelles n'ont pas de mode mémoire). La suite commune vérifie envoi valide, avoir,
  rejet, erreur silencieuse, timeout, idempotence, conservation `RawResponse`, et — surtout — que
  **toute capacité absente dégrade en résultat typé, jamais une exception**. Exemple vivant :
  `FakePaClientContractTests`.
- **Garde de frontière NetArchTest** (cf. `FakePaClientBoundaryTests`) : le plug-in ne référence que
  `Transmission.Contracts`.
- **Suite sandbox réelle** : `[Trait("Category","Sandbox")]`, **exclue de `verify-fast`, `run-tests`,
  `run-e2e` et de la CI** (testing-strategy §8). Clé locale via variable d'env `SUPERPDP_SANDBOX_KEY`
  (et `client_id`), **jamais committée**. Documentée dans `testing-strategy.md` (PAS03).

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
| O1 | Base URLs sandbox/prod + token-endpoint OAuth + scopes | sandbox (§10 q.9) |
| O2 | Endpoints exacts (émission, statut, tax reports, settings, compte) | sandbox / OpenAPI (§10 q.10) |
| O3 | Couverture flux paiement 10.2/10.4 | support (§10 q.1) |
| O4 | Endpoint de téléchargement de la facture générée (→ `SupportsDocumentRetrieval`) | support (§10 q.3) |
| O5 | Archivage : inclus ? NF Z42-013 ? horodatage ? réversibilité ? | support (§10 q.2) |
| O6 | Erreurs silencieuses 200 + erreurs métier ? | sandbox (§10 q.4) |
| O7 | Modèle d'avoir + création sans envoi | sandbox (§10 q.5, q.6) |
| O8 | Montage marque grise (comptes/KYC/SIREN) | support (§10 q.7) |
| O9 | Rectification (Flux RE) → `SupportsReportRectification` | sandbox (§10 q.8) |
| O10 | Webhook vs polling : décision d'implémentation | sandbox (PAS02, §3.4) |

> Tant qu'un point Oₙ n'est pas levé, la capacité correspondante reste **`false`** et le code ne
> contient **aucune valeur Super-PDP devinée**. PAS02 figera ces points en sandbox ; PAS03 mettra ce
> document à jour avec les réponses support obtenues.
