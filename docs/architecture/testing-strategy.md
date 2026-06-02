# Stratégie de test — Conformat

> Document de stratégie de test (item SOL03). Définit la pyramide de tests, la **taxonomie de
> référence des catégories xUnit** (rattachée par `definition-of-done.md`), les conventions de
> nommage et la discipline « pas de faux vert ». Source : `blueprint.md` §8, `docs/adr/0001-socle-technique.md`
> §3, les scripts réels `tools/verify-fast.ps1` et `tools/run-tests.ps1`, `.github/workflows/ci.yml`,
> `docs/conception/`. Rien n'est inventé.
>
> Documents liés : [repo-standards.md](repo-standards.md), [module-rules.md](module-rules.md).

---

## 1. La pyramide de tests

blueprint.md §8 fixe les niveaux. Chaque niveau dit *quoi* et *quand* :

| Niveau | Quoi | Quand (outil) |
|---|---|---|
| **Unit** | Toutes les couches du Core, les ViewModels WPF, les contrats Api | `verify-fast` (chaque commit) |
| **Contrat PA** | `Gateway.PaClients.Contract.Tests` sur chaque plug-in PA, via mock HTTP | `run-tests` (avant review) |
| **Integration** | SQLite réel (base temp), fixtures EncheresV6, FakePaClient, API in-process | `run-tests` |
| **Parité** | `FixtureExtractor` vs `PervasiveExtractor` mocké sur le même jeu | `run-tests` |
| **Staging / Sandbox** | Envois **réels** B2Brouter staging / Super PDP sandbox | **Manuel uniquement, jamais en CI** — avant chaque gate PA |
| **Smoke WPF** | Checklists manuelles par écran (`docs/architecture/smoke-checklists/`) | Avant chaque gate console |

Principe : le bas de la pyramide (unit) est large, rapide et joué à chaque commit ; le haut
(staging/sandbox, smoke) est étroit, lent ou manuel et joué avant les gates.

## 2. Taxonomie de référence des catégories xUnit

C'est la **source de vérité** des catégories de tests du dépôt (rattachement explicite depuis
`definition-of-done.md`). Une catégorie est portée par un trait xUnit `[Trait("Category", "<nom>")]`.

| Catégorie (`Category`) | Désigne | Ressource requise |
|---|---|---|
| *(aucune)* | **Test unitaire** : pur, en mémoire, déterministe | Aucune (CPU seul) |
| `Integration` | Intégration **in-process** : SQLite base temp, fixtures, FakePaClient, API in-process, contrat PA via mock HTTP | Aucune ressource externe |
| `Integration.SqlServer` | Intégration nécessitant **SQL Server LocalDB** | SQL Server LocalDB (optionnel) |
| `Staging` | Envoi **réel** vers l'API B2Brouter de staging | Clé API B2Brouter (secret local) |
| `Sandbox` | Envoi **réel** vers l'API Super PDP de sandbox | Compte sandbox Super PDP |

### 2.1 Règle de pose des traits (l'invariant qui rend les filtres cohérents)

**Tout test qui n'est pas un test unitaire pur porte `Category=Integration` comme trait de base,
plus un trait de spécialisation quand il dépend d'une ressource externe ou optionnelle :**

| Type de test | Traits à poser |
|---|---|
| Unitaire | *(aucun)* |
| Intégration in-process | `Category=Integration` |
| Intégration SQL Server | `Category=Integration` **et** `Category=Integration.SqlServer` |
| Staging B2Brouter (envoi réel) | `Category=Integration` **et** `Category=Staging` |
| Sandbox Super PDP (envoi réel) | `Category=Integration` **et** `Category=Sandbox` |

Cet invariant — « non-unitaire ⟹ porte `Integration` » — est ce qui garantit que `verify-fast`
(qui exclut `Integration`) ne joue **que** des tests unitaires, sans avoir à énumérer chaque
sous-catégorie.

## 3. Mapping catégories ↔ filtres des scripts (harmonisation)

`definition-of-done.md` signale un **écart latent** entre les filtres (aucun test ne porte encore
de catégorie aujourd'hui ; l'écart ne mord donc pas, mais doit être cadré ici). Filtres réels :

| Outil | Filtre `dotnet test` | Joue | Exclut |
|---|---|---|---|
| `verify-fast.ps1` | `Category!=Integration&Category!=Staging` | Unitaires | Integration, Integration.SqlServer, Sandbox, Staging |
| `run-tests.ps1` | `Category!=Staging&Category!=Sandbox&Category!=Integration.SqlServer` | Unitaires + Integration in-process (dont contrat PA) | Staging, Sandbox, Integration.SqlServer |
| CI (`ci.yml`) | identique à `run-tests.ps1` | idem `run-tests` | idem `run-tests` |

**Pourquoi c'est cohérent sous l'invariant §2.1 :**

- `verify-fast` exclut `Integration` → exclut donc **tout** test non unitaire (puisque tous portent
  `Integration`), y compris SqlServer/Staging/Sandbox. La clause `&Category!=Staging` est une
  défense-en-profondeur redondante, conservée volontairement.
- `run-tests` / CI **ne** filtrent **pas** `Integration` → ils jouent l'intégration in-process
  voulue, mais excluent les trois catégories qui exigent une ressource externe ou des envois réels
  (`Staging`, `Sandbox`, `Integration.SqlServer`).

> **Conséquence opérationnelle** : le jour où l'on ajoute un test SQL Server, Staging ou Sandbox,
> il **doit** porter `Category=Integration` en plus de sa spécialisation, sinon `verify-fast`
> l'exécuterait (un test SQL Server ou un envoi réel dans une vérification « rapide » locale =
> faux rouge / effet de bord). Cet invariant est la condition de validité des filtres ci-dessus ;
> tout test qui le viole est un trou de test à corriger en review.

