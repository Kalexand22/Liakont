# F18 — Plug-in PA Chorus Pro (dépôt Factur-X B2G via PISTE/OAuth2)

> **Statut : 🟨 NOTE DE CONCEPTION / TRAÇABILITÉ DES SOURCES (2026-06-20, item CP01).** Document de
> **traçabilité des constantes API/fiscales** Chorus Pro, écrit **AVANT de coder les constantes**
> (CLAUDE.md règle n°2 : les chaînes du plug-in sont **authentiques** — issues de la Spécification
> Externe API Chorus Pro — mais n'étaient **pas encore tracées** dans `docs/conception/` ; c'est un
> **défaut de procédure** comblé ici). Plan d'implémentation : `tasks/plan-chorus-pro.md` (§1, §2bis, CP0).
>
> **Légende des sources** (même registre que F15/F17) : ✅ = sourcé sur texte primaire vérifié
> (citation + URL) ; 🔶 = lecture courante de la Spec, **page exacte / version courante à verrouiller**
> sur PDF V5.00 + Swagger PISTE ; ❓ **À VERROUILLER** = valeur non confirmée, **bloquante avant de
> figer la constante** (owner : intégrateur lors du raccordement qualif). Conformément à CLAUDE.md n°2,
> **aucune règle inventée** : tout point non confirmé est marqué ❓, jamais énoncé en registre acquis.
>
> **Périmètre du plug-in.** Chorus Pro = **dépôt d'un Factur-X DÉJÀ scellé** (transport pur, modèle
> `Generique`) via **OAuth2 PISTE** (modèle `SuperPdp`). **B2G uniquement.** Le plug-in ne **construit
> aucun payload** depuis le pivot, ne **calcule/arrondit aucun montant** (transport pur, le payload
> `deposerFluxFacture` ne porte aucun montant), n'a **aucune logique fiscale**. e-reporting **EXCLU**
> (décision D2 — voir §9).

---

## 0. Objet et statut de sourcing

| Constante / point | Statut | Renvoi |
|---|---|---|
| `syntaxeFlux` Factur-X = `IN_DP_E2_CII_FACTURX` | ✅ sourcé (Spec V5.00 ; valeur stable) | §4.1 |
| 9 libellés `etatCourantFlux` (accents/casse) | ✅ sourcé (Spec V5.00) ; 🔶 page à verrouiller | §5 |
| Mapping `etatCourantFlux` → `PaSendState` (`Intégré` → `Issued` SEUL) | ✅ décision produit (CLAUDE.md n°3) | §5 |
| `scope=openid` (ajout PISTE au `client_credentials`) | ✅ sourcé (raccordement OAuth2 PISTE) | §3.1 |
| Format `cpro-account` = `base64(login:motDePasse)` | ✅ sourcé (Spec V5.00 / doc raccordement) | §3.2 |
| Endpoint jeton OAuth2 (sandbox) | 🔶 lecture courante — à verrouiller Swagger | §3.1 |
| Base API + chemin REST versionné (`/cpro/.../v1/…`) | ❓ **À VERROUILLER** (Swagger PISTE) — **ne pas hardcoder** | §4.3, §8 |
| `avecSignature = false` (notre artefact est non signé) | ✅ décision interne (D9) ; ❓ acceptation Chorus Pro d'un Factur-X **non signé** à confirmer | §7 |
| Résolution `idUtilisateurCourant` + **cardinalité** | 🔶 méthode tranchée (`consulterCompteUtilisateur`) ; ❓ **cardinalité / caractère requis** à verrouiller | §4.2 |
| `codeRetour` de succès (valeur exacte) | ❓ **À VERROUILLER** (Swagger) | §6 |
| Hôtes cibles `*.piste.gouv.fr` (PAS `aife.economie.gouv.fr`) | ✅ sourcé (décommissionnement 30/09/2023) | §8 |

