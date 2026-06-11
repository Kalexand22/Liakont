# Visibilité des actions dans la console (principe FIX04b)

> Origine : recette humaine GATE_CONSOLE_WEB (2026-06-10/11), finding P2 « Actions documents
> pas assez visibles — boutons enfouis dans les onglets ». Décision opérateur 2026-06-10 :
> **toutes les actions directement accessibles**, principe valable partout dans la console.

## Principe

Les **actions** portant sur **une entité affichée** (verdict, re-vérification, envoi, résolution…)
vivent dans une **barre d'actions permanente en tête de page**, visible quel que soit l'onglet
actif. Elles ne sont **jamais** accessibles **uniquement** via un onglet : un onglet ne porte que
du **contenu** (lecture). La visibilité d'une action dépend de l'**état** de l'entité et de la
**permission** de l'opérateur (`liakont.actions`) ; sans la permission, l'action n'est pas rendue
(la page reste consultable en lecture). L'application réelle reste **serveur** (les services
d'action portent la garde de permission en défense en profondeur — le masquage UI n'est qu'un
confort visuel).

Composant de réemploi : `Stratum.Common.UI.Components.ActionBar` (barre stylée). Exemple de mise
en œuvre : `Liakont.Host.Components.DocumentActionBar` (page détail document).

## Revue des écrans console (acceptance FIX04b §3)

| Écran | Onglets ? | Constat | Action |
|-------|-----------|---------|--------|
| **Détail document** (`DocumentDetail` / `DocumentDetailView`) | oui (Contenu / Contrôles / Historique / Archive) | Les actions (verdict B2B/B2C, re-vérification) étaient **enfouies dans l'onglet « Contrôles »** ; l'envoi n'était pas proposé sur la fiche. | **Corrigé (FIX04b)** : barre d'actions permanente en tête (`DocumentActionBar` : verdict, re-vérification, **envoi** selon l'état) ; les onglets ne portent plus que du contenu ; les actions de résolution terminale (`DocumentResolutionActions`, panneaux multi-étapes) sont co-localisées en tête, hors onglet. |
| **Documents** (liste, `Documents.razor`) | non | Actions d'envoi (sélection, « Tout envoyer », « Lancer un traitement ») déjà en barre d'outils de page, hors onglet. | Conforme — rien à déplacer. |
| **Paramétrage / table TVA / journal / supervision** | non | Détails en lecture ou formulaires `DeclaredFormPage` dont les actions (Enregistrer) appartiennent au formulaire édité. | Conforme. |
| **Intégrations** (`AdminIntegrations.razor`, **page Stratum ERP**, hors console Liakont documentaire) | oui (Clés API / Tourinsoft / ChorusPro / SIG) | Chaque onglet est un **formulaire de configuration indépendant** ; ses boutons (Enregistrer / Tester) appartiennent **intrinsèquement** au formulaire qu'ils éditent (déplacer un « Enregistrer » dans une barre globale rendrait ambigu *quel* formulaire est enregistré). | **Pas un candidat** : le principe vise les actions sur **une** entité éclatées entre onglets informatifs, pas un Save par formulaire. Aucune modification. |

Aucun autre écran de la console n'éclate les actions d'une même entité entre des onglets
informatifs. Le seul écran présentant l'anti-pattern (détail document) est corrigé.
