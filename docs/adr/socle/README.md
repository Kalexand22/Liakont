# ADR du socle Stratum vendored (référence héritée)

Ce dossier contient une **copie** d'un sous-ensemble curé des ADR (Architecture Decision
Records) du dépôt **Stratum ERP**, dont Liakont vendorise le socle (`Stratum.Common.*` +
modules `Identity`, `Job`, `Notification`, `Audit`). Voir la règle du socle vendored dans
`blueprint.md` §4 et la provenance dans `docs/architecture/provenance-socle-stratum.md`.

## Statut de ces ADR

Ces ADR sont **hérités du socle, pas re-décidés par Liakont**. Ils sont conservés ici à
titre de **référence** : ils documentent les décisions d'architecture qui sous-tendent le
code vendored et que Liakont reprend telles quelles. Liakont ne les rouvre pas ; s'il devait
diverger d'une de ces décisions, il le ferait par un ADR **propre** (voir ci-dessous), sans
modifier les fichiers de ce dossier.

> Seuls les ADR pertinents pour le périmètre vendored (Common + Identity + Job + Notification
> + Audit) ont été copiés. Les ADR purement ERP de Stratum (axe-core, QuillJS, éditeur de code,
> billing/Stripe, moteur de réservation, civic blueprint) ne concernent aucun module vendored
> et sont **volontairement exclus**.

## Résolution de la collision du n°0010

Le dépôt Stratum contient **deux** fichiers portant le numéro `ADR-0010` (collision historique) :

| Fichier Stratum | Sujet | Sort dans Liakont |
|---|---|---|
| `ADR-0010-github-issue-reporter.md` | `HttpClient` pour la création d'issues GitHub (BugCapture) — vivant | ✅ **Copié** (seul porteur du n°0010) |
| `ADR-0010-multi-tenant-strategy.md` | Multi-tenant *schema-per-tenant* — **superseded** par ADR-0011 | ❌ **Écarté** |

La collision est résolue en **n'écartant que l'ADR superseded** : l'ADR multi-tenant
*schema-per-tenant* a été remplacé par `ADR-0011-database-per-tenant.md` (database-per-tenant,
l'isolation physique par tenant retenue). On ne conserve donc que l'ADR 0010 **vivant**
(`ADR-0010-github-issue-reporter`), qui devient l'unique porteur du n°0010 dans ce dossier.

**Toute référence à la stratégie multi-tenant pointe vers `ADR-0011-database-per-tenant.md`**
(et non vers l'ancien `ADR-0010-multi-tenant-strategy`). C'est aussi le choix repris par
Liakont (1 tenant = 1 client final, isolation physique par base — voir `blueprint.md` §7).

## ADR copiés (11 fichiers)

| ADR | Sujet | Pertinence socle |
|---|---|---|
| `ADR-0001-architecture-base.md` | Monolithe modulaire strict, isolation par Contracts, .NET 10, Dapper, MediatR, outbox | Fondation du pattern de tous les modules |
| `ADR-0002-frontend-strategy.md` | Stratégie front (Blazor Server) | UI du socle (`Common.UI`) |
| `ADR-0003-radzen-ui.md` | Bibliothèque de composants Radzen | UI du socle |
| `ADR-0004-questpdf.md` | Génération PDF (QuestPDF) | Rendu documentaire du socle |
| `ADR-0008-api-versioning.md` | Versionnement d'API | API du socle / Host |
| `ADR-0009-openapi-swagger.md` | OpenAPI / Swagger | API du socle / Host |
| `ADR-0010-github-issue-reporter.md` | `HttpClient` pour issues GitHub (BugCapture) | `Common.Infrastructure` |
| `ADR-0011-database-per-tenant.md` | Isolation physique par tenant (database-per-tenant) | Multi-tenancy du socle (supersede l'ancien 0010) |
| `ADR-0012-action-pipeline.md` | Pipeline d'actions | Pattern d'exécution du socle |
| `ADR-0013-keycloak-identity-provider.md` | Keycloak comme fournisseur d'identité (OIDC) | Module `Identity` vendored |
| `ADR-0016-nettopologysuite-gis.md` | NetTopologySuite (types GIS) | Type de données du socle (`Common`) |

## Numérotation ADR : socle vs. Liakont

- Les ADR **PROPRES à Liakont** (décisions d'architecture du produit) vivent dans
  `docs/adr/` (racine), avec leur **propre numérotation** repartant de `ADR-0001`
  (voir `docs/adr/ADR-0001-pivot-plateforme-agent.md`).
- Les ADR **du socle vendored** vivent ici, dans `docs/adr/socle/`, et **conservent la
  numérotation Stratum d'origine**.

Les deux numérotations sont **distinctes** et ne doivent pas être confondues : un `ADR-0001`
dans `docs/adr/` (le pivot plateforme/agent de Liakont) n'a aucun rapport avec le
`ADR-0001` de `docs/adr/socle/` (l'architecture de base de Stratum).

### Collisions de numéro **cross-dossier** (socle vs Liakont) — homonymes, pas des doublons

Parce que les deux numérotations sont indépendantes, **un même numéro existe des deux côtés** et désigne
des décisions **sans aucun rapport**. Ce ne sont **pas** des doublons à résoudre (contrairement à la
collision intra-Stratum du n°0010 ci-dessus) : chaque fichier est l'unique porteur de son numéro **dans son
dossier**. À nommer explicitement pour lever toute ambiguïté de référence :

| Numéro | `docs/adr/socle/` (Stratum hérité) | `docs/adr/` (Liakont propre) |
|---|---|---|
| **0011** | `ADR-0011-database-per-tenant.md` (isolation physique par tenant) | `ADR-0011-ancrage-temporel-rfc3161-opentimestamps.md` (ancrage temporel) |
| **0013** | `ADR-0013-keycloak-identity-provider.md` (IdP OIDC du socle) | `ADR-0013-modele-confiance-auto-update-agent.md` (auto-update agent) |

**Règle de référence** : toujours **préfixer le chemin** (`socle/ADR-0011…` vs `ADR-0011…` à la racine)
quand le contexte ne lève pas l'ambiguïté. Un `ADR-0011` **non préfixé** désigne, par convention, l'ADR
**Liakont** de `docs/adr/` ; l'ADR socle se cite toujours `docs/adr/socle/ADR-0011-database-per-tenant.md`.
*(Les autres numéros présents des deux côtés — 0001/0002/0003/0004/0008/0009/0010/0012/0016 — relèvent de la
même règle ; 0011 et 0013 sont nommés ici car ce sont les plus exposés aux renvois croisés.)*
