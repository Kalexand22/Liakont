# ADR-0030 — Client soft de signature sur place (Wacom) : capteur desktop hors `agent/` + proxy `OnSiteCapture` tenant-scopé + hash de binding propre + déposant ≠ signataire + DPAPI locale

- **Statut** : Proposé (2026-06-16).
- **Date** : 2026-06-16
- **Nature** : cet ADR **précède** le chantier d'implémentation (client soft non démarré, **aucun code** ; le module
  `Liakont.Modules.Signature` est livré par SIG03, le client + le proxy par SIG08, l'ajout de la 3ᵉ solution à
  `verify-fast` par SIG09). Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la
  **cible**, pas l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test.
  Cet ADR est une **ADR-fille d'ADR-0022 §6** et une **sœur d'ADR-0024/0025/0026/0027/0028/0029** : il tranche une
  **frontière de comportement** (un capteur de signature sur place, pur capteur sans logique métier, piloté par
  l'abstraction à capacités d'ADR-0027) ; il ne tranche **aucun point fiscal** et prend des **défauts défendables**
  RGPD/eIDAS **paramétrables tenant** (la qualification fine relève du DPO/EC du client au déploiement, F17 §10).
- **Numérotation** : ADR-**0030**. Plan d'ADR-filles du lot signature (F17 §9) : 0027 (abstraction), 0028 (module
  générique), 0029 (Yousign), **0030** (ce client Wacom). Les amendements (F15 §1.9, ADR-0024 journal) sont gravés
  dans le même item (SIG02). *(Note : deux fichiers `ADR-0023-*` coexistent — collision de numéro assumée ; sans
  incidence ici.)*
- **Contexte décisionnel** : `docs/conception/F17-Signature-Validation-Document.md` §1.3 (Wacom : qualification +
  RGPD), §6 (volet sur place), §8 (secrets DPAPI), §9 (plan d'ADR), §10 (points ouverts — #5 AES, #7 RGPD, #8
  identité **tranché**), §11 (garde-fous P1) ; `docs/adr/ADR-0027-abstraction-signature-capacites.md`
  (`ISignatureProvider`, `SupportedLevels`, `SupportsBiometricTemplateMatching`) ;
  `docs/adr/ADR-0028-workflow-validation-document-generique.md` (preuve attachée, `DocumentEvent` append-only) ;
  patrons réels : `agent/tests/Liakont.Agent.Core.Tests/AgentBoundaryTests.cs` (test de frontière au niveau IL) ;
  `src/Modules/Notification/Infrastructure/Handlers/Commands/TestFireWebhookHandler.cs` (re-vérif
  `subscription.CompanyId == request.CompanyId`) ; `tools/verify-fast.ps1` (garde bootstrap `Test-SolItemPending`).

## Contexte

Le second volet de signature est **sur place** (salle des ventes / criée) : capture manuscrite via un **pad Wacom
STU** et le **Wacom Ink SDK for signature** (SDK **natif**, USB, .NET Framework). Karl a tranché (F17 §10 #8) que
c'est le **mandant qui signe en personne** (vente aux enchères, pas criée), identifié au guichet par la société de
ventes (SVV).

Trois pièges décisifs :

1. **Frontière physique et technique** : le capteur n'est **pas** l'agent d'extraction ; il embarque un SDK natif
   qu'il ne faut **pas** laisser entrer sous `agent/`. Le motif « le client doit référencer un contrat plateforme,
   or `AgentProjectReferenceTests` l'interdit » **était faux** (le client est purement HTTP, sans `ProjectReference`
   plateforme) — le **vrai** motif est l'isolation du SDK natif + la frontière physique.
2. **Identité** : le poste qui téléverse (le **déposant**, opérateur de la salle) **n'est pas** la personne qui tient
   le stylet (le **signataire**, le mandant). Confondre les deux fabriquerait une preuve d'identité fausse.
3. **Binding** : « re-hash = hash signé » n'a de sens que si **les mêmes octets exacts** sont hashés côté client et
   côté plateforme — et **aucun** hash de binding calculable n'existe encore (ADR-0023 ne définit aucun hash ; le seul
   hash du lot Factur-X, F16 §7/FX06, est celui de l'artefact transmis pour la journalisation, pas une primitive de
   binding).