> **Règle d'usage de ce document.** Une constante marquée ❓ **ne doit pas être figée dans le code**
> avant verrouillage sur le Swagger PISTE courant / le compte de qualification. Les constantes ✅
> peuvent être codées en référençant ce document. **Aucun endpoint en dur** : les URLs vivent dans
> `ChorusProDefaults`, alimentées depuis cette source et le Swagger (plan CP1/CP2).

---

## 1. Version de spec qui fait foi

- 🔶 **Spécification Externe API Chorus Pro V5.00 (2020)** = **référence des libellés et constantes**
  consultée. ⚠️ Les hôtes `*.aife.economie.gouv.fr` qui y figurent sont **décommissionnés depuis le
  30/09/2023** → remplacés par `*.piste.gouv.fr` (voir §8). Les **valeurs métier** (syntaxeFlux,
  `etatCourantFlux`, format `cpro-account`) restent valables ; seuls les **hôtes/chemins** sont à
  ré-ancrer sur le Swagger PISTE courant.
- 🔶 **Annexe « External Specifications API Appendix V4.14-bis »** consultée pour la **section
  idempotence / dédup** (faute de section équivalente repérée en V5.00) — voir §4.4. À **reconfirmer en
  V5.00 / Swagger** lors du raccordement.
- ❓ **À VERROUILLER** : confronter V5.00 publique vs annexe V4.14-bis vs **Swagger PISTE courant** au
  raccordement qualif ; tenir compte de la **réforme 2026** (décret 2024-266 + art. 123 LF 2026). En cas
  de divergence host/chemin, **le Swagger PISTE courant fait foi**.

---

## 2. Authentification — DOUBLE en-tête, à chaque appel métier

Chorus Pro via PISTE exige **deux** en-têtes simultanés sur chaque requête métier (distincts) :

```
Authorization: Bearer <token PISTE>           # jeton OAuth2 client_credentials (compte PISTE)
cpro-account: base64(loginTechnique:motDePasse)   # compte technique Chorus Pro (DISTINCT du compte PISTE)
```

### 2.1 Jeton OAuth2 PISTE (`client_credentials`)

- ✅ **Grant** : `client_credentials` ; **corps** `application/x-www-form-urlencoded` :
  `grant_type=client_credentials&client_id=<…>&client_secret=<…>&scope=openid`.
  Source : *Comment réussir son raccordement API OAuth2* (communauté Chorus Pro/PISTE) — URL §12.
- ✅ **`scope=openid`** est un **ajout spécifique PISTE** au `client_credentials` standard.
- 🔶 **Endpoint jeton (qualif)** : `https://sandbox-oauth.piste.gouv.fr/api/oauth/token` — **lecture
  courante, à verrouiller au Swagger PISTE**. Prod : hôte `oauth.piste.gouv.fr` (sans `sandbox-`) à
  confirmer.
- 🔶 **Durée du jeton** : **piloter sur le `expires_in` réel renvoyé** par le serveur (cache + renouveau
  proactif `expires_in − skew`). **Ne pas figer « 3600 s / sans refresh_token »** comme un fait — à
  confirmer au Swagger. Le modèle technique est `SuperPdpTokenProvider` (jeton court, pas de refresh).

### 2.2 En-tête `cpro-account` (compte technique Chorus Pro)

- ✅ **Format** : `base64(login:motDePasse)` du **compte technique Chorus Pro**, **distinct** du compte
  PISTE (`client_id`/`client_secret`). Constant par compte ; fourni par le resolver (plan CP6).
- ⚠️ **Sécurité (C5)** : `base64` **n'est PAS** du chiffrement. Le mot de passe technique est un **secret**
  (chiffré au repos, V012 — plan CP5). **Aucun logging** des en-têtes `cpro-account` **ni**
  `Authorization` (Chorus Pro = 1er connecteur HTTP réellement en prod) ; `RawResponse` conservée
  **expurgée de tout credential**.

---

## 3. Service `deposerFluxFacture` (émission / dépôt du Factur-X)

