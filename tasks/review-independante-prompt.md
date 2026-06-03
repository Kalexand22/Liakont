# Review indépendante — Liakont backlog v6 (architecture plateforme + agent)

## Ta mission

Tu es un **reviewer indépendant**. Tu n'as participé à aucune décision de conception de ce
projet et tu ne dois rien tenir pour acquis. Ton travail : passer en revue l'intégralité du
dépôt Liakont (vision, architecture, backlog d'orchestration, outillage, dépôt d'état) et
produire un rapport de findings exploitable.

Contexte particulier de cette review : le backlog v6 est issu d'un **pivot d'architecture
majeur** (2026-06-03) — l'architecture on-premise (service Windows + console WPF + SQLite)
a été remplacée par une **plateforme web centralisée multi-tenant (socle Stratum, .NET 10) +
agent local léger (net48)**. Le backlog v5 a été transposé/réécrit en une seule session :
ta review est le filet de sécurité avant de relancer l'orchestration.

Tu ne corriges RIEN. Tu observes, tu vérifies, tu rapportes.

## Règles absolues

1. **LECTURE SEULE.** Tu ne modifies, ne crées, ne supprimes aucun fichier — à une seule
   exception : le rapport final (voir « Livrable »).
2. Tu ne lances **aucune session d'orchestration**, tu ne prends aucun item, tu ne touches pas
   au dépôt d'état (`C:\Source\liakont-orchestration` : state.yaml, leases/, events.jsonl)
   ni au dépôt Stratum (`C:\Source\Stratum` — lecture seule pour la dimension J).
3. Chaque finding cite le **fichier** (et la ligne si pertinent). Pas de finding vague.
4. Classement : **P1** = bloquant (incohérence entre documents, trou fonctionnel, risque
   fiscal, dépendance cassée, faux vert dans l'outillage, hypothèse Stratum fausse,
   contenu v5 critique perdu) ; **P2** = important non bloquant.
5. Pas de compliments, pas de suggestions cosmétiques. Si une dimension est saine : une ligne
   « Aucun finding » suffit.
6. Utilise des subagents en parallèle pour les dimensions indépendantes (garde ton contexte
   principal propre), mais c'est TOI qui consolides et vérifies chaque finding avant de le
   rapporter : un finding non vérifié sur pièce n'entre pas dans le rapport.
7. Ne te fie pas aux résumés/commentaires des fichiers : vérifie sur le contenu réel.

## Le contexte en trois phrases

Liakont est une **passerelle de conformité facturation électronique GÉNÉRIQUE** (produit,
jamais un développement spécifique client) entre des logiciels métier legacy (base accessible,
pas d'API) et des **Plateformes Agréées** (réforme française, échéance septembre 2026).
Architecture (pivot 2026-06-03) : un **agent net48** chez chaque client final (extraction ODBC +
push HTTPS) et une **plateforme web multi-tenant** (pivot, TVA, validation, états, envoi PA,
archivage, console Blazor, supervision proactive) déployable en 3 topologies (self-hosted
éditeur / dédiée hébergée / mutualisée — marque grise = une instance par éditeur).
Le développement est piloté par un système d'orchestration multi-agents : le backlog est
`orchestration/manifest.yaml` (v6), l'état runtime vit dans `C:\Source\liakont-orchestration`.

## Lecture préalable (dans cet ordre)

La doctrine — tout écart du backlog par rapport à ces documents est un finding :

1. `blueprint.md` — doctrine d'architecture v2 (plateforme + agent, généricité 2 axes,
   multi-tenancy, frontières de modules, stack double)
