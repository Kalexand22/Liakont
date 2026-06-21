# ADR-0004-bis — Addendum au périmètre du contrat d'extraction et du pivot V1 (reclassements post-redline)

**Date :** 2026-06-20

**Statut :** Accepté (2026-06-20) — addendum de l'[ADR-0004](ADR-0004-perimetre-contrat-extraction-pivot-v1.md), qu'il **complète sans le remplacer**.

---

## Contexte

L'ADR-0004 a été re-passé en revue (redline demandé par Karl, 2026-06-19 ; source détaillée :
`tasks/redline-adr-0004.md`, 19 findings → 12 confirmés, 6 ajustés, 1 réfuté). Le **constat
transversal** est que le *contrat pivot* (le modèle de données) honore bien l'ADR, mais que certaines
**décisions de l'ADR étaient sur- ou sous-calibrées au regard de l'implémentation réelle** : des
décisions notées « Pivot V1 P1 » se révèlent **non sourçables** aujourd'hui, et des « différés » sont
en fait **tenus au contrat** (slot présent, inerte) plutôt qu'absents.

Cet addendum **reclasse** ces décisions à la lumière du code livré, **sans rien inventer** (CLAUDE.md
n°2 : aucune catégorie/VATEX/seuil/association sans source dans `docs/conception/` ou table tenant
validée par l'expert-comptable ; CLAUDE.md n°3 : bloquer plutôt qu'envoyer faux). Il **ne crée aucune
logique** : il met l'ADR, les specs (F01-F02, F03) et le code en cohérence, et **trace** les différés
pour qu'un futur connecteur soit un *ajout*, jamais une *rupture*.

Items du lot RD4 déjà livrés et référencés ici : RD401 (transport `ExtractorCapabilities`),
RD402 (gate « document finalisé »), RD403 (premiers consommateurs de capacités),
RD404 (cohérence auto-facturation + sort de `Payee`), RD406 (slots réservés des différés),
RD407 (`UnitCode` BT-130), RD408 (verrou EUR-only). Le présent addendum est l'item **RD409**.

---

## Décision — reclassements

### R1 — D3-1 (collection de régimes par ligne) : tenue au contrat ; scission BG-30 = différé tracé

- **Tenu (V1)** : la ligne pivot porte une **collection** de codes régime source bruts —
  `PivotLineDto.SourceRegimeCodes : IReadOnlyList<string>` (et non une simple chaîne). Une source peut
  encoder un **couple de codes** (NAV) ou **plusieurs taxes sur une même ligne** (Axelor) ;
  l'adaptateur passe la collection **brute**, sans jamais interpréter (CLAUDE.md n°2).
- **Différé tracé** : la **scission BG-30** (EN 16931 : exactement 1 catégorie de TVA par ligne pivot)
  d'une ligne multi-codes/multi-taxes **n'est pas « V1 »**. L'**association** d'un code régime à une
  ventilation TVA particulière n'est **pas sourcée** pour ce cas. Le moteur de CHECK **bloque
  délibérément** toute ligne hors forme NON AMBIGUË (exactement 1 code régime ET 1 ventilation), avec un
  motif explicite, **sans jamais deviner** (CLAUDE.md n°2/n°3).
  - Implémentation et test : `Liakont.Modules.Pipeline.Infrastructure.Check.CheckTvaMapping`
    (blocage de forme lignes 53-61 ; doc de classe lignes 25-30) + `CheckTvaMappingTests`.
  - Les documents réels du contrat (golden files contrat-v1) sont **tous** de la forme non ambiguë.
