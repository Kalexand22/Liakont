# ADR-0007 — Sérialisation canonique du pivot et empreinte de payload (PIV02)

**Date :** 2026-06-04

**Statut :** Accepté (2026-06-04)

---

## Contexte

Le contrat agent↔plateforme (`Liakont.Agent.Contracts`, netstandard2.0) est consommé par DEUX
runtimes : l'**agent** en **.NET Framework 4.8** (où Newtonsoft.Json est disponible) et la
**plateforme** en **.NET 10** (où System.Text.Json est le sérialiseur natif). Le pivot est hashé
(SHA-256) pour deux usages fiscaux critiques :

- **anti-doublon par tenant** (PIV04) — un même document re-poussé doit produire la MÊME empreinte ;
- **détection d'altération de la source** (TRK03) — une même `SourceReference` avec une empreinte
  différente est une altération à signaler.

Si l'agent et la plateforme calculaient des empreintes différentes pour un même document, l'agent ne
pourrait plus dédoublonner localement et la plateforme verrait des « faux nouveaux » documents : le
mécanisme d'idempotence s'effondre. **L'empreinte doit donc être identique octet par octet des deux
côtés.**

Newtonsoft.Json et System.Text.Json, même « configurés pareil », **divergent** sur exactement les
points qui changent les octets :

| Point | Newtonsoft (défaut) | System.Text.Json (défaut) |
|---|---|---|
| Ordre des propriétés | ordre de réflexion | ordre de réflexion (peut différer) |
| Échappement non-ASCII | **non échappé** (UTF-8 brut) | échappé, mais jeu de caractères et casse hex propres |
| `decimal` | `1.50` peut perdre l'échelle | idem, règles propres ; exposant possible sur `double` |
| Dates | `DateTime` sérialisé avec heure/fuseau | format ISO avec heure/fuseau |
| Espacement | indentation optionnelle | compact par défaut |

## Décision

**Un UNIQUE writer JSON manuel** (`CanonicalJsonWriter` + `CanonicalJson`) vit dans
`Liakont.Agent.Contracts` (netstandard2.0, **zéro PackageReference**). Le MÊME code est compilé des
deux côtés ⇒ la sortie est identique **par construction**, jamais « deux sérialiseurs configurés
pareil » qui dériveraient à la première montée de version d'une lib. `PayloadHasher` calcule
SHA-256 sur les octets UTF-8 (= ASCII) de ce JSON.

Ce sont des **utilitaires de sérialisation du contrat**, pas de la logique métier : la règle « DTOs
purs » (PIV01) interdit la logique MÉTIER (TVA, validation, états), pas un sérialiseur — même raison
d'être que `PivotRounding`. Aucune règle fiscale n'est portée ici.

### Règles de format FIGÉES (v1 du contrat)

1. **Ordre des membres = ordre de DÉCLARATION du DTO.** En v1, un champ s'AJOUTE en fin, ne se
   renomme/supprime jamais (cf. `AgentContractVersion.ContractVersion` ; toute rupture = v2).
2. **Noms de membres = noms de propriété C# (PascalCase).** Le JSON canonique est la base du HASH,
   distinct du format de transport HTTP (qui peut être camelCase, géré par ASP.NET en PIV04/PIV05).
3. **Champ optionnel `null` → OMIS** (jamais émis à `null`). Aucune occurrence de `null` dans la
   sortie. Une **collection est toujours émise**, même vide (`[]`).
4. **Énumérations émises par leur NOM** (`"E"`, `"AE"`, `"Mixte"`), jamais par valeur numérique
   (la valeur numérique pourrait changer ; le nom est la sémantique — code UNCL5305 pour `VatCategory`).
5. **`decimal` en culture INVARIANTE**, séparateur `.`, **échelle de la source PRÉSERVÉE**
   (`10.00m` → `10.00` ; `1234.5m` → `1234.5` ; `0m` → `0`), **jamais de notation exponentielle**
   (garanti par le type `decimal` ; vérifié par test). Aucun montant n'est en float/double (CLAUDE.md n°1).
6. **Dates : format unique `yyyy-MM-dd`** en culture invariante — **invariant « date calendaire »** :
   seules les composantes Year/Month/Day sont émises, le `DateTimeKind` (Utc/Local/Unspecified),
   l'heure et le fuseau sont **ignorés** (aucune conversion de fuseau). Deux `DateTime` de même date
   calendaire mais de `Kind` différent produisent donc le MÊME octet (vérifié par test). Les champs du
   pivot sont des dates (émission, paiement, référence) ; un adaptateur ne doit pas dériver une date du
   pivot d'un `DateTimeOffset.ToLocalTime()` qui décalerait la composante calendaire près de minuit
   (responsabilité de déterminisme de la source, cf. §traçabilité). Les horodatages UTC des enveloppes
   de transport (hors périmètre PIV02) utiliseront `yyyy-MM-ddTHH:mm:ssZ`.
