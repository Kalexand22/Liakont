# Installation de l'agent Liakont

> Public : **intégrateur / éditeur** qui déploie l'agent sur le poste ou le serveur du client final.
> L'agent extrait les données de la base source (ODBC, **lecture seule**), les met en tampon local
> et les transmet en HTTPS à la plateforme. Il n'embarque **aucune logique métier**.
>
> Sources : `docs/conception/F12-Architecture-Plateforme-Agent.md`,
> `docs/adr/ADR-0019-format-paquet-installeur-agent.md` (format de package),
> `docs/adr/ADR-0013-modele-confiance-auto-update-agent.md` (auto-update). Messages et CLI 100 %
> français.

---

## 1. Prérequis du poste cible

| Prérequis | Détail |
|---|---|
| Système | Windows 7 SP1+ / Windows Server 2012+ |
| Runtime | **.NET Framework 4.8** installé |
| Driver source | Le driver ODBC de la base source (ex. **Pervasive 32 bits** pour EncheresV6) installé et un DSN/chaîne de connexion valides |
| Droits | **Administrateur** pour l'installation (création du service Windows + ACL) ; le CLI de diagnostic tourne sous le compte de l'intégrateur |
| Réseau | HTTPS sortant vers l'URL de la plateforme (la clé API et les données fiscales ne transitent jamais en clair) |

## 2. Choisir x86 ou x64 — RÈGLE