2. `CLAUDE.md` — règles métier non négociables + règles de review P1/P2 (v2)
3. `tasks/decisions.md` — journal des décisions (notamment les 7 décisions du pivot 2026-06-03)
4. `tasks/analyse-impact-pivot-plateforme.md` — l'analyse d'impact du pivot (ce qui devait être
   transposé/réécrit/créé — c'est ton référentiel pour la dimension I)
5. `tasks/lessons.md` — erreurs passées et règles qui en découlent
6. `orchestration/MANIFEST-CONVENTIONS.md` + `orchestration/protocol.md`

Les specs fonctionnelles (la référence métier) :

7. `docs/conception/README-Index-Conception.md` puis `docs/conception/F01-*.md` à `F12-*.md`
   (attention aux AMENDEMENTS datés en tête de F06, F10, F11 — l'amendement fait foi sur le
   corps du document)

Le cadrage marché (la référence commerciale) :

8. `docs/market/Conception-Produit-Passerelle.md`, `DR9` (pricing), `DR17` (stratégie multi-PA),
   `Offre-Editeur-Passerelle.md`

Le backlog à reviewer :

9. `orchestration/manifest.yaml` (v6 : 79 items + 12 gates = 91 entrées, 11 segments)
10. `orchestration/items/*.yaml` (19 lots : SOL, PIV, TVA, VAL, TRK, CFG, PAA, PAB, PAS, PIP,
    AGT, ADP, API, WEB, SUP, OPS, BRD, DOC, CMP)
11. `orchestration/blueprints/*.yaml` (4 blueprints : module-work-item, blazor-page-item,
    tooling-item, docs-spec-item)

L'outillage et les agents :

12. `tools/*.ps1` (orch-state, verify-fast, run-tests, codex-review, build-agent-context)
13. `.claude/agents/*.md` (orch-commit, orch-finalize, orch-fix-*)
14. `.claude/settings.json`

Les dépôts externes (lecture seule stricte) :

15. `C:\Source\liakont-orchestration\state.yaml`, `config.yaml`, `README.md`
16. `C:\Source\Stratum` — le socle qui sera vendored par SOL01 (pour la dimension J)

L'historique v5 (pour la dimension I) :

17. Le backlog v5 est dans l'historique git : `git show 223f0f3~1:orchestration/manifest.yaml`
    et `git show 223f0f3~1:orchestration/items/<lot>.yaml` (lots v5 : SOL, PIV, TVA, VAL, TRK,
    CFG, PAA, PAB, PAS, PIP, SVC, API, CLI, ADP, WPF, PKG, DOC, CMP)

## Dimensions de review

### A. Cohérence structurelle de l'orchestration

- Chaque item du manifest existe dans son fichier de lot (`orchestration/items/<lot>.yaml`)
  et réciproquement (aucun item orphelin).
- Chaque entrée du manifest existe dans `state.yaml` et réciproquement (91 ↔ 91).
- Chaque `depends_on` référence un item existant. Aucun cycle de dépendances.
- Chaque `blueprint:` référencé existe. Chaque gate de segment existe dans les items.
- Les priorités sont cohérentes avec l'ordre des dépendances.
- Les chemins de specs cités dans les lots (`docs/conception/...`, `docs/market/...`) existent —
  attention aux références à des sections de F12 (§2.2, §3.3, §5.2, §6...) : vérifier qu'elles
  existent réellement dans F12.
- Les références croisées ENTRE items (ex. « consommé par API04 », « câblé dans PIP01 »,
  « via TRK05 ») pointent vers des items existants qui font bien ce qui est annoncé.
  ATTENTION : le lot TRK a été RENUMÉROTÉ en v6 (TRK05=Archive, TRK06=Ancrage, TRK07=Réconciliation,
  l'ex-TRK05 SQL Server a disparu) — toute référence à un numéro TRK obsolète est un P1.
- La convention de sous-branches (`<segment>-<item>`, tiret) est appliquée partout.
- Le nom de la branche du segment socle est `feat/socle-v6` (l'ancienne `feat/socle` a été
  supprimée) — vérifier qu'aucun document ne référence encore l'ancienne.

### B. Alignement backlog ↔ doctrine (généricité, frontières, multi-tenancy)

Pour CHAQUE item :

- Aucune donnée client (SIREN réel, table TVA réelle, chaîne ODBC, compte PA, nom CMP utilisé
  autrement que comme exemple de déploiement) dans la description ou les critères d'acceptation.
- Aucune fonctionnalité conditionnée à ce qu'UN PA précis sait faire (tout passe par
  `PaCapabilities`), aucune conditionnée à UN logiciel source précis (`ExtractorCapabilities`).
- **Frontières de modules** (blueprint §6) : inter-modules via Contracts uniquement,
  Transmission ne référence jamais un plug-in PA, plug-in → Transmission.Contracts only.
- **Frontière agent/plateforme** : l'agent ne référence que `Liakont.Agent.Contracts` ;
  AUCUNE logique métier dans l'agent (pas de TVA, pas de validation, pas d'états) — tout item
  AGT/ADP qui glisse de la logique métier côté agent est un P1.
