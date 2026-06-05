# Ajouter un plug-in de Plateforme Agréée (PA)

> Checklist pour brancher une nouvelle PA (B2Brouter, Super PDP, ou toute future plateforme) sur
> Liakont SANS toucher au produit. C'est le cœur de l'indépendance PA : aucune fonctionnalité ne
> dépend de ce qu'UNE PA sait faire (blueprint.md §2 ; CLAUDE.md n°6/8/16). Le produit s'adapte aux
> **capacités déclarées** du plug-in, jamais à un `if (pa is …)` ni à un flag produit.
>
> **Sources** (rien n'est inventé ici) : `blueprint.md` §2, `docs/conception/F05-Client-API-B2Brouter.md`,
> `docs/architecture/testing-strategy.md` §6/§8, `docs/architecture/module-rules.md` §5/§6.
> Plug-in de référence livré : `src/PaClients/Liakont.PaClients.Fake/` (PAA02).

---

## Principes (à ne jamais enfreindre)

- Un plug-in PA est une **assembly séparée** sous `src/PaClients/<Nom>/`. Il référence **UNIQUEMENT**
  `Liakont.Modules.Transmission.Contracts` (+ Common si besoin) — **jamais** un autre plug-in, **jamais**
  un module métier, **jamais** `Transmission.Infrastructure` (module-rules §6 ; garde NetArchTest, P1).
- **Aucun type HTTP** ne traverse `IPaClient` : la construction du payload PA-spécifique vit DANS le
  plug-in (F05 §6). L'abstraction ne connaît que des DTOs pivot/PA neutres.
- **Une capacité absente n'est jamais une exception** : l'appel retourne un résultat TYPÉ
  (`PaSendResult.State = CapabilityNotSupported`, `PaGeneratedDocument.CapabilityNotSupported`, …).
  Le produit n'est jamais bloqué par les limites d'un PA (PAA01).
- **Montants en `decimal`** (CLAUDE.md n°1) ; **secrets chiffrés par tenant**, jamais en clair ni en
  log (CLAUDE.md n°10) — la résolution des secrets est interne au plug-in.
- **Aucune règle fiscale inventée** : tout vient du paramétrage du tenant (CFG02) ou de `docs/conception/`.

---

## Checklist

### 1. Implémenter `IPaClient`

Implémenter `Liakont.Modules.Transmission.Contracts.IPaClient` (envoi de document/avoir, e-reporting de
paiement par flux, statut, tax reports, compte, réglage de tax report, facture générée). Mapper chaque
famille d'issue de la PA vers l'état correct de `PaSendState` :

| Réponse de la PA | État attendu |
|---|---|
| Émis / accepté | `Issued` |
| 4xx + `errors[]` | `RejectedByPa` (erreurs **intactes**) |
| **200 + `errors[]`** (erreur silencieuse) | `RejectedByPa` — surtout **pas** `Issued` (F05 §4.1) |
| Réseau / 5xx / timeout | `TechnicalError` (re-tentable) |
| Capacité non déclarée | `CapabilityNotSupported` (résultat typé, **jamais** d'exception) |

- Garantir l'**idempotence** par numéro de document (BT-1) : un numéro déjà émis n'est jamais ré-émis.
- Conserver la **réponse brute** (`RawResponse`) pour la piste d'audit (F06/DR6).

### 2. Déclarer les capacités (`PaCapabilities`)

Exposer `Capabilities` avec les drapeaux réellement supportés par la PA (B2C, paiement domestique 10.4
et international 10.2 — **deux capacités séparées**, avoirs, récupération de tax reports, téléchargement
de la facture générée, rectification, `MaxDocumentsPerRequest`). **Avant d'agir, tester la capacité**
(comme `Capabilities.SupportsPaymentReport(flux)`), puis retourner un résultat typé si elle manque.
Les capacités sont la **seule** source de vérité du comportement du produit.

### 3. S'enregistrer dans le Host (registre de TYPES)

- Fournir une `IPaClientFactory` (`PaType` = la clé du type de plug-in, ex. `"B2Brouter"` ;
  `Create(PaAccountDescriptor)` construit le client pour le compte PA d'un tenant).
- Fournir une extension `AddXxxPaClient(this IServiceCollection)` qui ajoute la fabrique via
  `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaClientFactory>(…))` — patron de
  `FakePaClientRegistration`. Le `IPaClientRegistry` du module les indexe par `PaType` : la résolution
  d'un compte de tenant se fait **par la clé**, jamais par un `if (type == "…")` (PAA01 §5).

### 4. Hériter la suite de contrat

La base abstraite `PaClientContractTests` vit dans la bibliothèque **sans plug-in**
`tests/Liakont.PaClients.Contract.Tests/` (elle ne référence que `Transmission.Contracts` + le pivot —
c'est ce qui permet de l'hériter sans tirer une autre PA en dépendance transitive). Dans le projet de
test de **votre** plug-in (`<Nom>.Tests.Unit`), ajoutez une `ProjectReference` vers cette bibliothèque,
dérivez `PaClientContractTests` et implémentez `CreateClient` : mappez chaque `PaClientContractSetup`
(issue + capacités) vers un client de votre plug-in **piloté par un mock HTTP** (les PA réelles n'ont
pas de mode mémoire). L'exemple vivant est `FakePaClientContractTests` dans
`Liakont.PaClients.Fake.Tests.Unit`. La suite commune vérifie alors, sans aucun test réécrit, l'envoi
valide, l'avoir, le rejet, l'erreur silencieuse, le timeout, l'idempotence, la conservation de la
réponse brute (audit F06/DR6) et — surtout — que **toute capacité absente dégrade en résultat typé,
jamais une exception**. Ajoutez aussi une garde de frontière NetArchTest (cf. `FakePaClientBoundaryTests`) :
le plug-in ne référence que `Transmission.Contracts`.

### 5. Fournir la suite réelle (Staging / Sandbox), séparée

Les envois réels (B2Brouter **Staging**, Super PDP **Sandbox**) sont une suite **manuelle**, marquée
`[Trait("Category","Staging")]` / `[Trait("Category","Sandbox")]`, exécutée **avant chaque gate PA** et
**jamais en CI** (clé/API réelle requise — testing-strategy §8). Elle est exclue de `verify-fast`,
`run-tests` et `run-e2e` par filtre. Ne **jamais** committer de secret réel : la clé reste locale /
chiffrée par tenant (CLAUDE.md n°10).

---

## Définition de « fini » pour un plug-in PA

- [ ] `IPaClient` implémenté ; aucune capacité ne lève (résultat typé) ; idempotence assurée.
- [ ] `PaCapabilities` déclarées et **cohérentes avec le comportement réel** (vérifié par la suite de contrat).
- [ ] Fabrique + `AddXxxPaClient` enregistrées ; résolution par `PaType` (aucun `if (pa is …)`).
- [ ] Suite de contrat (`PaClientContractTests`) **verte** contre le plug-in (mock HTTP).
- [ ] Garde de frontière NetArchTest verte (ne référence que `Transmission.Contracts`).
- [ ] Suite réelle Staging/Sandbox écrite, exécutée hors CI avant la gate PA.
- [ ] `verify-fast` + `run-tests` verts ; review propre.
