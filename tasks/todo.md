# WEB02 — Page Documents (vue centrale)

Segment console-web, sous-branche `feat/console-web-WEB02`. Blueprint `blazor-page-item`.
Spec : F10 §2.1/§2.2 ; gabarit AdminAgents.razor + DeclaredListPage (P1 : aucune grille maison).

## Décisions d'architecture (documentées)

1. **DeclaredListPage est CLIENT-paginée** (`LoadItems` renvoie la liste complète, pagination/filtre
   en mémoire) ; il n'existe pas de variante server-paged dans le socle Stratum (en bâtir une =
   modifier le socle vendored, hors périmètre). La directive opérateur (P1, répétée) « bâtie sur
   DeclaredListPage, parité fonctionnelle complète, aucune grille maison » prime. La « pagination
   serveur » est honorée AU NIVEAU REQUÊTE (`IDocumentQueries.GetDocumentsAsync` est paginée serveur,
   PageSize max 200) ; la page charge le périmètre **PÉRIODE** complet (défaut = mois courant, qui
   borne le volume — même comportement que le gabarit AdminAgents qui charge tous les agents) et
   DeclaredListPage en assure la pagination d'affichage avec toute la parité (recherche /, filtres
   avancés, colonnes, export, multi-sélection). **Aucune troncature silencieuse** : le service boucle
   sur les pages serveur jusqu'à `TotalCount`.

2. **Filtres** : Période → SERVEUR (From/To, re-fetch + re-key au changement). État + Type → CLIENT
   (CustomFilterPredicate de DeclaredListPage, pas de reload, état de grille préservé). Texte → la
   recherche « / » intégrée de DeclaredListPage (n°, acheteur via ColumnRegistry searchable).
   Le filtre Type serveur serait fragile (document_type = valeur BRUTE source, casse incohérente
   « invoice »/« Invoice ») → filtrage Type côté client, robuste (prédicat insensible à la casse).

3. **Compteurs** : calculés CLIENT-side depuis le périmètre période filtré par Type (pas par État),
   groupés par état dans l'ordre canonique → « synchronisés avec les filtres » (période + type).
   Clic sur un compteur = filtre État. Pure agrégation d'affichage, aucune règle métier.

4. **Type (affichage)** : helper `DocumentTypeDisplay.For(raw)` total, insensible à la casse :
   credit* → « Avoir », invoice → « Facture », sinon valeur brute (jamais masquée). Label générique
   (produit générique — pas « Bordereau » qui est le vocabulaire d'UN client). Classification
   facture/avoir réelle = module Validation (Document.cs:20), ici pur affichage (F10 §2.1 colonne Type).

5. **Actions** : [Voir] = RowAction → `/documents/{id}` (détail WEB03a). [▶ Envoyer la sélection] /
   [▶▶ Tout envoyer] = boutons PRÉSENTS mais DÉSACTIVÉS + tooltip (branchés en WEB05).

## Tâches

- [ ] `Documents/IDocumentConsoleQueries.cs` + `DocumentConsoleQueryService.cs` (boucle de chargement
      complet du périmètre période, sans troncature) + DI dans AppBootstrap.
- [ ] `Documents/DocumentColumnRegistry.cs` (N°, Date, Acheteur, Montant, Type, État, +LastUpdate masqué).
- [ ] `Components/DocumentTypeDisplay.cs` (helper d'affichage type).
- [ ] `Components/DocumentCountsBanner.razor` (bandeau compteurs cliquables).
- [ ] `Components/Pages/Documents.razor` (`@page "/documents"`, [Authorize] read) — filtre bar période,
      DeclaredListPage + état/type (CustomFilters), templates (badge état, type FR, montant FR, date),
      RowAction [Voir], boutons envoi désactivés.
- [ ] Tests bUnit : DocumentConsoleQueryService (complétude), DocumentColumnRegistry, DocumentTypeDisplay,
      DocumentCountsBanner, et smoke render de la page Documents.
- [ ] Test E2E Playwright : login → /documents → grille + filtre + [Voir] → détail.
- [ ] verify-fast + run-tests + run-e2e verts ; codex-review propre.

## Review

Tous les fichiers prévus créés/modifiés :
- Composition lecture : `Documents/IDocumentConsoleQueries.cs` + `DocumentConsoleQueryService.cs`
  (boucle de chargement du périmètre période COMPLET, aucune troncature) + DI dans `AppBootstrap.cs`.
- Liste : `Documents/DocumentColumnRegistry.cs` (N°/Date/Acheteur/Montant/Type/État + LastUpdate masqué).
- Affichage : `Components/DocumentTypeDisplay.cs` (helper type FR, total, fallback brut).
- Synthèse : `Components/DocumentCountsBanner.razor` (+ `.razor.css`) — compteurs cliquables (filtre État).
- Page : `Components/Pages/Documents.razor` (+ `.razor.css`) — `@page "/documents"`, [Authorize] ;
  DeclaredListPage (aucune grille maison), filtres période (serveur) + état/type (CustomFilterPredicate),
  recherche « / », templates (badge état, type FR, montant/date FR), RowAction [Voir], envoi désactivé.
- Tests bUnit : `DocumentConsoleQueryServiceTests` (complétude pagination), `DocumentColumnRegistryTests`,
  `DocumentTypeDisplayTests`, `DocumentCountsBannerTests`, `DocumentsTests` (render page complet + erreur).
- Test E2E : `DocumentsListE2ETests` (login → nav Documents → page rendue, filtres, compteurs, envoi désactivé).

Décisions notables (rien d'inventé) — voir section « Décisions » ci-dessus :
- DeclaredListPage est client-paginée → la « pagination serveur » est honorée au niveau requête
  (GetDocumentsAsync paginée, boucle jusqu'à TotalCount, aucune troncature) ; la période (mois courant)
  borne le volume. La directive opérateur P1 « DeclaredListPage + parité complète » prime.
- Type filtré/affiché côté client (valeur source BRUTE à casse incohérente) — filtre serveur fragile écarté.
- Compteurs calculés client (période + type), aucune règle métier.

Vérification :
- [x] verify-fast (plateforme .NET 10 + agent net48) — PASS
- [x] run-tests (suite complète) — PASS (4139 tests, 0 échec)
- [x] run-e2e (Playwright) — PASS (3 E2E : LoginShell + Dashboard + DocumentsList)
- [ ] codex-review -Base feat/console-web — à lancer
