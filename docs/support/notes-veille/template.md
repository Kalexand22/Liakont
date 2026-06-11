<!--
  MODÈLE de note de veille réglementaire.
  Copier ce fichier sous notes-veille/AAAA-MM-JJ-<source-sujet>.md et remplir chaque champ.
  Processus complet : ../veille-reglementaire.md
  Règle absolue : la veille produit des AMENDEMENTS DE SPEC, jamais du code direct (CLAUDE.md n°2).
-->

# Note de veille — <AAAA-MM-JJ> — <source : sujet>

| Champ | Valeur |
|---|---|
| **Date de la revue** | AAAA-MM-JJ |
| **Type** | mensuelle \| ad hoc (annonce DGFiP) \| ad hoc (PA) |
| **Source** | <nom + URL/réf précise — ex. DGFiP spécifications externes B2B v3.2, https://...> |
| **Auteur de la veille** | <nom / rôle> |
| **Verdict** | aucun impact \| impact mineur \| impact majeur \| **bloqué (décision fiscale manquante)** |

## 1. Changement constaté

<Description factuelle du changement, citée depuis la source primaire. Pas d'interprétation ici.
Si la source n'est pas re-vérifiable, le marquer « à confirmer » — jamais « confirmé ».>

## 2. Analyse d'impact

> Dérouler la checklist d'impact de [veille-reglementaire.md](../veille-reglementaire.md) §4.
> Cocher ce qui est touché ; pour chaque case « oui », nommer la spec et l'action.

- [ ] Specs métier F01-F09 touchées ? → <lesquelles / pourquoi>
- [ ] Specs architecture F10-F14 touchées ? → <…>
- [ ] Schémas / flux modifiés ? → <…>
- [ ] Catégories / codes TVA modifiés ? → <…>
- [ ] Validation à amender ? → <… — rappel : pas de dégradation Blocking → Warning>
- [ ] Tests de régression à rejouer (golden files, suites de contrat PA) ? → <…>
- [ ] Capacités d'un plug-in PA modifiées (`PaCapabilities`) ? → <…>
- [ ] Échéances / périmètre de l'obligation modifiés ? → <…>

## 3. Décision

<L'une des trois :>
- **Aucune action** : les specs en vigueur restent valides. (Justifier : pourquoi le changement
  n'atteint pas le produit — ex. absorbé par la PA, périmètre phase 2.)
- **Amendement de spec** : lister les specs à amender (`docs/conception/F*.md`) et l'item de
  backlog correspondant. L'amendement (note datée en tête de spec) est écrit **le jour même**.
- **Bloqué** : une décision fiscale manque. Nommer la décision (ex. « cadence reportingFrequency »,
  « régime 6 = marge ou hors champ ») et son **propriétaire** (expert-comptable du client / support
  PA). Aucune implémentation devinée — l'item reste `blocked`.

## 4. Specs amendées / actions ouvertes

| Spec / artefact | Amendement | Item backlog | Statut |
|---|---|---|---|
| docs/conception/F0x-….md | <note d'amendement datée> | <ID item ou « à créer »> | ouvert \| fait \| bloqué |
| Tests golden / contrat | <suite à rejouer> | — | … |

## 5. Communication

| Sortie | Audience | Délai | Statut |
|---|---|---|---|
| Release note réglementaire | éditeurs / clients | <date> | … |
| Alerte d'échéance | clients | ≤ 5 j ouvrés | … |

---
<!-- La note n'est CLOSE que lorsque tous les amendements de spec listés au §4 sont écrits
     (ou explicitement marqués « bloqué » avec leur propriétaire de décision). -->