> **x86 par DÉFAUT** dès qu'**un** adaptateur ODBC 32 bits est requis : un service x64 ne peut pas
> charger un driver 32 bits (cas du driver Pervasive d'EncheresV6). **x64 seulement si TOUS** les
> adaptateurs du client sont 64 bits. **Le service et ses adaptateurs partagent le même bitness** —
> jamais de mélange.

Chaque package (`Liakont.Agent-<version>-x86.zip` / `-x64.zip`) embarque l'EXE et le
`SQLite.Interop.dll` natif de **sa** plateforme uniquement.

## 3. Fabriquer le package (côté éditeur)

```powershell
# Les deux plateformes
powershell -ExecutionPolicy Bypass -File tools/package-agent.ps1

# x86 uniquement (cas EncheresV6)
powershell -ExecutionPolicy Bypass -File tools/package-agent.ps1 -Platform x86
```

Le script construit la solution agent en Release (x86 et/ou x64), assemble chaque package, **vérifie
le bitness** de l'EXE et du natif SQLite, puis produit les `.zip` dans `artifacts/agent-packages/`
(avec leur empreinte SHA-256).

### Package PRÉ-CONFIGURÉ pour un tenant (optionnel)

```powershell
powershell -ExecutionPolicy Bypass -File tools/package-agent.ps1 -Platform x86 `
  -PreConfigPlatformUrl https://liakont.editeur-x.fr -PreConfigAdapter EncheresV6 `
  -PreConfigApiKey '<clé API du tenant>' -PreConfigSchedule 03:00
```

La clé API est embarquée **chiffrée** (jamais en clair). Le script affiche un **mot de passe à usage
unique** : communiquez-le à l'intégrateur **par un canal séparé** du package (voir
[ADR-0019](adr/ADR-0019-format-paquet-installeur-agent.md)).

## 4. Installer (côté poste cible)

Extraire le `.zip`, ouvrir une console **administrateur** dans le dossier extrait :

```powershell
# Instance par défaut
powershell -ExecutionPolicy Bypass -File install-agent.ps1

# Instance nommée (serveur mutualisé : une instance par base cliente)
powershell -ExecutionPolicy Bypass -File install-agent.ps1 -InstanceName ClientA -Silent

# Package pré-configuré : fournir le mot de passe à usage unique reçu séparément
powershell -ExecutionPolicy Bypass -File install-agent.ps1 -InstanceName ClientA -PreConfigPassword <mot-de-passe>

# Valider le plan SANS rien modifier (ne requiert pas l'élévation)
powershell -ExecutionPolicy Bypass -File install-agent.ps1 -InstanceName ClientA -DryRun
```

L'installeur : copie les binaires sous `%ProgramFiles%\Liakont\Agent\<instance>`, crée le répertoire
de données `%ProgramData%\Liakont\<instance>` avec des **ACL en moindre privilège** (héritage retiré ;
accès au seul **SYSTEM** — le service — et aux **Administrateurs**), génère `agent.json` si le package
est pré-configuré, provisionne la clé publique de signature d'auto-update si elle est fournie, puis
enregistre le service Windows.

> **CLI lancé par un compte NON administrateur** : le CLI partage la file SQLite avec le service et
> doit donc pouvoir y écrire. Donnez le droit Modify à ce compte dédié à l'installation :
> `install-agent.ps1 -InstanceName ClientA -IntegratorAccount CONTOSO\liakont-ops`. Par défaut
> (CLI lancé en administrateur), aucun compte supplémentaire n'est nécessaire. Sur un hôte mutualisé,
> ce moindre privilège garantit qu'aucun utilisateur local n'accède au tampon de données fiscales
> d'un autre tenant.

Démarrer ensuite le service :

```powershell
sc start "LiakontAgent"            # instance Default
sc start "LiakontAgent$ClientA"    # instance nommée
```

## 5. Multi-instances (serveur SaaS mutualisé)

Un serveur hébergeant N bases clientes installe **N instances**, une par base :

| Élément | Instance « Default » | Instance « ClientA » |
|---|---|---|
| Service Windows | `LiakontAgent` | `LiakontAgent$ClientA` |
| Données | `%ProgramData%\Liakont` | `%ProgramData%\Liakont\ClientA` |
| Binaires | `%ProgramFiles%\Liakont\Agent\Default` | `%ProgramFiles%\Liakont\Agent\ClientA` |
| Verrou de run | `Global\LiakontAgentRun` | `Global\LiakontAgentRun-CLIENTA` |

Les instances tournent **sans interférence** (verrous distincts, files distinctes). Nom d'instance :
lettres/chiffres/`-`/`_`, 32 caractères max, première position alphanumérique ; les noms `logs`,
`update-work` et les périphériques réservés Windows (CON, PRN, COM1…) sont refusés. L'instance
**Default** conserve strictement les noms et chemins historiques (compatibilité des installations
mono-instance déjà déployées).

Désinstaller **une** instance sans toucher aux autres :

```powershell
powershell -ExecutionPolicy Bypass -File uninstall-agent.ps1 -InstanceName ClientA
# Avec suppression des données (IRRÉVERSIBLE) :
powershell -ExecutionPolicy Bypass -File uninstall-agent.ps1 -InstanceName ClientA -RemoveData
```

> `-RemoveData` sur l'instance **Default** ne supprime que les fichiers propres à Default ; les
> sous-dossiers des instances nommées (qui vivent sous la même racine) sont préservés.

## 6. Configurer manuellement `agent.json` (sans pré-configuration)

Copier le modèle [`config/exemples/agent.json`](../config/exemples/agent.json) dans le répertoire de
données de l'instance, puis :

1. **Chiffrer la clé API** (jamais en clair) — la valeur est lue sur l'entrée standard :
   ```powershell
   Liakont.Agent.Cli.exe encrypt
   ```
   Coller la valeur chiffrée dans le champ `apiKey`.
2. Chiffrer de même une éventuelle chaîne ODBC (`extraction.odbcConnectionString`).
3. Renseigner `platformUrl` (HTTPS), `extraction.adapter` (ex. `EncheresV6`), `schedule`.

## 7. Vérifier l'installation (CLI de diagnostic)

```powershell
Liakont.Agent.Cli.exe check-config             # agent.json bien formé, secrets déchiffrables, adaptateur connu
Liakont.Agent.Cli.exe test-odbc                # connexion ODBC en LECTURE SEULE
Liakont.Agent.Cli.exe test-api                 # heartbeat à blanc vers la plateforme
# Cibler une instance nommée :
Liakont.Agent.Cli.exe --instance ClientA check-config
```

Codes de retour : `0` = OK, `1` = problème détecté, `2` = erreur d'exécution.

## 8. Mise à jour

- **Auto-update** (flotte) : si la clé publique de signature a été provisionnée
  (`update-signing.pubkey.xml`), l'agent applique les mises à jour signées poussées par la
  plateforme (ADR-0013, fail-closed sans clé).
- **Manuelle** : réinstaller un package plus récent avec le **même** `-InstanceName` (les binaires
  sont remplacés ; les données et la file en attente sont conservées).

## 9. Dépannage

| Symptôme | Cause probable | Action |
|---|---|---|
| Le service ne démarre pas, erreur de chargement du driver ODBC | Mauvais bitness (service x64, driver 32 bits) | Réinstaller le package **x86** (voir §2) |
| `test-odbc` échoue | DSN/chaîne ODBC absente ou invalide, driver non installé | Vérifier le driver source et `extraction.odbcConnectionString` |
| `test-api` : « clé invalide » ou « clé révoquée » | Clé API erronée ou révoquée côté plateforme | Régénérer la clé (console plateforme) et reconfigurer `apiKey` |
| `check-config` : « clé API non déchiffrable » | `agent.json` copié d'une autre machine (DPAPI portée machine) | Re-chiffrer la clé **sur cette machine** via `encrypt` |
| `install-agent.ps1` : « Droits administrateur requis » | Console non élevée | Relancer « en tant qu'administrateur » |
| Deux instances écrivent la même file | Noms d'instance identiques (insensible à la casse pour les chemins) | Choisir des noms distincts |
