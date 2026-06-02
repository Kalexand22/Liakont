# Review indépendante — Conformat backlog v4

## Ta mission

Tu es un **reviewer indépendant**. Tu n'as participé à aucune décision de conception de ce
projet et tu ne dois rien tenir pour acquis. Ton travail : passer en revue l'intégralité du
dépôt Conformat (vision, architecture, backlog d'orchestration, outillage, dépôt d'état) et
produire un rapport de findings exploitable.

Tu ne corriges RIEN. Tu observes, tu vérifies, tu rapportes.

## Règles absolues

1. **LECTURE SEULE.** Tu ne modifies, ne crées, ne supprimes aucun fichier — à une seule
   exception : le rapport final (voir « Livrable »).
2. Tu ne lances **aucune session d'orchestration**, tu ne prends aucun item, tu ne touches pas
   au dépôt d'état (`C:\Source\conformat-orchestration` : state.yaml, leases/, events.jsonl).
3. Chaque finding cite le **fichier** (et la ligne si pertinent). Pas de finding vague.
4. Classement : **P1** = bloquant (incohérence entre documents, trou fonctionnel, risque
   fiscal, dépendance cassée, faux vert dans l'outillage) ; **P2** = important non bloquant.
5. Pas de compliments, pas de suggestions cosmétiques. Si une dimension est saine : une ligne
   « Aucun finding » suffit.
6. Utilise des subagents en parallèle pour les dimensions indépendantes (garde ton contexte
   principal propre), mais c'est TOI qui consolides et vérifies chaque finding avant de le
   rapporter : un finding non vérifié sur pièce n'entre pas dans le rapport.
7. Ne te fie pas aux résumés/commentaires des fichiers : vérifie sur le contenu réel.

## Le contexte en deux phrases

Conformat est une **passerelle de conformité facturation électronique GÉNÉRIQUE** (produit,
jamais un développement spécifique client) entre des logiciels métier legacy (base accessible,
pas d'API) et des **Plateformes Agréées** (réforme française, échéance septembre 2026).
Le développement est piloté par un système d'orchestration multi-agents : le backlog est
`orchestration/manifest.yaml`, l'état runtime vit dans le dépôt séparé
`C:\Source\conformat-orchestration`.

## Lecture préalable (dans cet ordre)

La doctrine — tout écart du backlog par rapport à ces documents est un finding :

1. `blueprint.md` — doctrine d'architecture (généricité 2 axes, Service mono-écrivain, stack)
2. `CLAUDE.md` — règles métier non négociables + règles de review P1/P2
3. `tasks/decisions.md` — journal des décisions de pilotage avec leur justification
4. `tasks/lessons.md` — erreurs passées et règles qui en découlent
5. `orchestration/MANIFEST-CONVENTIONS.md` + `orchestration/protocol.md` — conventions et
   protocole d'orchestration

Les specs fonctionnelles (la référence métier) :

6. `docs/conception/README-Index-Conception.md` puis `docs/conception/F01-*.md` à `F11-*.md`

Le cadrage marché (la référence commerciale) :

7. `docs/market/Conception-Produit-Passerelle.md`, `DR9` (pricing), `DR17` (stratégie multi-PA),
   `Offre-Editeur-Passerelle.md`

Le backlog à reviewer :

8. `orchestration/manifest.yaml` (v4 : 68 items + 12 gates = 80 entrées, 11 segments)
9. `orchestration/items/*.yaml` (17 lots : SOL, PIV, TVA, VAL, TRK, PAA, PAB, PAS, PIP, SVC,
   API, CLI, ADP, WPF, CFG, DOC, CMP)
10. `orchestration/blueprints/*.yaml` (4 blueprints)

L'outillage et les agents :

11. `tools/*.ps1` (orch-state, verify-fast, run-tests, codex-review, build-agent-context)
12. `.claude/agents/*.md` (orch-commit, orch-finalize, orch-fix-*)
13. `.claude/settings.json`

Le dépôt d'état (lecture seule stricte) :

14. `C:\Source\conformat-orchestration\state.yaml`, `config.yaml`, `README.md`

## Dimensions de review

### A. Cohérence structurelle de l'orchestration

- Chaque item du manifest existe dans son fichier de lot (`orchestration/items/<lot>.yaml`)
  et réciproquement (aucun item orphelin).
- Chaque entrée du manifest existe dans `state.yaml` et réciproquement (80 ↔ 80).
- Chaque `depends_on` référence un item existant. Aucun cycle de dépendances.
- Chaque `blueprint:` référencé existe dans `orchestration/blueprints/`.
- Chaque gate de segment (`gate:`, `depends_on_gate:`) existe dans les items.
- Les priorités sont cohérentes avec l'ordre des dépendances (un item ne doit pas avoir une
  priorité inférieure à celle d'un item dont il dépend, au sein du même segment).
- Les chemins de specs cités dans les lots (`docs/conception/...`, `docs/market/...`) existent.
- La convention de nommage des sous-branches (`<segment>-<item>`, tiret) est appliquée
  partout (protocol.md, orch-state.ps1, blueprints, agents).

### B. Alignement backlog ↔ doctrine (généricité)

Pour CHAQUE item des lots Core (PIV, TVA, VAL, TRK, PAA, PIP, SVC, API, CLI, WPF, CFG, DOC) :

- Aucune donnée client (SIREN réel, table TVA réelle, chaîne ODBC, compte PA, nom CMP utilisé
  autrement que comme exemple de déploiement) dans la description ou les critères d'acceptation.
- Aucune fonctionnalité conditionnée à ce qu'UN PA précis sait faire (tout passe par
  `PaCapabilities`), aucune fonctionnalité conditionnée à UN logiciel source précis (tout passe
  par `ExtractorCapabilities`).
- La console (lot WPF) ne référence jamais Core/plug-ins/SQLite — uniquement Api + ApiClient.
- Les items du lot CMP (déploiement) sont du PARAMÉTRAGE pur — s'ils demandent du code dans
  `src/`, c'est un P1.
- Les frontières blueprint.md §6 sont respectées par chaque description d'item.

### C. Couverture des specs

- Chaque spec `F01` à `F11` est couverte par au moins un item, et chaque exigence majeure
  de chaque spec est traçable vers un item (faire le croisement spec par spec).
- Réciproquement : chaque item qui énonce une règle métier (catégorie TVA, VATEX, seuil,
  algorithme) cite une source dans `docs/conception/` — une règle fiscale sans source est un P1.
- Les engagements de l'offre commerciale (`docs/market/Offre-Editeur-Passerelle.md`, DR9 :
  archivage 10 ans inclus, multi-PA, etc.) ont chacun un item qui les réalise.

