# ADR-0008 — Stockage des PDF reçus par l'ingestion (PIV04)

**Date :** 2026-06-04

**Statut :** Accepté (2026-06-04)

---

## Contexte

L'agent transmet à la plateforme, en plus des documents pivot, les **PDF** correspondants (F12 §3.2,
PIV04), via deux endpoints de l'API agent :

| Endpoint | Rôle |
|---|---|
| `POST /api/agent/v1/documents/{sourceReference}/pdf` | PDF **rattaché** à un document (capacité source `ProvidesSourceDocuments`) |
| `POST /api/agent/v1/pdf-pool` | PDF **non rattachés** déposés en vrac → **pool de réconciliation** (F06 / TRK07) |

Trois contraintes encadrent ce stockage :

1. **Pas de dépendance au module Document du socle Stratum.** Ce module n'est PAS vendoré ; ce n'est
   pas une option (description PIV04, `module-rules.md` §10). L'ingestion ne doit dépendre que d'une
   abstraction propre.
2. **Isolation par tenant** (`blueprint.md` §7, `CLAUDE.md` n°9). Les fichiers d'un tenant ne doivent
   jamais se mélanger à ceux d'un autre.
3. **Les PDF sont volumineux.** Le batch de documents pivot reste léger ; les PDF transitent par des
   endpoints séparés (F12 §6, décision « transport des PDF = endpoint séparé »).

Les PDF reçus ne sont **pas** le coffre d'archive WORM (`Archive`, lot ultérieur) : ce sont des
fichiers de travail en attente de rattachement/traitement. Le WORM et l'intégrité chaînée relèvent du
module `Archive`, hors périmètre PIV04.

## Décision

L'ingestion stocke les PDF derrière l'abstraction **`IIngestedPdfStore`** (surface `Contracts` du
module Ingestion — consommée directement par le Host comme `IAgentAuthenticator`, car les endpoints
streament le corps de la requête et un `Stream` ne se modélise pas en commande MediatR). L'implémentation
V1 est **`FileSystemIngestedPdfStore`** : système de fichiers,
sous une **racine de déploiement** paramétrable (`Ingestion:Storage:PdfRootPath`, jamais une donnée
client en dur — `CLAUDE.md` n°7 ; repli sous le content root de l'instance si non configurée).

### Organisation des fichiers

```
{racine}/
  {tenant}/                         ← slug de tenant assaini (anti path-traversal)
    linked/
      {sha256(sourceReference)}.pdf ← PDF rattaché, adressable de façon déterministe
    pool/
      {guid}__{nomFichier}          ← PDF non rattaché (réconciliation), chaque dépôt distinct
```

- **PDF rattaché** : le nom de fichier est l'empreinte **SHA-256** de la `sourceReference` (hex
  minuscule) — déterministe, sans caractère de chemin dangereux, et **ré-adressable** par toute
  brique aval (module Documents/TRK02, réconciliation) à partir de la seule `sourceReference`. Un
  re-push du même document **écrase** l'entrée (idempotent — le PDF d'un document est un fait stable ;
  le coffre WORM, lui, est ailleurs).
- **PDF de pool** : préfixé d'un **GUID** pour conserver **chaque dépôt distinctement** (jamais
  d'écrasement), le nom d'origine étant assaini et conservé en suffixe pour la lisibilité. La
  réconciliation (F06 / TRK07) **découvre** le pool en énumérant `{tenant}/pool/` — pas besoin d'une
  table de métadonnées en V1 ; le système de fichiers EST le registre du pool.

### Assainissement

- Le **slug de tenant** et le **nom de fichier** sont assainis : seuls `[A-Za-z0-9-_.]` sont conservés,
  tout autre caractère devient `_`. Le nom de fichier de pool est réduit à son nom de base
  (`Path.GetFileName`) avant assainissement — aucun segment de chemin fourni par l'agent n'est honoré
  (anti path-traversal).

## Conséquences

- **Générique / enfichable.** Un backend objet (S3-compatible, Azure/GCS) se branche derrière la même
  abstraction `IIngestedPdfStore` sans toucher aux endpoints ni aux appelants — cohérent avec la
  généricité du stockage d'archive (`module-rules.md` §5). Aucun `if (store is FileSystem)`.
- **Pas de métadonnées en base en V1.** Les PDF rattachés sont adressables par `sourceReference` ; le
  pool est énuméré par répertoire. Si un besoin de métadonnées riches apparaît (date de dépôt,
  empreinte, statut de réconciliation), il sera ajouté par le lot de réconciliation (TRK07/TRK08) —
  pas anticipé ici (YAGNI).
- **OPS.** Le chemin racine est un paramètre d'exploitation par instance (volume dédié, sauvegarde,
  rétention). À documenter dans le toolkit de déploiement (lot OPS/DOC).
- **Limite de taille de requête.** La taille des PDF est bornée par la limite de corps de requête de
  l'hôte (Kestrel, défaut 30 Mo) ; un ajustement éventuel relève d'OPS, pas du produit.

## Alternatives écartées

- **Vendoriser le module Document du socle Stratum** — explicitement exclu (description PIV04).
- **Stocker les PDF en base (bytea/large object)** — gonfle la base, complique la sauvegarde et le
  streaming ; le système de fichiers (ou un store objet) est le bon support pour des binaires volumineux.
- **Multipart dans le batch de documents** — alourdit le batch (qui doit rester léger) ; endpoints PDF
  séparés retenus (F12 §6).
