# ADR-0022 — Le mandant d'autofacturation : tiers de première classe d'un module `Mandats` tenant-scopé (jamais un sous-tenant)

- **Statut** : Proposé (2026-06-13).
- **Date** : 2026-06-13
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Mandats` non démarré, **aucun code**).
  Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**, pas l'état du
  code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR tranche une
  **frontière de structure** ; il ne tranche **aucun point fiscal** — ceux-ci restent **NON TRANCHÉS dans F15**
  (décision expert-comptable / re-sourçage).
- **Contexte décisionnel** : `docs/conception/F15-Autofacturation-Mandat.md` (note d'orientation autofacturation 389,
  §1 sourcé, §2 module `Mandats`, §6 NON TRANCHÉ) ; `blueprint.md` §2 (généricité), §7 (multi-tenancy — *« 1 tenant =
  1 client final = 1 SIREN »*, *« pas de hiérarchie de tenants »*) ; `CLAUDE.md` n°6 (frontières inter-modules par
  Contracts), n°7 (aucune donnée client dans le code), n°9 (toute requête métier tenant-scopée) ;
  `docs/adr/socle/ADR-0011-database-per-tenant.md` (isolation physique par tenant) ;
  `docs/adr/ADR-0004-perimetre-contrat-extraction-pivot-v1.md` (le pivot porte déjà `Invoicer`/`IsSelfBilled`) ;
  `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (SOL06 : `ITenantJobRunner`/`ITenantScopeFactory`) ; patterns
  réels `src/Modules/TvaMapping/**` (`MappingTable`, `MappingChangeLog`, triggers append-only) et
  `src/Modules/TenantSettings/**` (`FeeImputationMethod`, `AuctionVerticalSettings`).

## Contexte

L'autofacturation sous mandat (BT-3 = 389, art. 289 I-2 CGI — voir F15) introduit le **MANDANT** : le vendeur au
nom et pour le compte duquel le tenant émet (livreur de lait, multiplicateur de semences, armement de pêche,
commettant d'enchères). Le pivot porte **déjà** `Invoicer` et `IsSelfBilled` au contrat (ADR-0004), mais il
n'existe **aucun concept** de mandant ni de mandat dans le produit.

Tension structurante avec le multi-tenancy. `blueprint.md` §7 pose : **1 tenant = 1 client final qui PAIE = le
mandataire-émetteur** (la laiterie, la criée, la maison de ventes). Le **mandant n'est PAS le payeur** : c'est un
**tiers récurrent** du tenant, à forte volumétrie (DR-Agro : ~42 000 livreurs de lait, 16 504 multiplicateurs ;
criée de Keroman : 456 armements distincts constatés en base `Kerport_Fact`, F15 §4).

Or le mandant porte des **obligations propres** qui exigent un **état persistant et auditable**, pas un objet
jetable : numéro de TVA (BT-31) et statut d'assujettissement **déclaré**, cycle de vie du **mandat** (clause,
caractère écrit/tacite, délai de contestation, révocation), **numérotation chronologique distincte par mandant**
(sourcé BOI-TVA-DECLA-30-20-20-10, F15 §1.4), workflow d'**acceptation/contestation** (F15 §1.5), et un **export
d'audit probant** par mandant (la responsabilité fiscale est croisée mandant/mandataire).

La question tranchée ici : **où vit cette entité, sans briser le multi-tenancy ni la généricité ?**

## Décision

### 1. Le mandant est une entité de **première classe** d'un **nouveau module `Liakont.Modules.Mandats`**

Le module porte le registre des mandants, le cycle de vie du mandat, la numérotation par mandant et le workflow
d'acceptation. Sa persistance vit dans le schéma `mandats` de la **base DU tenant** (database-per-tenant, socle
ADR-0011). Il est accédé par les autres modules (Pipeline, Documents, Validation) **uniquement par ses
`Contracts`** (frontière imposée par NetArchTest, `CLAUDE.md` n°6).

### 2. Le mandant n'est **JAMAIS un sous-tenant**

