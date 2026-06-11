# Note de veille — 2026-06-02 — DGFiP : spécifications externes B2B v3.1 → v3.2

| Champ | Valeur |
|---|---|
| **Date de la revue** | 2026-06-02 |
| **Type** | ad hoc (nouvelle version des spécifications externes DGFiP) |
| **Source** | DGFiP — spécifications externes B2B v3.2, publiée le 30/04/2026 (page modifiée le 07/05/2026), <https://www.impots.gouv.fr/specifications-externes-b2b>. Changelog officiel : [`3- XSD_v3.2/Changelog_XSD.md`](../../references/dgfip-v3.2/3-%20XSD_v3.2/Changelog_XSD.md). Note de lecture détaillée : [`LECTURE-LIAKONT.md`](../../references/dgfip-v3.2/LECTURE-LIAKONT.md). |
| **Auteur de la veille** | Éditeur (N3) |
| **Verdict** | **impact mineur** — aucune spec à amender |

## 1. Changement constaté

Le changelog XSD officiel liste les modifications v3.1 (30/10/2025) → v3.2 (30/04/2026) :

| Domaine | Changement v3.1 → v3.2 (source : changelog officiel) |
|---|---|
| **E-reporting** (Flux 10.x) | `ereporting.xsd` : suppression de l'attribut `xmlns:ereporting`. |
| **E-invoicing** (Flux 1, profil CII BASE) | Mise en commentaire des éléments de ligne de facture `IncludedSupplyChainTradeLineItem` (BG-25). |
| **Annuaire** (Flux 11-12) | Aucun changement. |

La v3.1 était la référence citée par [F01-F02](../../conception/F01-F02-Modele-Pivot-Contrat-Extraction.md).

## 2. Analyse d'impact

- [x] Specs métier F01-F09 touchées ? → **Non.** L'e-reporting (cœur V1) ne change que par la
      suppression d'un attribut XML cosmétique ; de plus Liakont **ne produit pas** ce XML — les
      PA le font (voir [veille-reglementaire.md](../veille-reglementaire.md) §1).
- [ ] Specs architecture F10-F14 touchées ? → Non.
- [x] Schémas / flux modifiés ? → **Oui mais sans portée V1** : la mise en commentaire de BG-25
      concerne le **B2B (Flux 1)**, qui est de **phase 2** chez Liakont (à re-regarder en phase 2).
- [ ] Catégories / codes TVA modifiés ? → Non.
- [ ] Validation à amender ? → Non.
- [ ] Tests de régression à rejouer ? → Non (aucune règle ni format consommé par Liakont en V1
      n'est modifié).
- [ ] Capacités d'un plug-in PA modifiées ? → Non.
- [ ] Échéances / périmètre de l'obligation modifiés ? → Non.

## 3. Décision

**Aucune action.** Les specs internes F01-F11 (rédigées sur la base v3.1) restent valides. Le seul
changement touchant le cœur V1 (e-reporting) est cosmétique et, de toute façon, hors du périmètre
de production de Liakont (assuré par la PA). Le changement e-invoicing relève de la phase 2 B2B.

## 4. Specs amendées / actions ouvertes

| Spec / artefact | Amendement | Item backlog | Statut |
|---|---|---|---|
| — (aucune spec amendée) | — | — | — |

Actions de fond reportées (non bloquantes, déjà tracées dans
[`LECTURE-LIAKONT.md`](../../references/dgfip-v3.2/LECTURE-LIAKONT.md) §« Actions restantes ») :
- Lecture du Dossier général v3.2 (PDF) pour vérifier les évolutions de **texte** (le changelog ne
  couvre que les XSD).
- Croiser l'Annexe 7 (règles de gestion V1.9) avec [F04](../../conception/F04-Controles-Qualite-Validation.md) lors du lot VAL.
- Re-regarder BG-25 (lignes de facture) en **phase 2 B2B**.

## 5. Communication

| Sortie | Audience | Délai | Statut |
|---|---|---|---|
| Release note réglementaire | éditeurs / clients | au fil de la phase 2 (rien à signaler en V1) | sans objet (impact mineur) |
| Alerte d'échéance | clients | — | sans objet (calendrier inchangé) |

---
<!-- Note close : verdict « impact mineur », aucune spec à amender. Conservée comme preuve de
     diligence et comme exemple du processus de veille (DOC03). -->
