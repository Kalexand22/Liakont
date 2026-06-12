# Lot 1 — Polish UX/UI console (feat/console-polish)

Périmètre validé par l'opérateur (2026-06-12) :

- [x] 1. Composant partagé `LiakontAlert` (Severity + ChildContent, tokens `--color-severity-*-bg`)
- [x] 2. Composant partagé `LiakontLoading` (CircularProgress indéterminé + libellé, role=status)
- [x] 3. Remplacer les `<div role="alert">` nus des pages par `LiakontAlert` (testids conservés) —
      périmètre constaté : 18 nus + 2 « classe fantôme sans CSS » (DocumentDetail error/notfound)
- [x] 4. Habiller les 3 alertes du dashboard (profil incomplet, TVA non validée, fréquence manquante)
- [x] 5. Remplacer les 9 `<p>Chargement…</p>` par `LiakontLoading` (testids conservés)
- [x] 6. Tuiles de compteurs du dashboard cliquables → `/documents?etat=X` (filtre URL déjà câblé, issue #33)
- [x] 7. Login : placeholders anglais → ressources localisées `Login_Username`/`Login_Password`
- [x] 8. `Documents.razor.css` : variables fantômes → tokens sémantiques `--color-severity-*` / `--color-bg-subtle`
      (DocumentCountsBanner vérifié : toutes ses variables existent, aucun changement)
- [x] 9. Tests bUnit : LiakontAlertTests (7 cas), LiakontLoadingTests (3 cas), DashboardView drill-down,
      Login placeholders==labels — 733 tests verts
- [x] 10. verify-fast PASS → codex-review round 1 CLEAN

## Hors périmètre (lots suivants)
- Lot 2 : densité Documents/Encaissements (fix `:has()` padding, fusion filtres/pastilles, select État redondant)
- Lot 3 : sous-menus Paramétrage (INavNodeProvider), hub allégé, capacités PA déplacées, factorisation liste agents

## Review
- verify-fast : PASS (plateforme + agent, 2026-06-12)
- run-tests : non requis (changements purement front Host + tests bUnit ; aucun endpoint/service touché)
- codex-review : round 1 CLEAN (« No findings ») — tokens dark vérifiés, drill-down vérifié câblé,
  asymétrie des @using vérifiée, a11y spinner vérifiée, orphelins CSS vérifiés
- Décisions notables :
  - Sévérités : accès refusé = Warning, échec de chargement = Error, document introuvable = Warning,
    pas de compte PA actif = Warning (visuel uniquement, aucun changement de comportement)
  - LiakontAlert : marge basse 1rem par défaut (bandeau au-dessus de contenu), annulée en ::deep dans
    le flex-gap du dashboard ; pas de marge dans le composant LiakontLoading
  - Tuiles dashboard : liens naturels `<a>` (vue pure, pas de NavigationManager), aria-label descriptif
