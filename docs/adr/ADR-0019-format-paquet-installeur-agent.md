# ADR-0019 — Format du paquet d'installation de l'agent + transport de la clé de pré-configuration

- **Statut** : Accepté
- **Date** : 2026-06-11
- **Items** : OPS05 (packaging de l'agent), réutilisé par OPS08 (installeur GUI par profil intégrateur)
- **Contexte décisionnel** : `docs/conception/F12-Architecture-Plateforme-Agent.md` §2/§6,
  `docs/conception/F13-Installateur-Agent-Profils-Integrateur.md`, ADR-0003 (stack agent),
  ADR-0013 (confiance auto-update), `CLAUDE.md` n°7/n°10 (aucun secret en clair)

## Contexte

L'agent (.NET Framework 4.8, x86 **et** x64) doit être déployé chez l'éditeur / l'intégrateur :

1. **installation silencieuse** possible (un intégrateur enchaîne des postes) ;
2. **x86 ET x64** — le service et ses adaptateurs partagent un bitness (driver ODBC Pervasive
   d'EncheresV6 = 32 bits) ;
3. **multi-instances** — un serveur SaaS mutualisé héberge N bases clientes, donc N services
   `LiakontAgent$<nom>` sur une même machine (décision 2026-06-10) ;
4. **pré-configuration par tenant** optionnelle (OPS03 étape 4) : agent.json pré-rempli, **la clé
   API ne devant JAMAIS apparaître en clair** dans le package (`CLAUDE.md` n°10).

Deux questions à trancher : le **format de package** (ZIP vs MSI) et le **transport sécurisé de la
clé API** dans un package pré-configuré.

## Décision

### 1. Format = ZIP auto-suffisant + script PowerShell d'installation (pas MSI en V1)

`tools/package-agent.ps1` produit, par plateforme, un **ZIP** contenant les binaires net48
(framework-dependent, xcopy), le natif `SQLite.Interop.dll` de la plateforme, et les scripts
`install-agent.ps1` / `uninstall-agent.ps1` / `AgentInstall.psm1` (`deploy/agent-installer/`).

- **Installation silencieuse** : `powershell -ExecutionPolicy Bypass -File install-agent.ps1
  -InstanceName <nom> -Silent`. Critère du F13 satisfait sans chaîne d'outils MSI (WiX).
- **Multi-instances** : toute la dérivation (service, `%ProgramData%\Liakont\<instance>`, mutex de
  run, journaux) est portée par le script + `Liakont.Agent.Core.AgentInstance` — un MSI standard ne
  modélise pas nativement « N instances paramétrées par un nom ».
- **Pas de nouveau paquet NuGet** : PowerShell + BCL .NET uniquement → aucun avenant à ADR-0003.
- **Aucun secret par défaut** dans le ZIP versionné ; un package pré-configuré est un artefact
  GÉNÉRÉ (jamais committé).

**MSI = fast-follow** si un déploiement d'entreprise (GPO / SCCM) l'exige ; il réutiliserait le même
moteur de profil (OPS08). **Authenticode** (signature des binaires) s'ajoutera en complément dès
qu'un certificat de signature de code sera disponible — il ne remplace pas le manifeste signé de
l'auto-update (ADR-0013), il renforce la chaîne au niveau OS.

### 2. Un package PAR plateforme, élagué et vérifié (anti-faux-vert)

Chaque ZIP est mono-plateforme : l'EXE est **réellement** du bon bitness, et seul le sous-dossier
natif `SQLite.Interop.dll` correspondant est conservé (l'autre est élagué). `package-agent.ps1`
**vérifie** ces invariants avant de produire le ZIP : un package au mauvais bitness échoue le build,
il n'est jamais livré silencieusement. Le contrôle de l'EXE lit directement le PE — **champ Machine +
drapeau `COMIMAGE_FLAGS_32BITREQUIRED`** de l'en-tête CLR — pour distinguer x86 (32 bits forcé)
d'AnyCPU : le seul champ Machine rapporte I386 pour les deux et laisserait passer un EXE AnyCPU au
lieu d'un x86 forcé. Cette lecture d'octets est identique en .NET Framework et en .NET (pwsh, runtime
de la CI), contrairement à `AssemblyName.ProcessorArchitecture`, **obsolète sous .NET Core+**
(SYSLIB0037, rend `None`). Le natif `SQLite.Interop.dll` (non managé) est vérifié par son type
machine PE.

### 3. Transport de la clé API de pré-configuration = chiffrement par mot de passe à usage unique

Quand un package est pré-configuré (`-PreConfigApiKey`), la clé API est embarquée **chiffrée** dans
`preconfig.json` :

- dérivation **PBKDF2-SHA256** (200 000 itérations, sel aléatoire) d'un mot de passe à usage unique
  vers une clé de chiffrement + une clé MAC ;
- **AES-256-CBC** (IV aléatoire) ; intégrité **HMAC-SHA256** sur `IV || ciphertext`
  (chiffrer-puis-MAC, vérifié AVANT déchiffrement) ;
- le **mot de passe à usage unique** est généré par `package-agent.ps1` (ou fourni) et **communiqué
  SÉPARÉMENT** à l'intégrateur (hors package, jamais versionné, jamais journalisé).

À l'installation, `install-agent.ps1 -PreConfigPassword <mot de passe>` déchiffre la clé, puis la
**re-chiffre en DPAPI** (portée machine, via la commande CLI `encrypt` — source de vérité unique du
schéma DPAPI de l'agent) dans `agent.json`. La clé en clair ne touche jamais le disque et n'apparaît
jamais dans `preconfig.json` (vérifié par le contrôle anti-fuite de `package-agent.ps1`).

Propriété : un package pré-configuré intercepté SANS le mot de passe (transmis par un autre canal)
ne révèle pas la clé API ; un mot de passe erroné ou un package altéré échoue le contrôle d'intégrité
HMAC.

## Conséquences

- Le format ZIP + script est consommé tel quel par l'installeur GUI par profil (OPS08, F13) : même
  packaging, le profil ajoute branding + visibilité d'écrans.
- L'installeur provisionne aussi la **clé publique de signature** d'auto-update
  (`update-signing.pubkey.xml`, ADR-0013) quand elle est fournie au packaging.
- La validation « installation réelle sur machine propre + coexistence de 2 instances » est exercée
  à la gate humaine **GATE_TOOLKIT** (mutation de machine : service Windows, ACL) ; la logique pure
  (dérivation d'instance, bitness, transport chiffré) est couverte par `tools/test-agent-packaging.ps1`,
  **câblé dans `tools/run-tests.ps1` ET `.github/workflows/ci.yml`** (gate permanente, pas seulement
  un nœud d'orchestration ponctuel), plus le contrôle anti-faux-vert intégré de `package-agent.ps1`.
- Le répertoire de données (tampon SQLite des données fiscales extraites) est posé en **moindre
  privilège** : héritage retiré, accès explicite à SYSTEM (service) et aux Administrateurs
  uniquement ; un compte intégrateur non administrateur reçoit Modify seulement s'il est fourni
  explicitement (`-IntegratorAccount`). Aucun droit n'est accordé au groupe « Utilisateurs » — sur
  un hôte mutualisé, aucun utilisateur local n'accède au tampon d'un autre tenant.

## Références

- `docs/conception/F12-Architecture-Plateforme-Agent.md` §2 (poste client), §6 (déploiement)
- `docs/conception/F13-Installateur-Agent-Profils-Integrateur.md`
- ADR-0003 (stack de paquets de l'agent), ADR-0013 (confiance auto-update)
- `docs/installation-agent.md` (procédure intégrateur), `deploy/agent-installer/`, `tools/package-agent.ps1`
- `CLAUDE.md` n°7 (aucune donnée client dans le code), n°10 (secrets chiffrés)