`blueprint.md` §7 interdit toute hiérarchie de tenants. Un mandant **appartient à UN tenant**, porte un
`company_id` défensif (convention vérifiée dans `TvaMapping`) et n'est **jamais** partagé ni lu en cross-tenant.
Le mandant est une **donnée de référence** réutilisée document après document, **pas** un `PivotParty` jetable.

### 3. Registre et cycle de vie = paramétrage tenant **journalisé en transaction** (gabarit `TvaMapping`)

Parce que le mandat est de la **preuve fiscale**, sa persistance suit le gabarit FORT de `TvaMapping`, **pas** le
gabarit léger post-commit de `AuctionVerticalSettings` : validation humaine explicite (`ValidatedBy`/`ValidatedDate`),
**`Invalidate()` à chaque mutation** (→ autofacturation 389 suspendue jusqu'à revalidation), **mutation + entrée
de journal append-only écrites dans la MÊME transaction**, immuabilité du journal garantie par **trigger base**
(jamais par du code applicatif). L'activation du module/profil (le « ce tenant fait de l'autofacturation »), elle,
peut suivre le gabarit léger.

### 4. Statut d'assujettissement et délai de contestation = **NULLABLE = suspendu** (gabarit `FeeImputationMethod`)

Toute donnée du mandat dont la valeur relève d'une décision non encore prise (statut d'assujettissement déclaré du
vendeur, délai de contestation) est **nullable** ; `null` = décision en attente = **389 suspendu**, **jamais un
défaut inventé** (`CLAUDE.md` n°2/3 : bloquer plutôt qu'émettre faux).

### 5. **Aucune donnée client dans le code** (`CLAUDE.md` n°7)

Mandants, numéros de TVA, préfixes de numérotation réels = **paramétrage de tenant** (en base) ou **seed
`deployments/<client>/`**. Le code n'embarque que des **exemples fictifs** (`config/exemples/`).

### 6. Portée : cet ADR acte la **structure**, pas l'implémentation, et ne tranche **aucune décision fille**

Cet ADR **ne déplace ni n'écrit aucun code**. Les décisions suivantes feront l'objet d'**ADR séparés** : (a) le
**workflow d'acceptation** comme agrégat distinct + port `ISelfBilledGate` (ne pas étendre la machine d'émission) ;
(b) l'**allocation BT-1 hybride** (identifiant source = clé d'idempotence ; BT-1 fiscal alloué par mandant) + la
**re-clé anti-doublon F06** (conditionnée à un amendement de F06 §4) ; (c) le contrat de **réception
`IPaInboundClient` séparé** (capacité B). Les points **fiscaux** (statuts admis, délai, code de l'avoir, routage
261-2-4°, taxe de criée…) restent **NON TRANCHÉS dans F15 §6**.

## Invariants

- **INV-MANDATS-1** — Tout `Mandant`/`Mandat`/séquence/acceptation est **tenant-scopé** (`WHERE company_id =
  @CompanyId` via `ICompanyFilter.GetRequiredCompanyId` dans chaque handler) ; **aucune** requête cross-tenant ; un
  mandant n'est **jamais** un sous-tenant.
- **INV-MANDATS-2** — Le module `Mandats` n'est accédé par les autres modules **que par ses `Contracts`**
  (NetArchTest) ; **aucune** logique Mandat dans l'agent.
- **INV-MANDATS-3** — Registre et cycle de vie journalisés **append-only en transaction** (mutation + entrée de
  journal atomiques) ; immuabilité par **trigger base** ; **aucun** chemin update/delete sur le journal.
- **INV-MANDATS-4** — `AssujettissementStatus` null **OU** `ContestationDelay` null **OU** mandat non validé/révoqué
  ⇒ autofacturation 389 **SUSPENDUE** ; **aucun** défaut inventé.
- **INV-MANDATS-5** — **Aucune** donnée client réelle dans le code ni les fixtures committées ; exemples fictifs
  uniquement (`config/exemples/`).

