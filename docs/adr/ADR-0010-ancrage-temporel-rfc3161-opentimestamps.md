# ADR-0010 — Ancrage temporel du coffre : RFC 3161 natif par défaut, OpenTimestamps reporté en V1.1 (TRK06)

**Date :** 2026-06-04

**Statut :** Accepté (2026-06-04). Mise en œuvre : item TRK06.

---

## Contexte

Le module `Archive` (TRK05) constitue le coffre WORM 10 ans : chaîne de hashes SHA-256 + addenda
chaînés. TRK06 ajoute le **scellement renforcé** demandé par F06 (amendement 2026-06-02) : un **ancrage
temporel** de la tête de chaîne, qui borne dans le temps toute altération a posteriori — sans lui, une
chaîne de hashes recalculée intégralement par un attaquant resterait cohérente. L'ancrage est le **4ᵉ axe
de généricité enfichable** (après le store, les plug-ins PA, l'IdP) : choisi au niveau **instance**, piloté
par capacités déclarées, jamais par un `if` sur un type concret (CLAUDE.md n°8, blueprint §2 règle 6).

Le backlog v5 considérait l'ancrage comme un point dur (contraintes net48 : pas de RFC 3161 natif). Le
pivot .NET 10 (2026-06-03) LÈVE cette contrainte : `System.Security.Cryptography.Pkcs` fournit
`Rfc3161TimestampRequest` / `Rfc3161TimestampToken` en natif, sans aucune dépendance.

Trois options d'ancrage sont au cahier des charges (TRK.yaml TRK06) :
1. **RFC 3161** — horodatage qualifié eIDAS via une TSA configurable (Certigna/Universign, ~15-50 €/an/instance).
2. **OpenTimestamps** — ancrage blockchain Bitcoin, gratuit, via serveurs calendar publics.
3. **NoAnchor** — instances sans accès internet sortant.

## Décision

### 1. Abstraction `ITimestampAnchor` à capacités (Domain)

Le module ne référence jamais un ancrage concret : il ne voit que `ITimestampAnchor` et ses
`TimestampAnchorCapabilities` (`Method`, `IsOperational`, `ProducesImmediateProof`,
`RequiresOutboundInternet`). Le job d'ancrage et le vérifieur se pilotent par ces capacités. On horodate
les **32 octets bruts du `chain_hash`** de tête (déjà un condensé SHA-256), via `CreateFromHash(..., SHA256)`.

### 2. RFC 3161 = ancrage RECOMMANDÉ, API natives, sans dépendance

`Rfc3161TimestampAnchor` (Infrastructure) construit la requête, l'envoie à la TSA via la couture
`ITsaClient` (HTTP `application/timestamp-query`, testable sans TSA réelle) et conserve le **jeton signé**
comme preuve dans le coffre. La vérification est **100 % hors-ligne** (signature TSA + correspondance de
l'empreinte). La confiance dans la TSA (qualifiée eIDAS) est établie par la **configuration d'instance**
(URL, certificat) — jamais codée en dur, aucun secret versionné (CLAUDE.md n°7/n°10).

### 3. NoAnchor = défaut PROGRAMMATIQUE

Le défaut programmatique est `NoAnchor` : une instance non configurée ne tente **aucun appel sortant**
(air-gapped-safe). L'intégrité reste portée par la chaîne de hashes (blueprint §6). RFC 3161 est
*recommandé* mais activé par configuration (`Archive:Anchor:Method = Rfc3161` + `Rfc3161:TsaUrl`) — un
défaut qui ferait des appels TSA sans TSA configurée serait un mauvais défaut.

### 4. OpenTimestamps REPORTÉ en V1.1

Le protocole `.ots` complet (sérialisation, upgrade calendar, vérification Merkle/Bitcoin) n'a **aucune
bibliothèque .NET mûre et licence-compatible**. Un sous-ensemble maison serait du code cryptographique de
preuve **non vérifiable** dans le cadre d'un produit de conformité fiscale — contraire à la règle « bloquer
plutôt qu'envoyer faux » (CLAUDE.md n°3). Conformément à l'échappatoire prévue par TRK06
(« Si aucune option raisonnable : OpenTimestamps passe en V1.1 »), **OpenTimestamps est reporté en V1.1**.

`OpenTimestampsTimestampAnchor` EXISTE (capacité `Method = OpenTimestamps`, `IsOperational = false`) mais
**lève une `NotSupportedException` française** à l'usage : jamais un no-op silencieux (qui serait un faux
vert). Le job d'ancrage le détecte par sa capacité et ne l'appelle pas. Le plug-in V1.1 sera un fast-follow
**sans modifier le module** (même abstraction).

### 5. Preuves dans le coffre, indexées en base append-only

Les preuves (jetons RFC 3161) sont archivées dans le coffre sous `_anchors/` (write-once) et **indexées**
par la table `documents.archive_anchors` (migration V006), append-only/WORM (mêmes triggers que
`archive_entries`). Le job quotidien est **idempotent** : il ne réancre pas une tête déjà ancrée par la
même méthode (clé `(chain_head_hash, method)`). Le cycle `pending → complete` (colonne `status`) est réservé
à l'ancrage asynchrone d'OpenTimestamps en V1.1 ; RFC 3161 produit directement un `anchored`.

### 6. Job quotidien via la mécanique multi-tenant (SOL06)

L'ancrage quotidien passe par `TenantJobRunner` (SOL06) : un job SYSTÈME (`DailyAnchoringTrigger`) dont le
handler (`DailyAnchoringFanOutHandler`) fait le fan-out sur tous les tenants via `RunForAllTenantsAsync`,
chaque tenant exécutant `DailyAnchoringTenantJob`. Aucun module ne réinvente sa boucle multi-tenant
(module-rules §6). La **planification** du job système (cron) est câblée côté Host (`AddJobHandler` +
JobScheduler) — hors du module.

## Conséquences

- **Généricité respectée.** Le module ne voit que `ITimestampAnchor` + capacités. Azure/GCS de stockage et
  OpenTimestamps d'ancrage sont des plug-ins fast-follow, sans changement du module.
- **Aucune dépendance TIERCE.** RFC 3161 utilise les API in-box `System.Security.Cryptography.Pkcs`,
  référencé comme package .NET **first-party** (support fourni par le runtime, pas une bibliothèque
  externe). La TSA de test ajoute `System.Formats.Asn1` (idem, first-party) au seul projet de tests.
  Ces deux références sont documentées ici (repo-standards §4) ; aucune dépendance OpenTimestamps n'est
  introduite (déférée).
- **Intégrité indépendante du backend et de la TSA.** L'ancrage RENFORCE la chaîne de hashes (la borne dans
  le temps), il ne la remplace pas. Une instance NoAnchor reste intègre (détection d'altération par la chaîne).
- **Vérifiable par l'opérateur.** `IArchiveVerifier` vérifie chaîne + preuves d'ancrage ; l'export contrôle
  fiscal inclut le rapport et une notice de vérification en français (vérifiable avec `openssl ts -verify`).
- **Limite assumée et documentée.** Ce coffre n'est PAS un SAE certifié NF Z42-013 ; l'argument commercial
  est « scellement qualifié eIDAS (RFC 3161) + ancrage blockchain en option », jamais la certification.

## Alternatives écartées

- **Implémenter un sous-ensemble OpenTimestamps maison en V1** — code cryptographique de preuve non
  vérifiable, risque de fausse attestation sur un produit fiscal ; reporté en V1.1 derrière l'abstraction.
- **RFC 3161 comme défaut programmatique** — ferait des appels TSA sortants sur une instance non configurée
  (et air-gapped) ; le défaut sûr est NoAnchor, RFC 3161 activé par configuration.
- **Stocker les preuves uniquement en base (bytea)** — incohérent avec ADR-0009 (le coffre porte les
  pièces) ; les preuves vivent dans le coffre, la base ne fait qu'indexer.
- **Une boucle multi-tenant maison dans le module** — violation de frontière (module-rules §6) ; on passe
  par `TenantJobRunner` (SOL06).
```
