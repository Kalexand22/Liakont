# Journal des décisions

_Décisions de pilotage prises en session interactive (les décisions d'architecture vont dans
docs/adr/, les décisions fiscales appartiennent à l'expert-comptable CMP)._

| Date | Décision | Justification |
|---|---|---|
| 2026-06-02 | Reprise du principe d'orchestration Stratum pour Conformat | Système éprouvé (85+ sessions, ~100 items livrés sur Stratum) |
| 2026-06-02 | Dépôt d'état séparé : C:\Source\conformat-orchestration | Éviter les conflits de merge, permettre le multi-agents |
| 2026-06-02 | Backlog initial : 45 items de travail + 7 gates en 7 segments | Décomposition des specs F01-F11 + F12 à rédiger |
| 2026-06-02 | Pas de Playwright (produit WPF desktop) : tests ViewModel + checklists smoke | Adaptation du principe « UI = E2E obligatoire » de Stratum au desktop |