## Conséquences

**Positif** : l'isolation tenant est garantie par construction (réutilisation de database-per-tenant + `ICompanyFilter`,
`CLAUDE.md` n°9) ; l'auditabilité **par mandant** est native (le mandat persiste, ses mutations sont append-only) ; on
réutilise **intégralement** des patterns existants (gabarit `TvaMapping` pour le journal append-only en transaction,
`FeeImputationMethod` pour le suspendu-par-défaut, `TenantJobRunner` SOL06 pour les jobs) — **aucun code `Stratum.*`
vendored modifié**, aucune abstraction inventée. Modéliser le mandant en entité de référence (et non en `PivotParty`
jetable) préserve la preuve du mandat.

**À la charge du(des) lot(s) d'implémentation** : créer le module (`Contracts`/`Domain`/`Application`/`Infrastructure`/
`Web` + `MODULE.md`/`INVARIANTS.md`), les migrations DbUp (schéma `mandats` + journaux à double trigger append-only),
les `Contracts` exposés, et les garde-fous testés (NetArchTest de frontière ; tests d'intégration prouvant le rejet
de UPDATE/DELETE/TRUNCATE sur les journaux ; scoping cross-tenant prouvé sur ≥ 2 bases tenant). **Dimensionner la
volumétrie** (dizaines de milliers de mandants par tenant — `F01-F02` R8) dès la conception du registre et de l'import.

**Limite** : cet ADR ne tranche **ni** la numérotation par mandant (BT-1 hybride), **ni** le workflow d'acceptation,
**ni** la capacité B — ce sont des **ADR filles**. Les décisions **fiscales** restent à l'expert-comptable (F15 §6).

## Alternatives rejetées

- **Mandant = sous-tenant** (realm/base dédiée par mandant) : brise `blueprint.md` §7 (*« pas de hiérarchie de
  tenants »*), fragmente l'isolation, multiplie les bases pour des dizaines de milliers de tiers, et casse le
  scoping `company_id`. Un mandant ne paie pas et n'opère pas la plateforme : il n'a pas vocation à être un tenant.
  **Rejetée.**
- **Rattacher le mandant au module `TenantSettings`** : le mandant est une **entité riche** (cycle de vie validé/
  invalidé + numérotation chronologique + workflow d'acceptation + export d'audit), pas un simple réglage. Le
  fondre dans `TenantSettings` diluerait ce module et empêcherait une frontière NetArchTest propre pour
  l'autofacturation. **Rejetée.**
- **Mandant = `PivotParty` enrichi jetable**, reconstruit document après document : duplication, **perte de
  l'auditabilité du mandat** (clause/acceptation/révocation doivent persister et se tracer), et anti-doublon /
  numérotation **par mandant** impossibles sans entité de référence stable. **Rejetée.**

## Références

- `docs/conception/F15-Autofacturation-Mandat.md` (§1 référentiel sourcé, §2 module `Mandats`, §3 BT-1 hybride,
  §4 criée mono-Seller, §6 NON TRANCHÉ)
- `blueprint.md` §2 (généricité, paramétrage tenant jamais en code), §7 (multi-tenancy) ; `CLAUDE.md` n°2/3/6/7/9
- `docs/adr/socle/ADR-0011-database-per-tenant.md` ; `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` ;
  `docs/adr/ADR-0004-perimetre-contrat-extraction-pivot-v1.md` (`Invoicer`/`IsSelfBilled` déjà au pivot)
- Patterns réels imités : `src/Modules/TvaMapping/**` (`MappingTable`, `MappingChangeLog`, double trigger
  append-only) ; `src/Modules/TenantSettings/**` (`FeeImputationMethod`, `AuctionVerticalSettings`) ;
  `src/Modules/Documents/**` (machine à états, `DocumentEvent` append-only)
- **ADR filles à créer** : agrégat d'acceptation + `ISelfBilledGate` ; allocation BT-1 hybride + re-clé F06 §4 ;
  `IPaInboundClient` séparé (capacité B)