S'y ajoutent les **secrets** côté poste (DPAPI, **sans** référencer le code de l'agent) et la posture **RGPD
biométrie** (la dynamique de signature est listée par la CNIL parmi les techniques biométriques). La question
tranchée ici : **comment capter une signature sur place, en preuve exploitable, sans violer les frontières ni
fabriquer une identité, avec des défauts RGPD/eIDAS défendables et paramétrables ?**

## Décision

### 1. Modèle d'hébergement : **desktop-companion** (option A)

Exécutable **Windows autonome (.NET Framework 4.8)**, **Wacom Ink SDK for signature**, pad **STU** par USB, **son
propre installeur**. Les options **B** (navigateur WebHID — Chromium desktop only) et **C** (service local SigCaptX —
empreinte lourde, fragile) sont **écartées en V1** : le SDK étant **natif**, le navigateur seul est exclu ;
justification du choix A — modèle desktop .NET 4.8 + DPAPI **déjà maîtrisé** (l'agent), faisabilité maximale, zéro
dépendance navigateur.

### 2. Racine **`clients/OnSiteSignature/`** distincte, jamais sous `agent/` ; pureté + garde `<PackageReference>`

- Le client soft vit dans une **racine de solution DISTINCTE** (`clients/OnSiteSignature/` avec sa propre `.sln`),
  **jamais sous `agent/`**. Motif (qui tient) : **isoler le SDK Wacom natif** (USB, .NET Framework) et poser une
  **frontière physique** nette (le capteur n'est pas l'agent ; aucune logique métier).
- **Test de pureté symétrique** : le client ne référence **ni** `Liakont.Agent.Contracts` **ni** un module métier
  (pur capteur ; il parle au proxy **uniquement en HTTP**, sans `ProjectReference` vers un contrat plateforme).
- **Garde au niveau `<PackageReference>` déclaratif (oubli comblé).** `AgentBoundaryTests` **existe déjà**
  (`agent/tests/Liakont.Agent.Core.Tests/AgentBoundaryTests.cs`) et opère **au niveau IL**
  (`GetReferencedAssemblies()`, liste blanche fermée) — il couvre les références **exercées**. Le trou résiduel est
  **plus étroit** : un `<PackageReference>` purement **déclaratif non exercé** (aucun type référencé) lui échappe.
  ⇒ **ajouter une inspection déclarative des `<PackageReference>` des `.csproj` sous `agent/`** (y **interdire le SDK
  Wacom** et toute lib non déclarée) — **pas** « écrire un test IL inexistant ». **(Livré par SIG09.)**
- **`verify-fast` doit builder/tester la 3ᵉ solution — et le guard doit être en place AVANT que le code client
  n'atterrisse.** `tools/verify-fast.ps1` ne build aujourd'hui que `src/Liakont.sln` + `agent/Liakont.Agent.sln`.
  Une racine `clients/OnSiteSignature/*.sln` non ajoutée serait **ni buildée ni testée** → le test de pureté serait
  **écrit-mais-jamais-lancé** (faux-vert). ⚠️ **Séquencement (correction P1) :** si le guard verify-fast n'est ajouté
  qu'**après** le client (ex. dans un item postérieur à celui qui livre `clients/OnSiteSignature`), alors la passe
  `verify-fast` **obligatoire** de l'item qui livre le client **passe au vert sans builder ni exécuter** la nouvelle
  solution ni son test de pureté = **exactement le faux-vert que cet ADR prévient**. **Décision :** le guard
  verify-fast est livré **AVANT ou DANS LE MÊME item que** le code client (étape initiale de SIG08, ou item antérieur),
  **jamais après**. **Garde bootstrap présence-aware** (modèle `Test-SolItemPending`, jamais un skip silencieux) :
  verify-fast **build `clients/OnSiteSignature/*.sln` dès que la `.sln` est présente sur disque** (donc la passe de
  l'item qui livre le client l'exerce immédiatement) ; il **SKIP** uniquement tant que la solution est absente **et**
  SIG08 non `done` ; il **ÉCHOUE** si la `.sln` attendue est **absente une fois SIG08 `done`**. ⚠️ **Implication
  orchestration (à acter par l'opérateur, hors périmètre SIG02) :** le manifest place aujourd'hui le travail
  verify-fast en SIG09 (`depends_on SIG08`) — il doit être **réordonné avant SIG08** (ou ce sous-travail folded dans
  SIG08) pour respecter la décision ci-dessus ; sinon SIG08 produit un faux-vert sur son propre livrable.

### 3. Pur **capteur** → proxy `OnSiteCapture` tenant-scopé (aucune logique métier, aucun accès base)

Le client soft **N'EST PAS l'agent**. Pur **capteur** (geste + horodatage + binding hash) qui **POST un objet
immuable** `{ FSS chiffré + image PNG + hash du Factur-X signé + identité opérateur DÉCLARÉE (indicative, NON
probante — voir §5) }` vers un **endpoint plateforme dédié** : le proxy **`OnSiteCapture`** du module `Signature`
(HTTPS, **auth derrière l'abstraction IdP** — Keycloak = une impl, jamais d'appel IdP-spécifique ; **tenant-scopé**).

- **Tenant-scoping serveur (CLAUDE.md n°9) :** le proxy **re-vérifie côté serveur l'appartenance `document_id →
  company_id`** du caller (clé API / tenant scopé) et **lève `NotFound` sinon** — modèle
  `TestFireWebhookHandler` (`subscription.CompanyId == request.CompanyId`). **Aucune confiance** dans le `company_id`
  envoyé par le client.
- **Aucune logique métier** dans le client : toute décision (transition `DocumentValidation`, bascule tacite,
  ouverture du gate) reste **côté plateforme / `TenantJobRunner`** (ADR-0028). **Aucun accès base** côté client.

### 4. Hash de **binding** = primitive **PROPRE à ADR-0030** (pas ADR-0023)

⚠️ **NE PAS attribuer ce hash à ADR-0023** : ADR-0023 ne définit **aucun** hash (scellement PDF/A-3 + sérialiseur
CII + XMP + validation seulement) ; le seul hash du lot Factur-X (F16 §7/FX06) est celui de l'**artefact transmis
pour la journalisation**, pas une primitive de binding calculable. ADR-0030 grave donc explicitement :

- **quels octets** : les **octets EXACTS de l'artefact Factur-X scellé transmis** (octet pour octet, **sans
  re-canonicalisation côté client**) ;
- **quel algorithme** : **SHA-256** ;
- **un seul flux d'octets** côté client **ET** plateforme.

La plateforme calcule le hash de binding, le **client signe ce hash**, la plateforme **re-hashe et vérifie**
(`re-hash == hash signé`) ; sans ce flux d'octets unique, « re-hash = hash signé » échouerait ou deviendrait
contournable. La preuve est enregistrée en **WORM** + `DocumentEvent` **append-only**.

### 5. Identité : **DÉPOSANT** (principal authentifié) ≠ **SIGNATAIRE** (liaison vérifiée séparée)

On enregistre **deux champs distincts** :

- **`UploaderPrincipal`** = le **déposant** = le **principal authentifié** de l'appel proxy (session / clé API IdP) =
  le poste / l'opérateur de la salle qui téléverse. **Fiable, jamais lu depuis le payload.**
- **`SignerIdentity`** = le **signataire** = exige un **mécanisme de liaison VÉRIFIÉ** (art. 26 b eIDAS,
  « identifier le signataire ») — **jamais** dérivé du déposant, **jamais** cru depuis le payload brut.

✅ **DÉCIDÉ (Karl, F17 §10 #8) :** le **mandant signe en personne** (vente aux enchères) → le mécanisme de liaison
vérifié = **identification en personne par la SVV au guichet**. Le cas nominal **déposant ≠ signataire** (opérateur
qui téléverse, mandant qui signe) est **légitime** ; l'oracle du **test d'usurpation** est donc « **`SignerIdentity`
n'est JAMAIS dérivée du déposant ni du payload brut** » (et **non** « déposant ≠ signataire »).

### 6. Niveau eIDAS : `SupportedLevels = { SES }` au départ ; **AES seulement après audit**

Le `SupportedLevels` du provider `OnSiteCapture` **ne contient que `{ SES }` au départ** et **n'inclut `AES` qu'après
audit documenté** du procédé : scellement **PAdES/CAdES** côté plateforme (art. 26 d) + **identité vérifiée**
(art. 26 b) + **contrôle exclusif du moyen de création (art. 26 c)** — ce dernier étant **le point faible d'un pad
partagé en salle des ventes**, explicitement **à auditer** ; le mandant signataire présent et identifié en personne
(§5) sert l'art. 26 b mais **ne lève pas à lui seul** l'exigence 26 c. **Jamais AES par défaut, jamais QES** (Wacom
seul ≠ dispositif + certificat qualifiés, art. 3 §12). *(L'étiquette `SES` elle-même reste 🔶 — F17 §1.3 ; le choix
conservateur n'en dépend pas, et **jamais à la hausse sans audit art. 26**.)*

### 7. Secrets : **DPAPI LOCALE au client**, `DataProtectionScope = CurrentUser` (tranché)

- ⚠️ **NE PAS référencer `ISecretProtector`/`DpapiSecretProtector` de l'agent** (`agent/src/Liakont.Agent.Core`) :
  le client doit rester **pur** (§2). **Option (a) retenue : une implémentation DPAPI LOCALE au client** (même
  technique `ProtectedData`, ~30 lignes, **zéro dépendance inter-projet**). L'option (b) — extraire la primitive dans
  un paquet neutre partagé — est **écartée en V1** : un tel paquet consommé par l'agent net48 devrait multi-cibler
  `net48;net10` **et**, s'il était référencé depuis `agent/`, ferait échouer `AgentBoundaryTests` (liste blanche) —
  frictions non justifiées pour ~30 lignes.
- **`DataProtectionScope` GRAVÉ = `CurrentUser`** (décision de sécurité). **Justification :** contrairement à
  l'agent — qui tourne sous **deux comptes du même poste** (service `LocalSystem` + CLI), ce qui impose
  `LocalMachine` — le client soft est une **application interactive unique** lancée dans la **session de l'opérateur**.
  Sur un **poste de criée partagé**, `CurrentUser` confine le déchiffrement au **compte Windows qui a chiffré** le
  secret (un autre opérateur du même poste ne peut pas le déchiffrer), ce qui est la posture de **moindre privilège**
  correcte ; `LocalMachine` exposerait le secret à **tout** compte du poste. **+ entropie applicative.** Le canal
  pad → hôte est déjà chiffré par le SDK (RSA + AES, anti-rejeu).
- **Aucune donnée client dans le code (CLAUDE.md n°7).**

### 8. RGPD biométrie : **sobre par défaut**, conclusion « hors art. 9 » **conditionnelle DPO**

Conception **sobre, gravée dans la capacité** : **`SupportsBiometricTemplateMatching = false`** (ADR-0027). On
capture le **tracé + horodatage + binding hash** comme **preuve d'intégrité et de consentement**, **sans extraire ni
stocker de gabarit** de comparaison et **sans** vérification d'identité par la dynamique. **Invariant testable :**
*aucun composant ne dérive un feature-vector / gabarit du FSS tant que `SupportsBiometricTemplateMatching = false`*
(le flag gouverne le **matching**, jamais la **capture brute**).

**Posture produit (défendable, stade build) :** la CNIL liste expressément « la dynamique de signature » parmi les
techniques biométriques — on **ne prétend donc PAS** « hors art. 9 » comme acquis. Le **défaut défendable est
conservateur** (pas de gabarit, finalité strictement limitée à la preuve, sans identification unique) ; la
qualification fine (art. 9 / AIPD / rétention / droit à l'oubli vs conservation fiscale + WORM / consentement B2B)
relève du **DPO du client au déploiement**. **Aucune communication commerciale « sans AIPD »** tant que le client ne
l'a pas validé. **Si** `BiometricTemplateMatching` était un jour activée (OPT-IN, isolée derrière le port) : bascule
**art. 9** ⇒ consentement explicite (art. 9 §2 a), **AIPD** (art. 35), minimisation, chiffrement du gabarit à clé
individuelle — **désactivée par défaut, jamais en dur**.

### 9. Portée : structure et comportement, **aucun code, aucune décision fiscale**

Cet ADR **n'écrit aucun code** (client + proxy livrés par SIG08 ; ajout `verify-fast` par SIG09 ; UI console par
SIG10). Il **ne fixe aucun** point fiscal et prend des **défauts paramétrables tenant**, pas des gates. **Les ADR de
package** (SDK Wacom Ink ; lib PAdES/CAdES de scellement) restent à inventorier avant dev (Post-Dev Checklist), à la
charge de SIG08.

## Invariants

- **INV-ONSITE-1** — Le client soft vit dans **`clients/OnSiteSignature/`** (`.sln` propre), **jamais sous
  `agent/`** ; **test de pureté** : il ne référence **ni** `Liakont.Agent.Contracts` **ni** un module métier (pur
  capteur).
- **INV-ONSITE-2** — Le **SDK Wacom n'entre jamais sous `agent/`** : inspection déclarative des `<PackageReference>`
  des `.csproj` sous `agent/` (un `<PackageReference>` non déclaré, ex. SDK Wacom, **échoue** le test) — complète
  `AgentBoundaryTests` (IL). **(SIG09.)**
- **INV-ONSITE-3** — `verify-fast` **build + teste** la 3ᵉ solution `clients/OnSiteSignature` **dès que sa `.sln` est
  présente** (garde bootstrap **présence-aware**, modèle `Test-SolItemPending`) : skip **uniquement** si la solution
  est absente **et** SIG08 non `done` ; **ÉCHEC** si la `.sln` attendue est **absente une fois SIG08 `done`** (jamais
  un skip silencieux). ⚠️ Le guard est livré **avant ou dans le même item que** le code client (jamais après), pour
  que la passe `verify-fast` obligatoire de l'item livrant le client **exerce réellement** la nouvelle solution +
  son test de pureté (anti-faux-vert). **(Implication orchestration : réordonner le travail verify-fast avant SIG08 —
  hors périmètre SIG02.)**
- **INV-ONSITE-4** — Le client est un **pur capteur** : **aucune logique métier**, **aucun accès base** ; il POST un
  objet immuable au proxy `OnSiteCapture` (HTTPS, auth derrière l'abstraction IdP).
- **INV-ONSITE-5** — Le proxy `OnSiteCapture` est **tenant-scopé** : re-vérif serveur `document_id → company_id`,
  `NotFound` sinon (test cross-tenant) ; **aucune confiance** dans le `company_id` du payload.
- **INV-ONSITE-6** — **Hash de binding** = octets EXACTS de l'artefact Factur-X scellé (SHA-256, **même flux**
  client/plateforme, sans re-canonicalisation côté client) ; la plateforme vérifie `re-hash == hash signé` (test) ;
  preuve en **WORM** + `DocumentEvent` **append-only**. **Primitive propre à ADR-0030, pas ADR-0023.**
- **INV-ONSITE-7** — **Déposant ≠ signataire** : `UploaderPrincipal` = principal authentifié (jamais le payload) ;
  `SignerIdentity` = mécanisme de liaison vérifié séparé (identification en personne par la SVV — F17 §10 #8),
  **jamais** dérivé du déposant ni du payload. **Test d'usurpation** : `SignerIdentity` jamais dérivée du déposant ni
  du payload brut.
- **INV-ONSITE-8** — `SupportedLevels = { SES }` **tant que l'AES n'est pas auditée** (art. 26, dont 26 c pad
  partagé) ; **jamais AES/QES par défaut** (test).
- **INV-ONSITE-9** — Secrets protégés par **DPAPI LOCALE au client** (`ProtectedData`, **jamais** le
  `ISecretProtector` de l'agent), **`DataProtectionScope = CurrentUser`** + entropie applicative ; **jamais en
  clair**.
- **INV-ONSITE-10** — `SupportsBiometricTemplateMatching = false` par défaut ; **aucun gabarit / feature-vector
  dérivé du FSS** tant que le flag est `false` (capture ≠ matching) — test.

## Conséquences

**Positif** : le SDK Wacom natif est **isolé** hors `agent/` (frontière physique + technique nette) et la garde
`<PackageReference>` + l'ajout à `verify-fast` ferment des faux-verts concrets (test de pureté écrit-mais-jamais-lancé,
SDK glissé sous `agent/`) ; l'identité est **honnête** (déposant authentifié ≠ signataire à liaison vérifiée, pas
d'usurpation) ; le binding est **vérifiable** (flux d'octets unique, primitive propre) ; la posture RGPD/eIDAS est
**conservatrice et paramétrable** (pas de gabarit, SES jusqu'à audit), donc défendable au déploiement sans rien
inventer. On **réutilise** la technique DPAPI (locale), `Archive`/`DocumentEvent` append-only et le modèle de
tenant-scoping `TestFireWebhookHandler` — **aucun mécanisme transverse nouveau, aucun code de l'agent référencé,
aucun code `Stratum.*` vendored modifié.**

**À la charge du(des) lot(s) d'implémentation** : **SIG08** — client `clients/OnSiteSignature/` (.sln propre,
SDK Wacom, capture → POST immuable) + DPAPI locale `CurrentUser` ; proxy `OnSiteCapture` tenant-scopé (re-vérif
`document_id↔company_id`, `NotFound`) ; primitive de hash de binding (octets exacts/SHA-256/même flux) + vérif
`re-hash == hash signé` + preuve WORM/`DocumentEvent` ; `UploaderPrincipal` + `SignerIdentity` (liaison SVV) ;
`SupportedLevels = {SES}` ; `SupportsBiometricTemplateMatching = false` + invariant « pas de gabarit » ; ADR de
package (SDK Wacom Ink, lib PAdES/CAdES) ; tests : pureté + cross-tenant + binding + usurpation + « pas de gabarit ».
⚠️ **Le guard `verify-fast` (3ᵉ solution, présence-aware) doit être en place AVANT/AVEC le code client** (§2) — donc
livré comme première étape de SIG08 ou par un item antérieur, **jamais après** ; sinon SIG08 produit un faux-vert sur
son propre test de pureté (correction P1). **SIG09** — inspection déclarative des `<PackageReference>` agent (+ le
guard verify-fast s'il n'a pas déjà été folded dans SIG08, **à condition que SIG09 soit réordonné avant SIG08** —
implication orchestration à acter par l'opérateur). **SIG10** —
UI console signature (déclencher/statut/preuve, bUnit/Playwright, logique aux handlers MediatR — CLAUDE.md
review n°19).

**Limite** : cet ADR ne grave **ni** le plug-in Yousign (ADR-0029), **ni** le module `Signature` (ADR-0027/SIG03),
**ni** `DocumentApproval` (ADR-0028/SIG04), **ni** la qualification RGPD fine (DPO du client), **ni** un niveau eIDAS
par défaut (paramétrage tenant).

### Points NON TRANCHÉS (F17 §10 — défaut défendable pris, le client tranche au déploiement, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| 5 | AES Wacom (art. 26 c contrôle exclusif sur pad partagé) + détenteur du FSS | **`SES`** ; AES **seulement après audit technique** documenté du procédé | investigation tech |
| 7 | RGPD biométrie (art. 9 / AIPD / rétention / droit à l'oubli vs WORM / consentement B2B) | **sobre : pas de gabarit** (`SupportsBiometricTemplateMatching = false`), finalité limitée à la preuve | DPO du client au déploiement |
| 8 | Identité du signataire sur place | ✅ **DÉCIDÉ (Karl) : le mandant signe en personne** (vente aux enchères) ; liaison = identification en personne par la SVV | tranché |

Aucun de ces points ne stalle le dev : ce sont des **défauts paramétrables**, pas des gates (F17 §10). Le choix de
`DataProtectionScope = CurrentUser` (§7) n'est **pas** un point ouvert : c'est une **décision de sécurité prise ici**,
à prouver/justifier, pas une valeur fiscale/juridique.

## Alternatives rejetées

- **Placer le client sous `agent/`** (motif « `AgentProjectReferenceTests` l'interdit ») : motif **faux** (le client
  est purement HTTP, sans `ProjectReference` plateforme) ; le **vrai** enjeu est d'isoler le SDK natif + la frontière
  physique. **Rejetée** — racine `clients/OnSiteSignature/` distincte.
- **Se reposer sur `AgentBoundaryTests` (IL) seul** : un `<PackageReference>` déclaratif non exercé lui échappe.
  **Rejetée** — ajout d'une inspection déclarative des `<PackageReference>` (SIG09).
- **Ne pas ajouter la 3ᵉ solution à `verify-fast`** : le test de pureté serait écrit-mais-jamais-lancé (faux-vert).
  **Rejetée** — ajout avec garde bootstrap présence-aware.
- **Ajouter le guard `verify-fast` APRÈS le code client** (ex. en SIG09 `depends_on SIG08`) : la passe verify-fast
  **obligatoire** de l'item livrant le client passerait au vert **sans** builder/tester la nouvelle solution — le
  faux-vert même que cet ADR prévient. **Rejetée** — le guard est livré **avant ou avec** le code client, et il est
  **présence-aware** (build dès que la `.sln` existe).
- **Faire confiance au `company_id` envoyé par le client** : fuite cross-tenant. **Rejetée** — re-vérif serveur
  `document_id↔company_id`, `NotFound`.
- **Dériver `SignerIdentity` du déposant ou du payload** : fabrique une identité fausse (art. 26 b). **Rejetée** —
  liaison vérifiée séparée (identification en personne par la SVV).
- **Attribuer le hash de binding à ADR-0023** : ADR-0023 ne définit aucun hash. **Rejetée** — primitive propre à
  ADR-0030 (octets exacts/SHA-256/même flux).
- **Re-canonicaliser l'artefact côté client avant de hasher** : « re-hash = hash signé » deviendrait contournable.
  **Rejetée** — un seul flux d'octets, sans re-canonicalisation côté client.
- **`SupportedLevels` incluant AES (ou QES) par défaut** : non auditée (art. 26 c) ; QES impossible avec Wacom seul.
  **Rejetée** — `{SES}` jusqu'à audit.
- **Référencer `ISecretProtector`/`DpapiSecretProtector` de l'agent** : casse la pureté du client. **Rejetée** —
  DPAPI locale (~30 lignes).
- **`DataProtectionScope = LocalMachine`** : exposerait le secret à tout compte du poste de criée partagé.
  **Rejetée** — `CurrentUser` (moindre privilège pour une app interactive mono-session).
- **Extraire un gabarit biométrique « pour vérifier l'identité »** : bascule art. 9 non justifiée au stade build.
  **Rejetée** — `SupportsBiometricTemplateMatching = false`, finalité limitée à la preuve d'intégrité/consentement.

## Références

- `docs/conception/F17-Signature-Validation-Document.md` §1.3 (Wacom : qualification + RGPD), §6 (volet sur place),
  §8 (secrets DPAPI), §9 (plan d'ADR), §10 (#5 AES, #7 RGPD, #8 identité tranché), §11 (garde-fous P1).
- `docs/adr/ADR-0027-abstraction-signature-capacites.md` (`ISignatureProvider`, `SupportedLevels`,
  `SupportsBiometricTemplateMatching`) ; `docs/adr/ADR-0028-workflow-validation-document-generique.md` (preuve
  attachée, append-only).
- Patrons réels imités : `agent/tests/Liakont.Agent.Core.Tests/AgentBoundaryTests.cs` (frontière IL) ;
  `src/Modules/Notification/Infrastructure/Handlers/Commands/TestFireWebhookHandler.cs` (re-vérif `CompanyId`) ;
  `tools/verify-fast.ps1` (garde bootstrap `Test-SolItemPending`).
- ADR-fille d'**ADR-0022** (frontières) ; sœurs **ADR-0024/0025/0026/0027/0028/0029**. eIDAS : règlement UE 910/2014
  art. 3/25/26 ; C. civ. art. 1366/1367 ; décret n° 2017-1416 ; RGPD art. 4.14/9/35 ; doctrine CNIL biométrie.
  Wacom Ink SDK for signature (pad STU).
