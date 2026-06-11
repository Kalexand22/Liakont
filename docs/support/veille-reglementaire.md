# Processus de veille réglementaire

> **But.** Détecter toute évolution réglementaire ou normative qui touche le produit Liakont,
> en mesurer l'impact, et garantir qu'elle est répercutée **par un amendement de spécification**
> (jamais par du code écrit « de tête »). La veille réglementaire est un **engagement
> commercial de l'Offre Éditeur** (mutualisée, incluse à chaque palier, niveau de support N3 —
> voir [Offre-Editeur-Passerelle.md](../market/Offre-Editeur-Passerelle.md) §3/§5).
>
> **Propriétaire** : l'éditeur (IT Innovations) au titre du N3. Ce document décrit le processus ;
> les notes de veille produites vivent dans [`notes-veille/`](notes-veille/).

---

## 1. Pourquoi l'impact réglementaire est structurellement filtré

Liakont **ne dialogue jamais directement** avec le PPF/DGFiP. Le produit transmet des données
structurées aux **Plateformes Agréées** (B2Brouter, Super PDP…) via leurs API propres ; ce sont
les PA qui produisent les flux DGFiP conformes (Factur-X, Flux 10.x XML). Les spécifications
externes DGFiP servent à Liakont de **référence de validation amont** (s'assurer que les données
transmises permettront à la PA de produire un flux valide), **pas** de format de sortie.

Conséquence pour la veille : une évolution de **format de sérialisation DGFiP** (un attribut XML
supprimé, une cardinalité XSD modifiée) est le plus souvent **absorbée par la PA** et sans impact
sur Liakont. Ce qui touche réellement le produit, ce sont les évolutions de **règles métier** :
catégories/codes TVA, frontière B2B/B2C, règles de validation, cadence/structure de
l'e-reporting, codes de rejet. La checklist d'impact (§4) sépare ces deux familles.

---

## 2. Sources surveillées

| Source | Quoi | Où | Type d'impact attendu |
|---|---|---|---|
| **DGFiP — spécifications externes B2B** | Dossier général, annexes sémantiques (e-reporting, règles de gestion), XSD, changelog | <https://www.impots.gouv.fr/specifications-externes-b2b> | Validation amont (F04), structure e-reporting (F09), frontière B2B/B2C (F07-F08) |
| **DGFiP — BOFiP / fiches pratiques** | Doctrine fiscale (TVA sur les débits, régimes particuliers, marge) | <https://bofip.impots.gouv.fr> | Mapping TVA (F03), avoirs (F07-F08), e-reporting (F09) |
| **Normes AFNOR** | XP Z12-012 / -013 / -014 (modèle sémantique de la facture électronique) | Boutique AFNOR (normes payantes) | Modèle pivot et contrat d'extraction (F01-F02) |
| **PA partenaire — B2Brouter** | Évolutions d'API, nouvelles capacités (ex. statut « Encaissée »), dépréciations | Doc B2Brouter + canal partenaire | Client API (F05), capacités déclarées du plug-in |
| **PA partenaire — Super PDP** | Évolutions d'API, capacités, sandbox | superpdp.tech/documentation + canal partenaire | Plug-in Super PDP (F14), capacités déclarées |
| **Recodification / textes fiscaux** | Renumérotation d'articles (CGI, art. 289), décrets d'application, calendrier de l'obligation | Légifrance, communications DGFiP | Piste d'audit (F06), périmètre/échéances |

