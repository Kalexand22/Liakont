# F13 — Installateur de l'agent & profils intégrateur
### Document de conception — `liakont-agent` (installateur GUI + packaging par profil)

> Statut : 🟨 conception interne (2026-06-03, session interactive Karl). À revoir ensemble.
> Référence : `blueprint.md` v2 §3-4, `docs/conception/F12` (architecture agent, contrat,
> déploiement), `docs/adr/ADR-0005` (agent en dépôt séparé + contrat NuGet),
> `CLAUDE.md` règles 5-12.
>
> Ce document spécifie la **mise en service guidée** de l'agent : un installateur à écrans, et son
> **paramétrage par profil intégrateur** (un même code, des `.exe` qui n'exposent que ce qui
> concerne chaque intégrateur). Il complète F12 §6 (packaging/déploiement) et OPS05 (packaging).

---

## 1. Objet et périmètre

Aujourd'hui, la mise en service de l'agent repose sur **OPS05** (packaging x86/x64 pré-configurable,
install silencieuse) et **AGT05** (CLI de diagnostic : `check-config`, `test-odbc`, `test-api`,
`encrypt`, `run`…). Il manque une **expérience GUI guidée** pour l'intégrateur, et un moyen de
**moduler les écrans selon l'intégrateur**.

F13 ajoute :

1. Un **installateur à écrans guidés** (bootstrapper GUI) qui pose l'agent, le configure et le teste.
2. Un **profil intégrateur déclaratif** qui décide, par packaging, quels écrans/options sont
   affichés, verrouillés ou masqués — produisant **un `.exe` par intégrateur à partir du même code**.

**Hors périmètre** : aucune logique métier (l'agent reste « extraction + transport », F12 §2,
`CLAUDE.md` règle 6). L'installateur ne fait que poser, configurer, tester, transmettre.

## 2. Vocabulaire — trois axes de variabilité à ne pas confondre

| Axe | Varie selon… | Porté par | Exemple |
|---|---|---|---|
| **Profil intégrateur** | l'intégrateur / partenaire qui déploie | l'installateur (ce doc) | branding, écrans visibles, options verrouillées |
| **Config tenant** | le client final (1 tenant = 1 SIREN) | wizard + `deployments/<client>/` + DPAPI | chaîne ODBC, SIREN, clé API agent |
| **Adaptateur** | le logiciel source (EncheresV6, Sage, AS400…) | plug-in `Agent.Adapters.*` | requêtes ODBC d'extraction |

Un **intégrateur** installe potentiellement chez **plusieurs clients finaux** avec le même `.exe`.
La décision « **générique par intégrateur** » (ADR-0005, validée le 2026-06-03) : le `.exe` est
paramétré par profil intégrateur (branding + visibilité) ; la **config tenant est saisie dans le
wizard** à chaque installation, jamais embarquée dans le binaire versionné.

## 3. Architecture de l'installateur

**Un bootstrapper GUI (`.exe`), .NET Framework 4.8, WinForms** (zéro dépendance externe, démarrage
immédiat, suffisant pour des écrans de formulaire). Il **réutilise** la logique déjà spécifiée — il
ne la duplique pas :

| Étape de l'installateur | S'appuie sur |
|---|---|
| Pose des binaires + création du service Windows + ACL `ProgramData` | self-install AGT01 + OPS05 |
| Écriture de `agent.json` avec secrets **chiffrés DPAPI** | `SecretProtector` (AGT01) |
| Test de la base source (lecture seule) | `IExtractor.CheckHealth` / `test-odbc` (AGT05) |
| Test du serveur central (heartbeat à blanc) | `test-api` (AGT05) |

Conséquence : l'installateur est une **couche de présentation** au-dessus de `Liakont.Agent.Core` et
des commandes d'AGT05. Le projet proposé est `Liakont.Agent.Installer` (voir §8). Une installation
**silencieuse** (OPS05, paramètres en ligne de commande / fichier de réponses) reste possible pour
les déploiements en masse : le wizard et le mode silencieux partagent le même moteur de
configuration.

## 4. Écrans guidés

L'ordre et la présence de chaque écran sont **gouvernés par le profil intégrateur** (§5).

### 4.1 Connexion à la base source (lecture seule)
- Choix de l'**adaptateur** (parmi les plug-ins embarqués) — masquable si l'intégrateur n'en a qu'un.
- Paramètres de connexion **ODBC** (DSN ou chaîne ; paramètres avancés repliables).
- Bouton **« Tester »** → `CheckHealth` : tables détectées + comptage, **aucune écriture, aucun
  verrou** (F12, `CLAUDE.md` règle 5). Les identifiants sont **chiffrés DPAPI** avant écriture.