> ⚠️ `deposerFluxFacture` est le **nom du SERVICE**, **pas** un segment d'URL REST.

### 3.1 Payload (JSON)

```jsonc
{
  "idUtilisateurCourant": <résolu en amont — voir §3.2 ; OMIS si non requis>,
  "fichierFlux": "<base64 du PDF/A-3 Factur-X scellé>",
  "nomFichier": "<nom de fichier>",
  "syntaxeFlux": "IN_DP_E2_CII_FACTURX",   // ✅ Factur-X (CII dans PDF/A-3)
  "avecSignature": false                    // ✅ notre artefact est non signé (D9 ; §7)
}
```

- ✅ **`syntaxeFlux = IN_DP_E2_CII_FACTURX`** pour un Factur-X (CII embarqué dans un PDF/A-3). Source :
  Spec V5.00 (nomenclature des syntaxes de flux) — 🔶 page à verrouiller.
- ✅ **`fichierFlux`** = **base64 du PDF/A-3 scellé** porté par `PaSendContext` (FX07). Le plug-in
  **bloque si l'artefact est absent/vide** (`*_ARTEFACT_REQUIS`, `TechnicalError`, message FR) et **ne
  régénère JAMAIS** (CLAUDE.md n°6, patron `GeneriqueClient`). **Aucun montant** dans le payload.

### 3.2 Résolution de `idUtilisateurCourant`

- 🔶 **Méthode tranchée** (C3) : il n'existe pas de service « utilisateur courant » direct ;
  `idUtilisateurCourant` se résout via **`consulterCompteUtilisateur(adresseEmailConnexionUtilisateur)`**
  (entrée = **email de connexion du compte technique**), **mis en cache par compte**. Service du chapitre
  « Cross-functional and reference services » de l'Annexe V5.00.
- ❓ **À VERROUILLER** : **cardinalité** et **caractère requis** de `idUtilisateurCourant` pour la version
  ciblée (évolutif selon service/version). **Si requis** → l'inclure au payload (résolu + caché) ;
  **sinon** → **l'omettre**. Sourcer service ET cardinalité au Swagger PISTE avant de coder le champ.

### 3.3 Endpoint / base path

- ❓ **À VERROUILLER, NE PAS HARDCODER** : base API qualif = `https://sandbox-api.piste.gouv.fr/cpro/…` ;
  chemin REST **versionné** (ex. `/cpro/factures/v1/…`) → **à verrouiller sur le Swagger PISTE courant**.
  Tracer l'URL exacte ici une fois le Swagger consulté.

### 3.4 Retour du dépôt — `numeroFluxDepot` ≠ preuve d'intégration

- ✅ Le dépôt accepté renvoie `codeRetour`, `libelle`, **`numeroFluxDepot`** = **accusé de RÉCEPTION du
  flux**, **PAS** preuve d'intégration (A1/D5). → mapping `PaSendState.Sending` (voir §5), **jamais
  `Issued`** au dépôt.
- ✅ **Idempotence (A3/D8)** : **aucune clé d'idempotence client, aucun `idExterne`** (Annexe V4.14-bis —
  🔶 à reconfirmer V5.00/Swagger) ; la dédup serveur est **métier par n° de facture à l'intégration**, pas
  une idempotence de flux. → timeout/réseau = **`TechnicalError` SANS re-POST automatique** (sinon double
  dépôt = double facture, CLAUDE.md n°3). Reprise opérateur.

---

## 4. Service `consulterCR` (compte rendu d'intégration) — `etatCourantFlux`

✅ Le champ **`etatCourantFlux`** (Spec V5.00) a **9 valeurs** accentuées/casse mixte — à mapper
**EXACTEMENT** (défaut **fail-safe** sur valeur inconnue). 🔶 Page à verrouiller au PDF V5.00.