7. **Chaînes (texte LIBRE) : normalisation Unicode NFC, puis sortie ASCII PUR.** Toute valeur de texte
   libre (raison sociale, libellé, `SourceData`…) est d'abord **normalisée en NFC**
   (`String.Normalize(NormalizationForm.FormC)`) : « café » précomposé (U+00E9) et décomposé
   (U+0065 U+0301) sont la MÊME chaîne abstraite (équivalence canonique Unicode) et doivent produire la
   MÊME empreinte — sinon une source ODBC renvoyant tantôt NFC tantôt NFD romprait l'anti-doublon (PIV04).
   La forme NFC est **stable entre net48 et .NET 10** : la *Unicode Normalization Stability Policy* garantit
   que la décomposition canonique d'un caractère **assigné** ne change jamais d'une version d'Unicode à
   l'autre — l'empreinte reste donc identique des deux côtés. C'est **ancré par des tests NFC≡NFD exécutés
   des DEUX côtés** (`CanonicalDeterminismTests`, lié net48 + .NET 10) : un cas Latin-1 (« café ») ET une
   syllabe Hangul HORS Latin-1 (U+AC00 ≡ U+1100 U+1161), pour ne pas réduire la preuve à une garantie de
   « policy » sur le seul Latin-1 ; les golden à empreinte figée complètent la couverture cross-runtime. La
   normalisation est portée par le **seul** `WriteString` (texte libre) : noms de membres, dates et noms
   d'énum sont du texte CONTRÔLÉ (ASCII) pour lequel NFC est un no-op. Ensuite, **sortie ASCII pur** : tout
   caractère `< 0x20` ou `> 0x7E` est échappé en `\uXXXX` **hexadécimal minuscule** ; `"` → `\"` et
   `\` → `\\` ; les contrôles usuels utilisent `\b \f \n \r \t`. C'est le point qui fait le plus diverger
   Newtonsoft (pas d'échappement non-ASCII) et STJ. Une chaîne Unicode **mal formée** (surrogate isolé) ne peut pas être normalisée — `String.Normalize` lève ; on préserve alors l'échappement code-unité déterministe antérieur (aucun nouveau rejet introduit, hors périmètre).
8. **Sortie compacte** : aucun espace ni saut de ligne hors des chaînes.
9. **Empreinte** : SHA-256 des octets UTF-8 (identiques aux octets ASCII) → **hexadécimal minuscule
   de 64 caractères**.

## Conséquences

- **Tests golden des DEUX côtés** (`PivotContractGoldenTests`, fichier lié dans la solution
  plateforme ET la solution agent) : un avoir complet (lignes, taxes, références, paiements, charges,
  montant négatif, caractères non-ASCII) sérialisé doit produire la MÊME empreinte figée. Toute
  divergence runtime — ou régression de format — casse le test. PIV03 étendra à un jeu de fixtures.
- **Round-trip sans perte** prouvé par un lecteur canonique de test (lié des deux côtés) : `désérialiser(sérialiser(doc))`
  re-sérialise à l'identique. Le contrat lui-même n'a besoin que du writer + du hasher.
- **Un lecteur canonique vit AUSSI en production** (amendement 2026-06-19, RDL02) : `PivotCanonicalJsonReader`
  (module `Pipeline`, .NET 10) relit le pivot canonique depuis le magasin de staging (PIP00) pour le SEND
  (`SendTenantJob`). Il est un **miroir STRICT du writer, au champ près** — le round-trip
  `Serialize(Read(json)) == json` est garanti octet par octet (INV-PIPELINE-001/002). Il **NE RE-SÉRIALISE
  JAMAIS pour ré-hacher** : la re-vérification du `payload_hash` porte sur la **string brute** lue du staging
  (`IPayloadStagingStore.ReadAsync`), pas sur une re-sérialisation. Conséquence : writer, lecteur de test et
  lecteur prod sont **trois artefacts à garder synchronisés au champ près**. Cette synchronisation est
  verrouillée par des **gardes de complétude par RÉFLEXION** sur un document entièrement peuplé — côté writer
  (`CanonicalJsonRulesTests`) et côté lecteur prod (`PivotCanonicalJsonReaderTests`, RDL02) : un champ pivot
  ajouté mais oublié dans le lecteur prod serait amputé avant transmission PA (EXT01/BT-9 l'a frôlé), et fait
  désormais échouer la garde.
- Le **format canonique est la base du HASH**, pas nécessairement le format de la requête HTTP. PIV04
  recalcule l'empreinte côté plateforme à partir du DTO reçu (et non du JSON HTTP brut) — la plateforme
  **re-désérialise les octets du fil via System.Text.Json** puis re-sérialise canoniquement et hashe le DTO
  (`AgentApiEndpoints` → `IngestDocumentBatchHandler`). L'axe `wire→STJ→writer→hash` est ancré par un test
  Host net10 sur le jeu de golden contrat-v1 (RDL02).

### Champs de traçabilité dans l'empreinte (`SourceData`, `SourceReference`)

L'empreinte porte sur le **pivot ENTIER**, y compris `SourceData` (données source brutes) et
`SourceReference` — c'est la décision de F01-F02 §3.7.4 (« chaque objet pivot est sérialisable en
JSON tel quel → c'est ce JSON qui est hashé pour l'anti-doublon et archivé pour la piste d'audit »).
Conséquences voulues :

- **Détection d'altération (TRK03)** : inclure `SourceData` est un atout — une modification de la
  source après envoi change l'empreinte, donc se signale (F01-F02 §6, « document modifié dans la
  source APRÈS envoi → détecté par le hash »).
- **Anti-doublon (PIV04)** : la reconnaissance d'un re-push repose sur le fait que **deux extractions
  du même document produisent les MÊMES octets** — c'est l'obligation d'**idempotence de l'adaptateur
  (R2, F01-F02 §4.2)** : « deux extractions de la même période retournent les mêmes documents ».
  `SourceData` doit donc être déterministe pour une ligne source donnée (pas d'horodatage
  d'extraction ni d'ordre de champ instable). Cette stabilité est une **responsabilité de
  l'adaptateur** (lot ADP), pas du sérialiseur : PIV02 hashe fidèlement le payload défini par le
  contrat, sans en exclure de champ.

  **Précision (amendement 2026-06-19, RDL05) — encodage vs contenu.** Il faut distinguer deux niveaux
  de déterminisme, qui n'entrent pas en conflit : (a) le déterminisme de **contenu/structure** —
  *quels* champs, *quelles* valeurs, *quel* ordre, pas d'horodatage d'extraction — relève de
  l'adaptateur (ci-dessus) ; (b) la canonicalisation de la **forme d'encodage** d'une chaîne donnée —
  échappement ASCII (règle 7) ET **normalisation Unicode NFC** — relève du writer, car elle est
  transverse et identique pour toute chaîne. Normaliser en NFC dans le writer ne « transforme » pas le
  contenu de la source au sens de la règle n°2 (CLAUDE.md) : NFC et NFD sont la **même chaîne abstraite**
  (équivalence canonique), exactement comme `é` et la lettre `é` désignent le même caractère. Ce
  n'est donc PAS une dérogation à F01-F02 §3.7.4 (aucun champ n'est exclu ni altéré sémantiquement),
  mais la garantie que deux représentations d'octets canoniquement équivalentes ne cassent ni
  l'anti-doublon (PIV04) ni la détection d'altération (TRK03). L'adaptateur reste libre de garantir NFC
  en amont (R2) ; le writer rend cette garantie **inconditionnelle et impossible à oublier** sur tout
  champ de texte libre présent ou futur.

Exclure des champs de l'empreinte serait une **dérogation à F01-F02 §3.7.4** : non décidée ici (pas
de source). Si une source réelle s'avérait incapable de produire un `SourceData` déterministe, le
traitement relèverait d'une décision sourcée au niveau de l'adaptateur concerné (ADP), pas d'un
contournement silencieux dans la sérialisation du contrat (CLAUDE.md n°2).

## Alternatives écartées

- **Deux sérialiseurs (Newtonsoft + STJ) « configurés à l'identique »** : c'est précisément le piège
  — la moindre montée de version d'une lib, ou un défaut de configuration, casse silencieusement
  l'idempotence. Rejeté.
- **RFC 8785 (JSON Canonicalization Scheme)** : standard reconnu, mais (a) il sérialise les nombres
  en `Number` ECMAScript (IEEE-754 double) — incompatible avec nos montants `decimal` à échelle
  préservée — et (b) il produit de l'UTF-8 avec échappement MINIMAL, alors que la réforme impose
  ici une sortie ASCII robuste. On reprend son principe (canonicalisation déterministe) avec des
  règles propres au domaine fiscal, figées ci-dessus.

## Références

- `docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md` §3.7 (montants decimal, JSON hashé).
- `docs/conception/F12-Architecture-Plateforme-Agent.md` §3.4 (contrat agent, golden files), §6.4
  (matrice de compatibilité N-1).
- `docs/architecture/mapping-pivot-en16931.md` (couverture EN 16931 / Annexe 6 DGFiP v3.2).
- `src/Contracts/Liakont.Agent.Contracts/Serialization/` (`CanonicalJsonWriter`, `CanonicalJson`,
  `PayloadHasher`) ; `tests/_shared/contract-v1/` (golden + lecteur, liés des deux côtés).