### 4.2 Connexion au serveur centralisé
- **URL de la plateforme** (peut être imposée par le profil).
- **Clé API agent** (saisie ; jamais embarquée dans le `.exe` — §6).
- Bouton **« Tester »** → heartbeat à blanc : diagnostique URL injoignable / clé invalide / clé
  révoquée / OK.

### 4.3 Options (selon profil)
Planification d'extraction, dossier du pool PDF, niveau/rétention des logs, auto-update : affichés,
verrouillés ou masqués selon le profil, avec une **valeur par défaut** quand masqués.

À la fin : récapitulatif → écriture de `agent.json` (secrets DPAPI) → démarrage du service →
`check-config` final affiché en clair (✅/❌ par point).

## 5. Le profil intégrateur (déclaratif)

### 5.1 Principe — data-driven, jamais `if(integrateur)`
Le comportement de l'installateur est **piloté par les données d'un profil**, jamais par une branche
conditionnelle codée en dur sur l'identité de l'intégrateur. C'est l'exact pendant des **capacités
PA déclarées** (`blueprint.md` §2 règle 2 : pas de `if (pa is B2Brouter)`) et du **branding
d'instance** côté plateforme. Un `if (integrateur == "X")` dans le code est un **P1** (même esprit
que `CLAUDE.md` règles 14/16).

### 5.2 Schéma du profil
Un manifeste (JSON/YAML) embarqué au packaging, déclarant :
- **Branding** : nom, logo, couleurs, libellés.
- **Par champ / écran** : un état parmi `affiché` | `verrouillé` (visible, non éditable) | `masqué`,
  plus une **valeur par défaut/imposée**.

```yaml
# exemple FICTIF (config/exemples/) — aucun secret, aucune donnée client
profil: "exemple-integrateur"
branding: { nom: "Exemple Intégrateur", logo: "logo.png", couleurPrincipale: "#0a5" }
champs:
  adapter:        { etat: masqué,     valeur: "EncheresV6" }
  platformUrl:    { etat: verrouillé, valeur: "https://exemple.tld" }
  apiKey:         { etat: affiché }                 # requis, saisi au wizard
  odbcConnection: { etat: affiché }                 # requis, saisi au wizard
  odbcAdvanced:   { etat: masqué }
  schedule:       { etat: affiché,    valeur: "0 */2 * * *" }
  logging:        { etat: masqué,     valeur: "info/90j" }
  autoUpdate:     { etat: verrouillé, valeur: true }
```