- **Reclassement de gravité** : RD4-02 (« scission BG-30 non implémentée ») était soupçonné P1 ;
  **dégonflé en P3** par la vérification, car le moteur bloque au lieu de mapper faux — aucune donnée
  fiscale fausse n'est produite. La scission sera implémentée **quand F03 sourcera** l'association
  régime→ventilation (table validée par l'expert-comptable du tenant, jamais une heuristique).
- Spec alignée : F01-F02 §3.3 + §4.2 R3 (collection) et F03 §4.1 bis (différé tracé).

### R2 — Débours (art. 267) : réconciliation ADR ↔ F15 → hors V1 tant qu'aucune F-spec ne tranche

- **Contradiction constatée** (RD4-05) : l'ADR-0004 classe « Débours (art. 267) » en **Pivot V1 P1**
  (ligne 133, Famille 1), alors que **F15 §… (l.375)** range les débours (267 II-2°) parmi les
  « verticales **non encore sourcées** … à ancrer dans une **F-spec avant tout code** (CLAUDE.md n°2) ».
- **Reclassement retenu** : les débours sont **hors V1 tant qu'aucune F-spec ne les source**. C'est
  F15 (« F-spec avant tout code ») qui **prime** sur la classification optimiste de l'ADR : décider
  une projection de débours sans source serait inventer une règle fiscale. Une source qui présente un
  débours est **bloquée** en Validation/CHECK (chemin sûr par défaut), **jamais mappée silencieusement**
  en catégorie « O » (Hors champ). La ligne 133 de l'ADR-0004 est donc à lire **amendée par cet
  addendum** : « débours = différé sourçable, F-spec avant tout code », pas « V1 maintenant ».

### R3 — Marge / Mixte : limite V1 actée ; dérivation adjudication/frais ouverte et non sourcée

- **Acté (limite V1)** : le mapping marge/Mixte n'est exerçable que si la **source est déjà éclatée**
  en 2 lignes (adjudication / frais). Le pivot générique ne porte **aucun découpage adjudication/frais**
  (cf. `CheckTvaMapping`, doc de classe lignes 15-23 : `Part = Autre`, décision tracée).
- **Ouvert (non sourcé)** : la **dérivation** automatique de la part adjudication/frais depuis une ligne
  pivot est une décision fiscale **OUVERTE et non sourcée** (F03 §2.3). Elle relèvera d'une **table
  tenant validée par l'expert-comptable**, **jamais d'une heuristique** côté adaptateur ou moteur
  (CLAUDE.md n°2). En attendant, un régime/part absent de la table validée **bloque** le document
  (`defaultBehavior=block`, F03 §4.1).
- Renvois : EUR-only → **RD408** ; slots réservés des différés → **RD406**.

### R4 — `Payee` (affacturage BG-10) : champ contractuel hashé mais INERTE en V1 (différé explicite)

Tranché par **RD404** et gravé ici (CLAUDE.md n°2 : ne pas inventer une projection d'affacturage) :

- `Payee` (EN 16931 BG-10) est un **champ contractuel** du pivot (`PivotDocumentDto.Payee`) **et
  participe à l'empreinte canonique** (hashé) — la donnée n'est pas perdue.
- Il est **INERTE en V1** : **aucun sérialiseur PA** ne le projette (la projection affacturage
  393/396 n'est pas sourcée — F15 §6.5/§6).
- Sa présence est **SIGNALÉE à l'opérateur** par la règle de Validation `PartyRoleConsistencyRule`
  (code `PAYEE_NOT_TRANSMITTED`, sévérité **Warning** — jamais bloquant, jamais inventé). L'opérateur
  est ainsi averti que l'information n'est pas transmise, plutôt qu'une projection fausse soit produite.
- **Différé** : la projection affacturage sera câblée quand une **F-spec** sourcera la correspondance
  (393/396) — pas avant.

### R5 — Différés « capacité » : à câbler quand la source concernée arrivera (pas avant)

Capacités déclarées par l'agent (`ExtractorCapabilities`, transportées et persistées par RD401) mais
**volontairement non consommées en V1** faute de source réelle qui les exerce. Tracées ici pour que le
câblage soit un *ajout* dirigé par la capacité déclarée (CLAUDE.md n°8), jamais un `if (source is …)` :

| Capacité (différé) | Finding | À câbler quand… | Action de câblage (future) |
|---|---|---|---|
| `HasDetailedLines` | RD4-08 | une source à **entête mono-ligne** (type criée) arrive | générer des **lignes synthétiques par taux** (description dérivée), conformément à F03 |
| `EmitterIdentitySource.DerivedFromVatNumber` | RD4-10 | une source **sans SIREN** mais avec n° TVA arrive | **normalisation TVA→SIREN** dans la couche d'adaptation (Conséquence §3 de l'ADR), jamais côté moteur |
| `NumberUniquenessScope` | RD4-12 | une source à **numérotation multi-établissement/série/reset annuel** arrive | **clé d'idempotence composite** (au lieu du n° global) pour l'anti-doublon F06 |

> `IsMutableAfterIssue` (RD4-12) et `ExposesPayments` ont leurs **premiers consommateurs livrés par
> RD403** (alerte d'altération ; conditionnement du flux paiement F09) — ils ne sont donc **pas** des
> différés ; ils figurent ici uniquement pour mémoire du cluster RD4-12.

### R6 — Veille réglementaire DGFiP v3.2 : verdict « delta minime » provisoire (action humaine planifiée)

- **Finding RD4-19** : le verdict « delta v3.1 → v3.2 **minime** » de
  `docs/references/dgfip-v3.2/LECTURE-LIAKONT.md` a été établi sur le **seul changelog XSD**, qui ne
  couvre **que les schémas**. L'**Annexe 7 « Règles de gestion » V1.9** et le **Dossier général v3.2**
  (texte) **n'ont pas été croisés** avec F04.
- **Décision** : le verdict reste **PROVISOIRE**. Action **HUMAINE planifiée et tracée** dans
  `LECTURE-LIAKONT.md` (§ « Actions restantes » + caveat sous le verdict) : croiser **règle par règle**
  l'Annexe 7 V1.9 avec les contrôles F04 (lot VAL), et lire le Dossier général pour les évolutions de
  **texte**. Tant que ces cases ne sont pas cochées, **ne pas requalifier** le delta en
  « définitivement minime » sur le seul changelog XSD.

---

## Note de cohérence — adaptateurs de démonstration ↔ panel ADR

**Finding RD4-18** (aucun changement de code) : les adaptateurs de démonstration `DemoErpA` / `DemoErpB`
**ne correspondent pas lettre pour lettre** au panel de validation de l'ADR-0004 (« Source A/B/C/D ») —
ce sont **deux jeux indépendants**. En particulier, l'ADR nomme « **Source A** » la source aux montants
**`float`**, mais côté démo c'est **`DemoErpB`** qui porte les montants `double` legacy (+ leur
conservation brute dans `SourceData`), `DemoErpA` étant un ERP **normalisé** en `decimal`. Une note
d'inversion a été ajoutée aux XML-doc (`<remarks>`) des deux extracteurs pour éviter la confusion de
lecture. Aucune logique modifiée.

---

## Conséquences

1. **Cohérence ADR ↔ spec ↔ code rétablie** sur les 7 points de cohérence du redline (RD4-02, RD4-05,
   RD4-08, RD4-10, RD4-15, RD4-17, RD4-18) + le sort de `Payee` (RD404), sans introduire de logique ni
   de règle fiscale.
2. **Aucune règle inventée, aucun affaiblissement de validation** : tous les cas non sourcés
   (scission BG-30, débours, dérivation adjudication/frais, projection affacturage) **bloquent ou
   signalent**, jamais ne devinent. Les différés sont **réservés et nommés**, pas oubliés (Conséquence
   §5 de l'ADR-0004, tenue ici et par RD406).
3. **Coût de câblage futur = ajout dirigé par capacité**, pas refactor du socle : chaque différé
   capacité (R5) a son point d'entrée déclaré et son action de câblage tracée.
4. **EUR-only confirmé** (RD408) : aucune gestion multi-devises ajoutée (décision Karl 2026-06-19).

---

## Références

- [ADR-0004 — Périmètre du contrat d'extraction et du pivot V1](ADR-0004-perimetre-contrat-extraction-pivot-v1.md) (décision mère)
- `tasks/redline-adr-0004.md` (findings + verdicts de vérification adversariale)
- `docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md` (§3.3, §4.2 R3 — collection alignée)
- `docs/conception/F03-Mapping-TVA.md` (§4.1 bis — scission BG-30 différée)
- `docs/conception/F15-Autofacturation-Mandat.md` (§6.5/§6, l.375 — débours/affacturage non sourcés)
- `docs/references/dgfip-v3.2/LECTURE-LIAKONT.md` (veille Annexe 7 V1.9 — action humaine tracée)
- `src/Modules/Pipeline/Infrastructure/Check/CheckTvaMapping.cs` (blocage de forme V1)
- `src/Modules/Validation/Domain/Rules/PartyRoleConsistencyRule.cs` (`PAYEE_NOT_TRANSMITTED`)
- `src/Contracts/Liakont.Agent.Contracts/Pivot/PivotLineDto.cs` (`SourceRegimeCodes`, `UnitCode`)
- `blueprint.md` §2/§6/§8 (frontières de la généricité, capacités déclarées)