> **Règle de méthode.** Toute affirmation issue de la veille **cite sa source primaire**. Une
> source non re-vérifiable se marque « à confirmer », jamais « confirmé ». (Même discipline que
> l'[index de conception](../conception/README-Index-Conception.md) : une abstention de
> vérification n'est pas une réfutation, mais ne vaut pas confirmation.)

---

## 3. Cadence

- **Mensuelle** : revue systématique des sources du §2 (au minimum la page DGFiP des
  spécifications externes et les canaux partenaires PA), même sans annonce — une note de veille
  est produite **même quand le verdict est « aucun impact »** (la traçabilité du « rien à faire »
  vaut preuve de diligence vis-à-vis du client).
- **À chaque annonce DGFiP majeure** (nouvelle version des spécifications externes, report de
  calendrier, nouvelle annexe de règles de gestion) : revue **immédiate**, hors cadence mensuelle.
- **À chaque annonce d'un PA partenaire** susceptible de changer une capacité (nouveau statut,
  dépréciation d'endpoint) : revue ciblée du plug-in concerné.

---

## 4. Checklist d'impact

À dérouler **pour chaque évolution détectée**, et à consigner dans la note de veille (§5).
Chaque case « oui » ouvre une action d'amendement de spec — jamais un commit de code direct (§6).

- [ ] **Specs métier F01-F09 touchées ?** Laquelle / lesquelles ?
  - F01-F02 (modèle pivot, contrat d'extraction) — nouveau BT/BG obligatoire ?
  - F03 (mapping TVA) — catégorie TVA, code VATEX, règle d'arrondi modifiés ?
  - F04 (contrôles / validation) — nouvelle règle de gestion, nouveau code de rejet ?
  - F05 (client API B2Brouter) — endpoint, format, capacité modifiés ?
  - F06 (tracking / piste d'audit) — durée de conservation, obligation d'horodatage modifiées ?
  - F07-F08 (avoirs, frontière B2B/B2C) — seuil, garde-fou, cas d'usage modifiés ?
  - F09 (e-reporting de paiement) — cadence, structure des flux 10.x, périmètre modifiés ?
- [ ] **Specs d'architecture/plateforme F10-F14 touchées ?** (console, CLI, agent, installateur,
      plug-in Super PDP) — un changement de format remonte-t-il jusqu'à l'UI ou l'agent ?
- [ ] **Schémas / flux modifiés ?** XSD DGFiP, annexes sémantiques, format pivot interne.
- [ ] **Catégories / codes TVA modifiés ?** (impact direct sur F03 et la table TVA de tenant —
      **donnée de paramétrage**, jamais codée en dur ; voir [CLAUDE.md](../../CLAUDE.md) règle 7.)
- [ ] **Validation à amender ?** Une règle Blocking ne se dégrade jamais en Warning pour « faire
      passer » (CLAUDE.md règle 3) — un assouplissement réel doit venir de la source DGFiP citée.
- [ ] **Tests de régression à rejouer ?** Golden files (pivot canonique, mappings, validations),
      suites de contrat des plug-ins PA.
- [ ] **Capacités d'un plug-in PA modifiées ?** (`PaCapabilities`) — une capacité se déclare
      d'après ce que le PA expose réellement, jamais d'après un flag produit (CLAUDE.md règle 8).
- [ ] **Échéances / périmètre de l'obligation modifiés ?** (calendrier DGFiP, populations
      assujetties) — impact commercial / documentaire (guides, Offre Éditeur).

---

## 5. Note de veille

Chaque revue (mensuelle ou ad hoc) produit **une note** dans
[`notes-veille/`](notes-veille/), au format du [modèle](notes-veille/template.md) :
date, source, changement constaté, analyse d'impact (checklist §4), décision, specs amendées.

- Nommage : `notes-veille/AAAA-MM-JJ-<source-sujet>.md` (ex.
  `2026-06-02-dgfip-specs-v3.2.md`).
- Une note « aucun impact » est légitime et **doit** exister (preuve de diligence).
- Une note qui conclut à un impact **liste les specs à amender** et **ouvre le ou les
  amendements** (§6) — la note n'est close que lorsque les amendements de spec sont écrits.

**Exemple réel disponible** : le delta des spécifications externes DGFiP **v3.1 → v3.2**
(publiées le 30/04/2026) a été analysé le 2026-06-02. Verdict : impact **minime** (un attribut
XML cosmétique en e-reporting, du B2B de phase 2) — les specs F01-F11 restent valides, aucun
amendement requis. La note de lecture source est
[`docs/references/dgfip-v3.2/LECTURE-LIAKONT.md`](../references/dgfip-v3.2/LECTURE-LIAKONT.md)
(avec le [changelog XSD officiel](../references/dgfip-v3.2/3-%20XSD_v3.2/Changelog_XSD.md)) ; la
note de veille correspondante, au format du modèle, est
[`notes-veille/2026-06-02-dgfip-specs-v3.2.md`](notes-veille/2026-06-02-dgfip-specs-v3.2.md).

---

## 6. Règle absolue : une évolution réglementaire ne s'implémente JAMAIS sans amendement de spec

C'est l'application directe de la **règle n°2 de [CLAUDE.md](../../CLAUDE.md)** (« aucune règle
fiscale inventée ») au cycle de vie réglementaire :

> **La veille produit des amendements de spécification, pas du code direct.**

Concrètement, l'ordre est **toujours** :

1. **Note de veille** (`notes-veille/`) : source citée, changement, analyse d'impact.
2. **Amendement de spec** (`docs/conception/F*.md`) : note d'amendement **datée, en tête** de la
   spec touchée, le jour même de la décision (discipline héritée de la review v6 — une décision
   qui contredit une spec sans amender la spec est une corruption en attente). Si une décision
   fiscale manque (régime non tranché, cadence non sourcée), la spec reste **bloquée** avec le
   nom de la décision manquante et son propriétaire (expert-comptable du client ou support PA) —
   on ne devine pas.
3. **Backlog** : l'item d'implémentation référence l'amendement de spec, jamais la source DGFiP
   directement. Aucun code de règle métier (catégorie TVA, seuil, code VATEX) sans une ligne
   traçable dans `docs/conception/`.

Toute PR qui modifie une règle fiscale **sans** amendement de spec correspondant est un **P1**
en review (CLAUDE.md, règles de review 9-11).

---

## 7. Communication

| Quoi | Qui | Vers qui | Délai |
|---|---|---|---|
| **Release note réglementaire** (résumé d'une évolution et de son traitement) | Éditeur (N3) | Éditeurs / clients finaux | À la livraison de l'amendement / correctif ; au plus tard à la fin de la cadence mensuelle |
| **Alerte d'échéance** (report ou avancement du calendrier DGFiP) | Éditeur (N3) | Tous les clients | Sous **5 jours ouvrés** après l'annonce officielle |
| **Note interne « aucun impact »** | Veille | Équipe / archive `notes-veille/` | À chaque cadence mensuelle |

> La gravité d'une communication suit la même logique que les alertes de supervision
> (🔴 critique / 🟠 importante, voir F12 §5 et le kit support DOC02) : un changement qui bloque
> l'envoi est traité comme une alerte critique, pas comme une simple note.

---

## Références

- [CLAUDE.md](../../CLAUDE.md) — règles métier non négociables (n°2 : aucune règle fiscale inventée).
- [docs/conception/](../conception/README-Index-Conception.md) — specs F01-F14 (source de vérité fonctionnelle).
- [Offre-Editeur-Passerelle.md](../market/Offre-Editeur-Passerelle.md) — engagement de veille (§3/§5).
- [docs/references/dgfip-v3.2/](../references/dgfip-v3.2/LECTURE-LIAKONT.md) — première note de veille réelle (delta v3.1→v3.2).
- [notes-veille/template.md](notes-veille/template.md) — modèle de note de veille.