### 5.3 Règle par défaut et garde-fous (anti-faux-vert)
- **Défaut ouvert** : un champ **non déclaré** est `affiché` + éditable (l'intégrateur voit tout
  sauf ce qu'on masque explicitement).
- **Masquer impose une valeur** : `masqué` sans `valeur` est **invalide** (sinon donnée implicite
  cachée dans le code).
- **Champs vitaux toujours résolus** : `platformUrl` et `apiKey` sont **requis** ; ils ne peuvent
  pas être `masqué` sans valeur (un agent non joignable serait non fonctionnel). `apiKey` ne peut
  pas être imposée par le profil (secret — §6).
- **Validation au packaging** : un profil avec une **clé inconnue**, un `masqué` sans valeur, ou un
  champ requis non résolu **fait échouer la construction du package**. Une faute de frappe qui ne
  masque rien silencieusement est un faux vert (cf. `tasks/lessons.md` 2026-06-02, gardes
  anti-faux-vert) — interdit.

### 5.4 Liste initiale (NON figée, extensible)
On démarre avec : `adapter`, `platformUrl`, `apiKey`, `odbcConnection`, `odbcAdvanced`, `schedule`,
`pdfPoolPath`, `logging`, `autoUpdate`. **La liste n'est pas verrouillée** : ajouter une option
profilable plus tard = **ajouter une clé** au schéma + déclarer sa visibilité dans le wizard, **sans
toucher au moteur** (le moteur itère sur les champs déclarés, il ne les énumère pas en dur).

### 5.5 Sécurité du profil
Le profil ne contient **que** du branding et de la visibilité : **aucun secret, aucune donnée
client** (`CLAUDE.md` règles 7/10). Il peut être **signé** pour empêcher un intégrateur de
réafficher un écran masqué (intégrité), mais ce n'est pas un secret en soi.

## 6. Sécurité — invariants (P1 en review)

1. **Aucun secret en clair dans le `.exe` versionné.** Clé API et identifiants ODBC sont **saisis
   dans le wizard** puis **chiffrés DPAPI** (machine scope, AGT01). Jamais committés, jamais logués
   (`CLAUDE.md` règles 10/12).
2. **Variante optionnelle « pré-rempli pour un client »** : réutilise le mécanisme **OPS05/OPS03**
   (clé API chiffrée par un mot de passe à usage unique communiqué séparément) — jamais une clé en
   clair dans le package. Ce n'est PAS le mode par défaut (qui est « générique par intégrateur »).
3. **Le profil intégrateur** = branding + visibilité uniquement, signable, sans secret ni donnée
   client.
4. **Lecture seule de la base source** preservée par le wizard (le test = `CheckHealth`, aucune
   écriture/verrou).

## 7. Packaging — un `.exe` par profil (réalisé par OPS08, réutilise OPS05)

Le packaging `tools/package-agent.ps1` (OPS05) est **réutilisé par OPS08** : à partir **du même
binaire produit** + **N profils intégrateur**, OPS08 génère **N `.exe`** d'installation, chacun avec
son profil **embarqué en ressource** (et son branding). Pipeline :

1. Build Release x86/x64 de la solution agent (inchangé, OPS05 — règle de bitness ODBC inchangée).
2. Pour chaque profil : **valider** le profil (§5.3 — échec si invalide) → embarquer profil +
   branding → produire le bootstrapper GUI auto-suffisant.
3. Tester l'installation d'au moins un profil sur machine propre (service démarre, `check-config`
   passe).

**Même code, des `.exe` différents** : aucune branche conditionnelle par intégrateur dans le code.

## 8. Items de backlog (intégrés — manifest v8 + state.yaml)

> ✅ **Intégrés le 2026-06-03** dans `manifest.yaml` (v8), `orchestration/items/OPS.yaml` et
> `$ORCH_REPO/state.yaml` (OPS08 `pending`) — opération atomique, faite alors qu'aucune session
> d'orchestration n'était active. Rappel du piège évité : un item au `manifest.yaml` mais **absent
> de `state.yaml`** est traité « done & purgé » par l'orchestration (`protocol.md` Step 1/2). Lot
> **OPS**, segment `deploiement-toolkit`.

**OPS08 — Installateur GUI de l'agent + moteur de profil intégrateur**
- `lot: OPS`, `depends_on: [OPS05, AGT05]`, `priority: ~7035` (entre OPS05=7030 et OPS03=7040),
  `blueprint: tooling-item`.
- **Description** : `Liakont.Agent.Installer` (WinForms net48) — bootstrapper GUI réutilisant
  `Agent.Core` + commandes AGT05 (test-odbc/test-api/encrypt) ; écrans §4 ; moteur de profil §5
  (3 états + défaut ouvert + garde-fous + validation) ; **packaging d'un installateur par profil**
  (§7, réutilise OPS05) ; écriture `agent.json` DPAPI ; mode silencieux partagé. Profil d'exemple
  FICTIF dans `config/exemples/`.
- **Acceptance** : wizard déroule connexion source + serveur central + tests verts ; profil pilote
  la visibilité (affiché/verrouillé/masqué + défaut) ; **aucun secret dans le `.exe`** (vérifié) ;
  profil invalide rejeté **au packaging** (testé) ; `if(integrateur)` absent du code (review) ;
  installation testée sur machine propre.

**OPS05 — note de renvoi (pas d'extension fonctionnelle)**
- La génération **multi-profils** (un `.exe` par profil — §7) est portée par **OPS08**, qui réutilise
  le packaging d'OPS05. OPS05 reçoit une simple note de renvoi vers OPS08/F13 — choix qui évite une
  **dépendance circulaire** OPS05↔OPS08.

GATE concerné : `GATE_TOOLKIT` — son `depends_on` inclut désormais OPS08.

## 9. Frontières & conformité (rappels)

- **Aucune logique métier** dans l'installateur (pas de TVA, validation, états) — `CLAUDE.md`
  règle 6.
- **Lecture seule stricte** de la base source, y compris depuis le wizard — règle 5.
- **Aucune donnée client dans le code** : profils d'exemple **fictifs** dans `config/exemples/` ; le
  paramétrage réel est saisi (wizard) ou versionné dans `deployments/<client>/` — règle 7.
- **Secrets chiffrés** (DPAPI), jamais en clair dans un fichier versionné ou un log — règle 10.
- **Messages opérateur en français**, orientés intégrateur — règle 12.