- **Tenant-scoping** : les items qui manipulent des données métier le font par tenant ;
  seul le module Supervision a des vues cross-tenant (lecture). Tout autre accès cross-tenant
  décrit dans un item est un P1.
- **Socle vendored** : les items qui modifient du code `Stratum.*` exigent la consignation
  dans la provenance.
- Les items du lot CMP sont du PARAMÉTRAGE pur — s'ils demandent du code dans `src/` ou
  `agent/`, c'est un P1.

### C. Couverture des specs

- Chaque spec `F01` à `F12` est couverte par au moins un item, et chaque exigence majeure
  de chaque spec est traçable vers un item (croisement spec par spec).
  Attention : F10 et F11 sont AMENDÉES — c'est le contenu fonctionnel (écrans, états,
  vocabulaire, planification) qui doit être couvert, pas l'architecture obsolète du corps.
- Réciproquement : chaque item qui énonce une règle métier (catégorie TVA, VATEX, seuil,
  algorithme, heuristique) cite une source dans `docs/conception/` — une règle fiscale sans
  source est un P1.
- Les engagements de l'offre commerciale (archivage 10 ans inclus, multi-PA, supervision
  proactive, marque grise, réversibilité) ont chacun un item qui les réalise.
- Les décisions de `tasks/decisions.md` (notamment les 7 du pivot) sont matérialisées dans
  les items — une décision actée sans item qui la réalise est un finding.

### D. Graphe de dépendances et ordonnancement des segments

- L'ordre des gates permet-il réellement de livrer ? (socle → core-foundation →
  pa-framework/agent → pa-b2brouter/pa-superpdp/pipeline/adapter → console-web → toolkit → cmp)
- Dépendances inter-segments cachées : un item d'un segment a-t-il besoin d'un type/d'une
  fonctionnalité créés dans un segment dont la gate n'est PAS en amont ? Cas à vérifier
  spécifiquement :
  - PIV04/PIV05 (module Ingestion, segment core-foundation) référencent le module Documents
    (TRK01/02, même segment) — les depends_on intra-segment le reflètent-ils ?
  - Le lot AGT (segment agent) a besoin des endpoints d'ingestion (PIV04/05, core-foundation) —
    la gate GATE_CORE_FOUNDATION est-elle bien en amont du segment agent ?
  - Le lot SUP (console-web) a besoin des heartbeats (AGT03/PIV05) — GATE_AGENT est-elle en
    amont de console-web ?
  - WEB07 a besoin de TVA03/TVA05 + API04 ; WEB08 de TRK07 + API04 — les chemins de gates
    sont-ils corrects ?
  - OPS03 (écran provisioning tenant) a besoin de CFG02 (ImportTenantSeed) et de l'infra web —
    le segment deploiement-toolkit est-il bien après console-web ?
- GATE_DEMO_ISATECH et GATE_PROD_CMP : le chemin critique est-il réaliste et complet pour
  l'échéance septembre 2026 ?

### E. Parcours opérateur (trous fonctionnels)