## 4. Catégories par projet de test

Les 8 projets de test (blueprint.md §4, ADR-0001 §3) et le niveau de pyramide qu'ils portent :

| Projet de test | Contenu | Catégories attendues |
|---|---|---|
| `Gateway.Core.Tests` | Unit Core + intégration SQLite/fixtures/API in-process + garde de frontières (`ProjectReferenceBoundaryTests`) | *(aucune)* pour l'unit ; `Integration` pour l'intégration |
| `Gateway.PaClients.Contract.Tests` | Suite de contrat PA via mock HTTP — **tout** plug-in PA doit la passer | `Integration` |
| `Gateway.PaClients.Fake.Tests` | Plug-in PA factice (preuve du modèle plug-in) | *(aucune)* / `Integration` |
| `Gateway.PaClients.B2Brouter.Tests` | Unit du plug-in B2Brouter (+ suite **staging** séparée) | *(aucune)* ; `Staging` pour les envois réels |
| `Gateway.PaClients.SuperPdp.Tests` | Unit du plug-in Super PDP (+ suite **sandbox** séparée) | *(aucune)* ; `Sandbox` pour les envois réels |
| `Gateway.Adapters.EncheresV6.Tests` | Extraction + parité fixture/Pervasive mocké | *(aucune)* ; `Integration` pour la parité |
| `Gateway.Service.Tests` | API + ordonnanceur | *(aucune)* ; `Integration` pour l'API in-process |
| `Gateway.App.Tests` | ViewModels WPF (jamais le code-behind) | *(aucune)* |

> La frontière App ↔ API impose que `Gateway.App.Tests` teste des **ViewModels**, pas un accès
> base ou Core (voir [module-rules.md](module-rules.md) §2). Un item WPF sans tests ViewModel est
> un P1 (CLAUDE.md règle 19).

## 5. Conventions de nommage des tests

- **Classe de test** : `<TypeTesté>Tests` (ex. `TvaMapperTests`, `ValidationPipelineTests`).
- **Méthode de test** : `Méthode_Condition_RésultatAttendu`
  (ex. `Map_RegimeInconnu_RetourneNonMappe`, `Round_HalfUp_DeuxDecimales`). En français quand cela
  clarifie l'intention métier ; les termes techniques restent en anglais.
- **Trait de catégorie** : `[Trait("Category", "Integration")]` (et la spécialisation, cf. §2.1).
- **Suites staging/sandbox** : isolées dans des classes dédiées portant `Staging`/`Sandbox`, jamais
  mélangées avec l'unit du même plug-in.

## 6. Pipeline de validation d'un changement

blueprint.md §9, definition-of-done.md :

```
code → verify-fast (build x86 + analyzers + tests unitaires)
     → run-tests (intégration + contrat PA) [si l'item en a]
     → codex-review (P1/P2, rounds jusqu'à clean)
     → merge --no-ff dans la branche de segment
     → gate humaine (PR + validation fonctionnelle) avant main
```

- `verify-fast.ps1` construit **x86** (plateforme contraignante des drivers ODBC Pervasive 32 bits)
  pour rester rapide ; la couverture **x64** est assurée par la CI (matrice x86/x64). Log détaillé
  dans `.verify-fast.log`.
- `run-tests.ps1` exécute la suite complète moins Staging/Sandbox/SqlServer ; résumé compact sur
  stdout, détail dans `.run-tests.log`.
- La CI (`.github/workflows/ci.yml`) construit les **deux** plateformes en Release et joue les
  tests ; une étape en échec fait échouer le pipeline (aucun `continue-on-error`).

## 7. Faux verts interdits (P1 en review)

blueprint.md §8 — chacun de ces cas est un P1 :

- un test écrit mais **jamais exécuté** (tasks/lessons.md : un test jamais lancé est un faux vert) ;
- une assertion affaiblie pour faire passer ;
- un `[Fact(Skip="…")]` posé pour masquer un échec ;
- un script de vérification qui réussit là où il devrait échouer (la règle SOL02 — « tester le
  scénario *ça DOIT échouer* avant de se fier à un outil » — s'applique à tout l'outillage).

Les tests sont **EXÉCUTÉS**, pas seulement écrits, avant de déclarer un item done
(definition-of-done.md).

## 8. Tests d'intégration : ressources et isolation

- **SQLite** : chaque test d'intégration crée sa base dans un fichier **temporaire** dédié, jamais
  une base partagée ni versionnée (`*.db` est dans `.gitignore`). Le Service reste le seul écrivain
  dans le produit ; les tests ouvrent leur propre base jetable.
- **API in-process** : l'API HTTP self-hosted est lancée en in-process pour les tests `Integration`,
  sans dépendre d'un service Windows installé.
- **FakePaClient / mock HTTP** : le contrat PA et le pipeline se testent sans réseau réel ; les
  envois réels sont réservés aux catégories `Staging`/`Sandbox` (manuel, hors CI).
- **Données de test** : uniquement des codes **fictifs** de `config/exemples/`, jamais une table TVA
  ou un SIREN réels (voir [module-rules.md](module-rules.md) §7).

## 9. Smoke WPF (avant les gates console)

Les écrans WPF ne sont pas couverts par des tests automatisés d'UI ; ils ont une **checklist smoke
manuelle** par écran dans `docs/architecture/smoke-checklists/`, mise à jour à chaque item WPF et
rejouée avant la gate `GATE_CONSOLE_ADMIN`. Les ViewModels, eux, ont des tests unitaires
obligatoires (CLAUDE.md règle 19, wpf-screen-item).
