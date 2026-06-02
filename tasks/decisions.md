# Journal des décisions

_Décisions de pilotage prises en session interactive (les décisions d'architecture vont dans
docs/adr/, les décisions fiscales appartiennent à l'expert-comptable du client concerné)._

| Date | Décision | Justification |
|---|---|---|
| 2026-06-02 | Reprise du principe d'orchestration Stratum pour Conformat | Système éprouvé (85+ sessions, ~100 items livrés sur Stratum) |
| 2026-06-02 | Dépôt d'état séparé : C:\Source\conformat-orchestration | Éviter les conflits de merge, permettre le multi-agents |
| 2026-06-02 | Pas de Playwright (produit WPF desktop) : tests ViewModel + checklists smoke | Adaptation du principe « UI = E2E obligatoire » de Stratum au desktop |
| 2026-06-02 | Sous-branches nommées `<segment>-<item>` (tiret, jamais slash) | Collision de namespace de refs git détectée par la 1ère session d'orchestration |
| 2026-06-02 | **Conformat est un produit GÉNÉRIQUE** : 2 axes de plug-ins (sources IExtractor + PA IPaClient), toute donnée client = paramétrage dans deployments/ | Correction d'alignement utilisateur — le backlog v1 mettait la table TVA CMP dans le Core (erreur) |
| 2026-06-02 | Multi-PA dès la V1 : plug-ins B2Brouter ET Super PDP, abstraction + capacités + tests de contrat | Super PDP (superpdp.tech) porte l'Offre Éco marque grise (DR17/DR9) — exigence commerciale, pas du futur-proofing |
| 2026-06-02 | Aucune fonctionnalité produit ne dépend d'UN PA : comportement piloté par PaCapabilities | « On ne va rien bloquer parce qu'1 PA n'est pas prêt » (F09 développé à fond) |
| 2026-06-02 | **Architecture multi-utilisateurs : le Service est le cœur** (ordonnanceur + pipeline + API HTTP + seul accès SQLite), la console WPF est cliente HTTP | Besoin : plusieurs comptables vérifient depuis leurs postes ; SQLite sur partage réseau = corruption |
| 2026-06-02 | Stockage Tracking multi-provider : SQLite (défaut) + SQL Server Express (politiques IT), via Dapper | Institutions (type CMP) imposent souvent « toutes les bases sur NOTRE SQL Server » |
| 2026-06-02 | **Archivage fiscal 10 ans intégré au produit** : coffre local WORM + chaîne de hashes + rendu lisible + horodatage RFC 3161 optionnel ; copie PA en doublon | L'offre vend « archivage 10 ans inclus » ; l'archive doit être indépendante de la PA. Limite assumée : pas de certification NF Z42-013 |
| 2026-06-02 | Objectif du backlog : socle produit final (GATE_TOOLKIT) → démo ISATECH (GATE_DEMO_ISATECH) → production CMP (GATE_PROD_CMP) | La démo et la prod CMP sont des applications du produit, pas le produit |
| 2026-06-02 | Stack : .NET 4.8, SDK-style csproj, xUnit, MahApps.Metro | Validé utilisateur |