Dérouler concrètement chaque parcours et vérifier que chaque étape a un item qui la réalise,
côté agent ET côté plateforme ET côté console web ET côté API :

1. **Onboarding complet** : création d'une instance (éditeur) → création d'un tenant →
   création d'un utilisateur → enregistrement d'un agent → installation de l'agent chez le
   client → premier push → premiers documents visibles.
2. **Traitement nominal** : extraction agent → push → ingestion → mapping TVA → validation →
   envoi PA → suivi statuts → tax report → archive.
3. **Régime TVA inconnu** : blocage → le comptable voit quoi ? → complète la table comment ? →
   revalidation expert-comptable → déblocage (recheck).
4. **SIREN invalide / acheteur professionnel détecté** : blocage → verdict opérateur → reprise.
5. **Avoir** : extraction → lien facture d'origine → cas orphelin → résolution manuelle.
6. **Rejet PA** : rejet → correction dans le logiciel source → nouveau document poussé →
   liaison supersede → ancien Superseded.
7. **Panne / reprise** : coupure réseau agent (buffer) → reprise sans perte ; timeout pendant
   envoi PA → reprise sans doublon ; crash plateforme pendant Sending → reprise.
8. **Panne silencieuse** : agent muet → détection dead-man's switch → alerte opérateur
   d'instance → (optionnel) alerte contact tenant → résolution → auto-résolution de l'alerte.
9. **PDF** : récupération Factur-X PA + bordereau source → archive ; cas pool sans lien →
   réconciliation auto/manuelle → archive en addendum.
10. **Contrôle fiscal dans 5 ans** : export d'un dossier → vérification d'intégrité →
    lisibilité sans le logiciel.
11. **Réversibilité** : un tenant quitte la plateforme → export complet → suppression contrôlée ;
    un éditeur hébergé passe self-hosted → migration d'instance.
12. **Multi-rôles** : lecture / actions / paramétrage / supervision — chaque page et chaque
    endpoint respecte les permissions ; un utilisateur d'un tenant ne voit JAMAIS un autre tenant.
13. **Mise à jour** : nouvelle version de la plateforme → mise à jour des instances → mise à
    jour de la flotte d'agents (auto-update) → compatibilité contrat N-1.

Pour chaque parcours : si une étape n'a pas d'item, ou si la résolution d'un blocage n'a pas
d'écran/d'endpoint, c'est un P1.

### F. Solidité fiscale et réglementaire

- Les exigences de la réforme (e-reporting Flux 10.3/10.2/10.4, EN 16931, Factur-X, VATEX,
  BR-CO-15, conservation 10 ans, art. 289 CGI) telles que décrites dans les specs sont-elles
  correctement traduites dans les items ?
- Les garde-fous non négociables (decimal partout, append-only, WORM, lecture seule de la base
  source, blocage plutôt qu'envoi faux, garde-fou production table TVA non validée)
  apparaissent-ils dans les critères d'acceptation des items concernés — pas seulement dans
  CLAUDE.md ?
- NOUVEAU RISQUE v6 — la responsabilité d'hébergement : les items OPS (sauvegardes,
  réversibilité, suppression contrôlée) couvrent-ils les obligations qui découlent du fait
  que l'opérateur de l'instance détient les données fiscales et les archives de tiers ?
- Les limites assumées (pas de certification NF Z42-013, pas d'OCR, B2B en garde-fou V1)
  sont-elles documentées de façon cohérente partout ?

### G. Faisabilité technique

Évaluer de façon critique (sans coder) la crédibilité des choix pour chaque item technique :

- **Sérialisation canonique identique entre Newtonsoft.Json (net48) et System.Text.Json
  (.NET 10)** (PIV02) : c'est une exigence FORTE (hash identique octet par octet des deux
  côtés). Est-elle réaliste telle que décrite ? L'item prévoit-il la bonne approche
  (writer JSON manuel dans Agent.Contracts) ? Sinon : P1 avec la question précise.