| # | `etatCourantFlux` (libellé EXACT) | → `PaSendState` | Justification |
|---|---|---|---|
| 1 | `Reçu` | `Sending` | flux reçu, non encore intégré |
| 2 | `Traité SE CPP` | `Sending` | en cours côté plateforme |
| 3 | `En attente de traitement` | `Sending` | attente |
| 4 | `En cours de traitement` | `Sending` | en cours |
| 5 | `Incidenté` | `RejectedByPa` | doc `monitoring-flows` : « flux NON traité, à rejouer entièrement » → **reprise opérateur, jamais re-dépôt automatique** (cohérent D8). 🔶 à confirmer en raccordement |
| 6 | `Rejeté` | `RejectedByPa` | rejet ; `Errors` + `RawResponse` intacts |
| 7 | `En attente de retraitement` | `Sending` | attente d'un retraitement |
| 8 | **`Intégré`** | **`Issued`** | ✅ **SEUL** chemin vers `Issued` (A1/D5) |
| 9 | **`Intégré partiellement`** | `RejectedByPa` | C2 — prudent : intégration incomplète ≠ émis ; `Errors` + `RawResponse` intacts |
| — | **valeur inconnue** | **fail-safe — JAMAIS `Issued`** | C1 (CLAUDE.md n°3) |

- ✅ **Règle d'or (CLAUDE.md n°3)** : **`Issued` UNIQUEMENT sur `Intégré`**. Tout le reste reste `Sending`
  (états transitoires, lecture idempotente, retry backoff) ou `RejectedByPa`. **Valeur inconnue → jamais
  `Issued`**.