### D. Graphe de dépendances et ordonnancement des segments

- L'ordre des gates permet-il réellement de livrer ? (socle → core-foundation → pa-framework →
  pa-b2brouter/pa-superpdp/pipeline → service-api → console-admin → toolkit → cmp)
- Y a-t-il des dépendances inter-segments cachées ? (ex. un item d'un segment qui a besoin d'un
  type créé dans un segment dont la gate n'est pas en amont)
- Les items sans `depends_on` au sein d'un segment dépendant d'une gate : est-ce voulu ?
- GATE_DEMO_ISATECH et GATE_PROD_CMP : le chemin critique est-il réaliste et complet ?

### E. Parcours opérateur (trous fonctionnels)

Dérouler concrètement chaque parcours et vérifier que chaque étape a un item qui la réalise,
côté moteur ET côté console ET côté API :

1. **Premier démarrage** : installation → configuration → premier run → premiers documents.
2. **Run nominal** : extraction → mapping TVA → validation → envoi PA → suivi statuts →
   tax report → archive.
3. **Régime TVA inconnu** : blocage → le comptable voit quoi ? → complète la table comment ? →
   revalidation → déblocage.
4. **SIREN invalide / acheteur professionnel détecté** : blocage → verdict opérateur → reprise.
5. **Avoir** : extraction → lien facture d'origine → cas orphelin.
6. **Rejet PA** : rejet → correction → renvoi sous nouveau numéro → ancien Superseded.
7. **Panne / reprise** : timeout pendant envoi → reprise sans doublon.
8. **PDF** : récupération Factur-X PA + bordereau source → archive ; cas pool sans lien →
   réconciliation auto/manuelle → archive en addendum.
9. **Contrôle fiscal dans 5 ans** : export d'un dossier → vérification d'intégrité →
   lisibilité sans le logiciel.
10. **Multi-postes** : deux comptables en même temps → droits différents (lecture / actions /
    paramétrage).

Pour chaque parcours : si une étape n'a pas d'item, ou si la résolution d'un blocage n'a pas
d'écran/outil opérateur, c'est un P1.

### F. Solidité fiscale et réglementaire

- Les exigences de la réforme (e-invoicing/e-reporting, Flux 10.3/10.2/10.4, EN 16931,
  Factur-X, mentions VATEX, BR-CO-15, conservation 10 ans, art. 289 CGI) telles que décrites
  dans les specs sont-elles correctement traduites dans les items ?
- Les garde-fous non négociables (decimal partout, append-only, WORM, lecture seule de la base
  source, blocage plutôt qu'envoi faux) apparaissent-ils dans les critères d'acceptation des
  items concernés — pas seulement dans CLAUDE.md ?
- Les limites assumées (pas de certification NF Z42-013, pas d'OCR, B2B en garde-fou V1) sont-elles
  documentées de façon cohérente partout où elles apparaissent ?

### G. Faisabilité technique (.NET Framework 4.8)

Évaluer de façon critique (sans coder) la crédibilité des choix pour chaque item technique :

- API HTTP self-hosted + auth Windows en net48 (HttpListener/OWIN) : réaliste ?
- OpenTimestamps en net48 sans package externe (règle « tout nouveau package = ADR ») : l'item
  TRK07 est-il réalisable tel que décrit ?
- Extraction de texte PDF sans OCR (TRK08) en net48 sans nouveau package : réalisable ou
  cet item nécessite-t-il un ADR de dépendance ?
- ODBC Pervasive x86, builds x86/x64, drivers : les contraintes sont-elles correctement
  propagées dans les items (SOL, ADP) ?
- Visionneuse PDF intégrée dans la console WPF (WPF08) : réaliste sans nouveau package ?
- Chaque point douteux = P2 avec la question précise à trancher (et le besoin d'ADR éventuel).

### H. Outillage et protocole d'orchestration (faux verts)

- `tools/verify-fast.ps1`, `run-tests.ps1`, `codex-review.ps1` : que se passe-t-il sur état
  vide, état sale, échec partiel ? Un échec peut-il passer pour un succès (faux vert) ?
- `tools/orch-state.ps1` : le mutex protège-t-il réellement toutes les mutations ? Les
  transitions d'état interdites sont-elles rejetées ?
- `orchestration/protocol.md` : les étapes Step 0-5b couvrent-elles les cas d'échec (lease
  orphelin, item failed, gate refusée, conflit de merge) ? L'auto-recovery est-il sûr ?
- `.claude/agents/*.md` : les instructions des subagents sont-elles cohérentes avec le
  protocole et les conventions (branches, encodage, scope) ?
- Encodage : les .ps1 ont-ils un BOM UTF-8 ? Les écritures de fichiers spécifient-elles l'encodage ?
- `state` / `session-log` / `archive` dans `orchestration/` du dépôt source : ces répertoires
  doivent-ils être là ou dans le dépôt d'état ? (incohérence potentielle)

## Livrable

Un rapport unique : `tasks/review-independante-v4.md` (c'est le SEUL fichier que tu crées).

Structure du rapport :

```markdown
# Review indépendante — Conformat backlog v4
Date : <date>
Reviewer : agent indépendant (session <id>)

## Synthèse
- Findings P1 : <n>
- Findings P2 : <n>
- Verdict global : <prêt pour lancement orchestration / corrections requises avant lancement>

## Findings P1
[P1] | <fichier:ligne> | <description concrète> | <correction suggérée>
...

## Findings P2
[P2] | <fichier:ligne> | <description concrète> | <correction suggérée>
...

## Dimensions sans finding
- <dimension> : Aucun finding.

## Questions ouvertes (ni P1 ni P2 — à trancher par un humain)
- ...
```

## Comment exécuter

1. Lis les documents dans l'ordre indiqué (utilise des subagents pour paralléliser les
   dimensions A à H une fois la doctrine lue).
2. Pour la dimension A, tu peux exécuter des vérifications par script (lecture seule).
3. Consolide, vérifie chaque finding sur pièce, écris le rapport.
4. Termine en affichant la synthèse (compte P1/P2 + verdict) dans ta réponse.