- **Vendoring du socle Stratum en un seul item** (SOL01) : volume réaliste pour une session
  d'orchestration ? Le périmètre de review (adaptation vs code copié) est-il opérationnel
  pour codex-review (le diff sera énorme) ?
- **Auto-update de l'agent** (AGT04) : le mécanisme décrit (updater séparé, rollback,
  vérification de hash) est-il réaliste en net48 ?
- **Multi-tenancy database-per-tenant** : la création de base par tenant (OPS03), les
  migrations DbUp par tenant, les sauvegardes — les items couvrent-ils la mécanique complète ?
- **E2E Playwright avec Keycloak** : les tests E2E exigés par blazor-page-item nécessitent
  une instance qui tourne (PostgreSQL + Keycloak) — l'infra de test est-elle prévue
  (SOL03 ? Testcontainers ?) ou est-ce un trou ?
- **Empreinte Keycloak par instance** : l'ADR est prévu (OPS01) — les hypothèses de coût
  des instances hébergées tiennent-elles ?
- ODBC Pervasive x86, builds x86/x64 : les contraintes sont-elles propagées dans les items
  AGT/ADP/OPS05 ?
- Chaque point douteux = P2 avec la question précise à trancher (et le besoin d'ADR éventuel).

### H. Outillage et protocole d'orchestration (faux verts)

- `tools/verify-fast.ps1`, `run-tests.ps1` (RÉÉCRITS pour le double build) : que se passe-t-il
  sur état vide, état sale, échec partiel, UNE solution présente et pas l'autre ? Un échec
  peut-il passer pour un succès (faux vert) ? Les gardes bootstrap (SOL01 pour la plateforme,
  SOL02 pour l'agent) sont-elles correctes ? La logique « absent du state = done » est-elle
  bien appliquée ?
- `tools/codex-review.ps1` : les règles P1 mises à jour (frontières modules, tenant-scoping,
  provenance socle, Blazor) sont-elles cohérentes avec CLAUDE.md ? Les exit codes 0/2/3
  fonctionnent-ils toujours ?
- `tools/orch-state.ps1` : le mutex et les transitions valides couvrent-ils les nouveaux
  items/segments ? Rien à adapter pour v6 ?
- `orchestration/protocol.md` + `prompt.md` : sont-ils réellement agnostiques de
  l'architecture (aucune référence WPF/Gateway/x86 résiduelle) ? Les étapes Step 0-5b
  fonctionnent-elles avec les nouveaux noms de segments ?
- Les blueprints : `blazor-page-item` exige des tests E2E AVANT commit — est-ce exécutable
  (l'instance de test existe-t-elle à ce stade) ? Le nœud integration_tests de
  `module-work-item` est-il conditionnel comme avant ?
- `.claude/agents/*.md` : cohérents avec le protocole et la nouvelle architecture ?
- Encodage : les .ps1 ont-ils un BOM UTF-8 ? Les .md et .yaml n'en ont PAS ?

### I. Fidélité de la transposition v5 → v6 (NOUVELLE DIMENSION — CRITIQUE)

Le backlog v6 a été produit en transposant le v5 en une seule session. Vérifier qu'aucun
contenu critique n'a été perdu. Méthode : pour chaque lot v5, comparer avec son équivalent v6
(`git show 223f0f3~1:orchestration/items/<lot>.yaml` vs le fichier actuel).

- **Lots transposés** (TVA, VAL, TRK, PAA, PAB, PAS, PIP, ADP, DOC, CMP) : chaque règle
  fiscale, chaque cas limite, chaque garde-fou, chaque critère d'acceptation « anti-faux-vert »
  du v5 est-il présent dans le v6 ? (ex. : le test-espion lecture seule d'ADP02, l'exception
  La Poste de VAL02, l'imputation prorata de PIP03, le chaînage des addenda de l'ex-TRK06,
  les 3 familles d'erreurs de PAB02, l'idempotence des rectificatifs PIP04...)
- **Lots supprimés** (SVC, CLI, WPF, PKG) : chaque exigence du v5 a-t-elle un nouveau foyer ?
  - SVC01 (ordonnanceur) → module Job / PIP01 ; SVC02 (montre morte) → SUP01 ;
    SVC03 (notifications SMTP) → SUP03 ; où est passé chaque point ?
  - CLI01 (check-config, encrypt-secret, run de secours, backup, verify-archive, audit-export) →
    AGT05 + OPS01 + API03 ; TOUT est-il couvert ? (ex. : verify-archive existe-t-il encore ?)
  - WPF01-08 → WEB01-08 : chaque écran, chaque action, chaque état d'affichage du v5 est-il
    dans le v6 ?
  - PKG01 (SQLite.Interop par plateforme, packaging.json piloté par les gates) → OPS05 :
    tout est-il repris ?
- **Leçons des reviews v4/v5** (`tasks/lessons.md` + decisions.md) : les corrections issues
  des deux reviews précédentes (faux vert codex-review -Base, machine à états, supersede,
  PIP04 rectificatifs, heuristique B2B alignée spec, garde production...) sont-elles toujours
  présentes dans le v6 ? Une régression sur une leçon déjà apprise est un P1.
- Tout contenu v5 perdu sans justification (l'analyse d'impact §3 documente ce qui devait
  être remplacé) est un P1.

### J. Hypothèses sur le socle Stratum (NOUVELLE DIMENSION)

Les items v6 supposent que le socle Stratum (`C:\Source\Stratum`, qui sera vendored par SOL01)
fournit certaines briques. Vérifier ces hypothèses SUR PIÈCE (lecture seule de C:\Source\Stratum) :

- Le module **Job** fournit : jobs planifiés cron, retries, dead letter (supposé par PIP01,
  SUP01, TRK06...). Vrai ?
- Le module **Identity** fournit : RBAC par permissions, policies nommées (supposé par API01-04,
  3 niveaux de droits + supervision). Le modèle de permission de Stratum permet-il les
  permissions par tenant telles que décrites ?
- Le module **Notification** fournit : envoi d'emails + modèle ApiKey prefix+hash (supposé par
  SUP03, PIV05). Vrai ? Le modèle ApiKey est-il réutilisable pour les agents ou faut-il le
  réimplémenter ?
- **Database-per-tenant** : Stratum implémente quoi exactement (ADR-0010 schema-per-tenant vs
  ADR-0011 database-per-tenant) ? Les items v6 et le blueprint citent database-per-tenant —
  est-ce l'état réel du code Stratum ?
- **Outbox / événements** : le pattern outbox supposé par PIV04 (événement DocumentReceived)
  existe-t-il dans Stratum tel que décrit ?
- **NetArchTest + Playwright** : l'infrastructure de test supposée existe-t-elle ?
- **Modules autonomes** : Identity/Job/Notification/Audit sont-ils réellement sans dépendance
  vers les modules ERP (Party, Sales...) ? Si une dépendance cachée existe, SOL01 va échouer.
- Toute hypothèse fausse = P1 (l'item concerné bloquera pendant l'orchestration).

## Livrable

Un rapport unique : `tasks/review-independante-v6.md` (c'est le SEUL fichier que tu crées).

Structure du rapport :

```markdown
# Review indépendante — Liakont backlog v6
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

1. Lis la doctrine (1-6) toi-même, puis parallélise les dimensions A à J avec des subagents
   (une dimension = un subagent ; les dimensions I et J sont les plus longues, lance-les en premier).
2. Pour la dimension A, tu peux exécuter des vérifications par script (lecture seule).
3. Pour la dimension I, utilise `git show 223f0f3~1:<chemin>` pour lire le contenu v5.
4. Consolide, vérifie chaque finding sur pièce, écris le rapport.
5. Termine en affichant la synthèse (compte P1/P2 + verdict) dans ta réponse.