- 🔶 `Incidenté` : ne **jamais** mapper en `Technical` re-tentable (re-déposerait à l'aveugle) — à
  confirmer en raccordement que la reprise est bien manuelle.

---

## 5. `codeRetour`

- ❓ **À VERROUILLER (Swagger PISTE)** : valeur(s) exacte(s) de **`codeRetour` de succès** du dépôt et des
  consultations. Sémantique connue : `codeRetour` OK → dépôt accepté → `Sending` ; 4xx **métier** →
  `Rejected` (`PaError[]` **intactes**) ; 5xx / 401 / 403 / **timeout** → `Technical` (re-tentable, **sans
  re-POST de dépôt**). Tracer ici les codes exacts une fois le Swagger consulté.

---

## 6. Acceptation d'un Factur-X **non signé** (`avecSignature=false`)

- ✅ **Décision interne (D9)** : notre artefact scellé est un **PDF/A-3 conforme NON signé**
  (`FacturXBuilder.Seal` n'applique aucune signature ; `PaSendContext` = `ReadOnlyMemory<byte>` opaque ;
  module `Signature`/Yousign disjoint). → constante **`avecSignature = false`** (reflet de l'artefact).
- ❓ **À VERROUILLER (Spec/raccordement)** : confirmer que **Chorus Pro ACCEPTE un Factur-X non signé**.
  Si une signature est **exigée**, la décision **et** la démo cassent → étendre `PaSendContext` (drapeau
  alimenté au build, **impact contrat**). À trancher avant l'envoi réel.

---

## 7. Endpoints `*.piste.gouv.fr` (et NON `aife.economie.gouv.fr`)

- ✅ Les URLs PISTE historiques (`*.aife.economie.gouv.fr`) sont **décommissionnées depuis le 30/09/2023**.
  Source : *Décommissionnement des URL PISTE historiques* — URL §12.
- ➡️ **Toujours `*.piste.gouv.fr`.** Hôtes/chemins exacts **depuis le Swagger PISTE courant** (jeton :
  `*-oauth.piste.gouv.fr` ; API : `*-api.piste.gouv.fr/cpro/…`). Sandbox = préfixe `sandbox-`. **Aucun
  endpoint en dur** dans le code : `ChorusProDefaults` lit cette source + le Swagger.

---

## 8. e-reporting EXCLU du plug-in (D2)

- ✅ **Capacités e-reporting du plug-in = `false`** → résultat typé (jamais d'exception). Raison : (a)
  **aucune API e-reporting Chorus Pro publiée** (seul `deposerFluxFacture` / e-invoicing existe) ; (b)
  l'e-reporting B2B **privé** transite par PA/PDP, pas par Chorus Pro.
- 🔶 **Note CMP (hors plug-in)** : pour un **émetteur public**, la doctrine route le B2C/G2C par Chorus
  Pro, mais **sans API e-reporting** et rattachement non vérifié → **arbitrage fiscal CMP**
  (`unresolved-needs-arbitration`), **distinct** de l'adaptateur Chorus Pro (dépôt Factur-X B2G). Ne PAS
  coder de routage sur hypothèse (CLAUDE.md n°2/8).

---

## 9. Capacités déclarées (synthèse — détail en plan CP7)

Rien d'inventé ; chaque capacité est **explicite** (`PaCapabilities`), pilote le comportement (jamais
`if (pa is ChorusPro)`) :

- `SupportsFacturXTransmission = true` (transport d'un Factur-X scellé `IN_DP_E2_CII_FACTURX`).
- `SupportsB2bInvoicing = false` (Chorus Pro = B2G), e-reporting `false` (D2), `SupportsCreditNotes =
  false` **en démo** (bascule `true` **uniquement** sur confirmation Spec/sandbox, jamais déduite d'un
  test vert — patron `SuperPdpCapabilities`), récupération doc/tax-reports `false` (hors tranche démo).

---

## 10. Points à verrouiller AVANT de figer une constante (récap ❓)

1. **Base API + chemin REST versionné** (`/cpro/.../v1/…`) — Swagger PISTE courant (§3.3).
2. **Endpoint jeton** OAuth2 sandbox/prod exact (§2.1).
3. **`codeRetour`** de succès — valeur(s) exacte(s) (§5).
4. **`idUtilisateurCourant`** — cardinalité + caractère requis (§3.2).
5. **Acceptation Factur-X non signé** par Chorus Pro (§6).
6. **Idempotence** — confirmer en V5.00/Swagger l'absence de clé client (§3.4).
7. **Version qui fait foi** — V5.00 vs V4.14-bis vs Swagger courant + impact réforme 2026 (§1).
8. **Pages exactes** du PDF V5.00 pour les libellés `etatCourantFlux` et la nomenclature `syntaxeFlux`.

---

## 11. Sources (officielles — endpoints à re-vérifier au catalogue/Swagger PISTE courant)

- **Décommissionnement URLs PISTE historiques (30/09/2023)** :
  `https://piste.gouv.fr/decommissionnement-des-url-piste-historiques`
- **Spécifications Externes API Chorus Pro — Annexe V5.00 (2020 ; hôtes périmés)** :
  `https://communaute.chorus-pro.gouv.fr/wp-content/uploads/2020/04/External_Specifications_API_Appendix_V5.00.pdf`
- **Raccordement OAuth2 PISTE** :
  `https://communaute.chorus-pro.gouv.fr/chorus-pro-piste-comment-reussir-son-raccordement-api-oauth2/`
- **Dépôt de facture / suivi des flux** : `https://communaute.chorus-pro.gouv.fr/submit-flow-invoice/` ·
  `https://communaute.chorus-pro.gouv.fr/monitoring-flows/`
- **e-reporting B2B privé via PA** :
  `https://www.impots.gouv.fr/facturation-electronique-et-plateformes-agreees`
- **Chorus Pro = PPF / secteur public** :
  `https://www.impots.gouv.fr/actualite/chorus-pro-restera-la-plateforme-de-reference-pour-la-facturation-electronique-du-secteur`
- **Calendrier réforme** : décret 2024-266 + art. 123 LF 2026 (Légifrance).

---

> **Réf. internes** : `tasks/plan-chorus-pro.md` (§1, §2bis, CP0, §8 sources) ; F16 (génération Factur-X
> PDF/A-3 + CII, artefact transporté) ; F14 (plug-in Super PDP, modèle OAuth2). Items : `orchestration/items/CP.yaml` (CP01..CP10).
