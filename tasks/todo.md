# Lot 2 élargi — densité Documents + finitions fiche détail + dashboard (feat/console-polish)

Périmètre calibré sur les captures opérateur + retour d'agent externe (2026-06-12).
(Lots 1 et 3 : DONE, commités 7b50760 / 3749427 / 3a031a9.)

## Investigations tranchées (pas de code)
- [x] « Mois précédent = mêmes 3 bloqués » → PAS un bug : le seed contient 3 bloqués en mai ET 3 en juin (vérifié en base). Problème réel = lisibilité (16 zéros).
- [x] Graphique année : la barre haute EST Bloqué (13) mais 9 catégories dont 7 à zéro → étiquettes auto-skip illisibles. Fix = masquer les zéros.
- [x] « AVO »/« FAC » bruts : DocumentTypeDisplay mappe le contrat (invoice/credit_note), le seed local est hors vocabulaire → corriger la DONNÉE de démo (UPDATE local), pas le mapping (fallback brut sourcé + testé).
- [x] Badge Bloqué 🛑/orange : sourcé F10 §2.2 tel quel → NE PAS changer (règle n°2). Signalé à l'opérateur.
- [x] Heures UTC : décision sourcée (recoupement écran↔export contrôle fiscal) → conservé.
- [x] « Ne loguer que ce qui change » dans l'audit : contenu d'événement = module Pipeline, append-only → item métier séparé, hors lot.

## A. CSS global
- [ ] app.css : supprimer les règles `:has(> .declared-list-page)` (padding 0) → respiration cohérente sur toutes les listes (le bug « filtres collés au bord »).

## B. Liste Documents
- [ ] Retirer le select État (doublon des pastilles) + OnStateChanged ; adapter 6 tests bUnit + 1 E2E (passage par les pastilles).
- [ ] Toolbar unique : Période + Type + pastilles sur une rangée flex (wrap propre), hauteurs alignées.
- [ ] Pastilles redessinées (retour « moche ») : compactes, zéros masqués (sauf état actif), finitions.
- [ ] Colonne Acheteur : « — » si vide.
- [ ] Colonne Montant : « € » + alignement droite + tabular-nums (template ; en-tête socle inchangé).
- [ ] « Tout envoyer » désactivé si table TVA non validée (envois suspendus), Title explicatif. Fallback OUVERT si la lecture échoue (la garde réelle reste serveur — règle n°3 intacte).

## C. Fiche détail document
- [ ] DocumentDetailView.razor.css (NOUVEAU) : en-tête en grille multi-colonnes dense, totaux, tables lignes/charges, timeline structurée (date · type · détail · auteur — fix « textes collés »), hash mono, alertes sur tokens severity.
- [ ] FormatMoney : « 50,00 € » (espace insécable) — détail + colonne liste.
- [ ] Fusion visuelle des deux zones d'actions (ActionBar + ResolutionActions) dans une section commune ; h2 « Actions de résolution » → micro-label.
- [ ] Hint « Tranchez : » → « Que souhaitez-vous faire ? » ; B2C et B2B au MÊME niveau visuel (pas de primaire qui oriente une décision de conformité).
- [ ] DocumentDetail.razor.css (NOUVEAU) : h1 réduit, feedback d'action stylé severity.

## D. Dashboard
- [ ] Masquer les compteurs à zéro par périmètre (+ message « aucun document » si périmètre vide) ; idem graphique année (clic remappé sur la liste filtrée).
- [ ] Agent : LastSeen null et non révoqué → badge « Jamais connecté » (Warning) au lieu d'« Actif » ; meta « Dernier contact » seulement si non null.
- [ ] Affordance des tuiles-liens (hover) — vérifier l'existant.

## E. Données de démo (local uniquement)
- [ ] UPDATE document_type : FAC→invoice, AVO→credit_note (aligne la démo sur le contrat).

## Vérification
- [x] verify-fast PASS (757 bUnit Host verts ; E2E compile)
- [x] codex-review CLEAN au round 3 (R1 : 3 P2 → E2E pastilles + dette devise actée + recette élargie ;
      R2 : 1 P2 nouveau → bouton « Envoyer » fiche aligné sur la suspension TVA, message partagé
      `DocumentSendSuspension.Reason` ; R3 : no findings)
- [ ] recette visuelle serveur + validation opérateur avant commit — INCLURE les pages liste HORS
      lot impactées par la restauration du padding (Traitements, Agents, Alertes & supervision,
      Réconciliation, Supervision, Encaissements), pas seulement Documents (codex P2 round 1)

## Dette actée (codex P2 round 1, à porter en item backlog par l'opérateur)
- « € » affiché en dur sur les montants console alors que le read-model n'expose pas BT-5
  (CurrencyCode, F01-F02, défaut EUR) : pour un futur flux non-EUR, l'écran afficherait une devise
  fausse. Suivi = exposer la devise dans le read-model Documents puis piloter le symbole par BT-5.

## Bug préexistant corrigé au passage
- `SelectedState="_selectedState"` (sans @) dans Documents.razor : paramètre string lié en chaîne
  LITTÉRALE → la pastille active ne s'allumait jamais depuis une URL restaurée (piège CS0414 déjà
  documenté en mémoire). Corrigé + épinglé par les tests adaptés.
