# Installeur de l'agent Liakont (`deploy/agent-installer/`)

Scripts d'installation **embarqués dans le package** produit par
[`tools/package-agent.ps1`](../../tools/package-agent.ps1) (item OPS05).

| Fichier | Rôle |
|---|---|
| `install-agent.ps1` | Installe une **instance** de l'agent (binaires, ACL `%ProgramData%\Liakont\<instance>`, service Windows, pré-configuration optionnelle). |
| `uninstall-agent.ps1` | Désinstalle **une instance précise** sans toucher aux autres. |
| `AgentInstall.psm1` | Fonctions partagées (dérivation des noms/chemins d'instance, contrôle PE x86/x64, transport sécurisé de la clé de pré-configuration). |

## Fabriquer un package

```powershell
# Les deux plateformes (x86 + x64)
powershell -ExecutionPolicy Bypass -File tools/package-agent.ps1

# x86 uniquement (cas EncheresV6 : driver ODBC Pervasive 32 bits)
powershell -ExecutionPolicy Bypass -File tools/package-agent.ps1 -Platform x86

# Package pré-configuré pour un tenant (clé API jamais en clair)
powershell -ExecutionPolicy Bypass -File tools/package-agent.ps1 -Platform x86 `
  -PreConfigPlatformUrl https://liakont.editeur-x.fr -PreConfigAdapter EncheresV6 `
  -PreConfigApiKey '<clé API>'   # un mot de passe à usage unique est généré et affiché
```

## Installer (poste cible, extraire le .zip puis)

```powershell
# Instance par défaut
powershell -ExecutionPolicy Bypass -File install-agent.ps1

# Instance nommée (serveur SaaS mutualisé : une instance par base cliente)
powershell -ExecutionPolicy Bypass -File install-agent.ps1 -InstanceName ClientA -Silent

# Valider le plan sans rien modifier (ne requiert pas les droits administrateur)
powershell -ExecutionPolicy Bypass -File install-agent.ps1 -InstanceName ClientA -DryRun
```

Documentation complète (prérequis, règle x86/x64, multi-instances, dépannage) :
[`docs/installation-agent.md`](../../docs/installation-agent.md).
Décision de format de package et transport de la clé de pré-configuration :
[`docs/adr/ADR-0019-format-paquet-installeur-agent.md`](../../docs/adr/ADR-0019-format-paquet-installeur-agent.md).
