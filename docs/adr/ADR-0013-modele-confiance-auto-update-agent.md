# ADR-0013 — Modèle de confiance de l'auto-update de l'agent

**Date :** 2026-06-05

**Statut :** Accepté (2026-06-05)

**Amende :** F12 §2.5 (heartbeat & auto-update), §3.3 (réponse 426), §7 décision n°4 (D6 du 2026-06-03)

**Items :** AGT04 (côté agent), OPS07 (registre de versions + publication, côté plateforme)

---

## Contexte

Une **flotte d'agents** installés chez les clients finaux doit se mettre à jour **sans
intervention**. Le déclencheur vient de la plateforme : un `updateRequired=true` dans la réponse de
heartbeat (F12 §2.5) ou un `426 Upgrade Required` sur un push (F12 §3.3). L'agent télécharge un
paquet et se remplace.

Le risque structurant : l'agent exécute du **code arbitraire** récupéré sur le réseau, sur le poste
du client final, avec les droits nécessaires pour remplacer ses binaires et piloter son service
Windows. **Si la plateforme est compromise**, elle ne doit PAS pouvoir pousser n'importe quel binaire
à toute la flotte. Un simple hash publié par la **même API** que `updateUrl` ne protège pas : l'attaquant
qui contrôle l'API contrôle aussi le hash.

## Décision

### 1. Authenticité par **manifeste de version signé** (clé dédiée hors plateforme)

La confiance ne repose **pas** sur la plateforme mais sur une **clé applicative de release dédiée**,
dont la **clé privée est détenue hors plateforme** (poste de release IT Innovations) et la **clé
publique est provisionnée dans l'agent à l'installation** (fichier
`C:\ProgramData\Liakont\update-signing.pubkey.xml`, posé par l'installeur OPS05/F13 — jamais embarquée
en dur dans le code, CLAUDE.md n°7).

Le flux de vérification (porté par `AutoUpdateCoordinator`) :

1. La config heartbeat fournit `updateUrl` (URL du **manifeste**) et `versionManifestSignature`
   (signature RSA des octets bruts du manifeste, produite par la clé de release).
2. L'agent télécharge le **manifeste** (HTTPS) : un JSON `{ version, packageUrl, packageSha256 }`.
3. L'agent **vérifie la signature** des octets bruts du manifeste contre la clé publique provisionnée
   (RSA, SHA-256, PKCS#1 v1.5). **Pas de clé provisionnée → refus** (fail-closed). Signature invalide
   → refus + signalement opérateur.
4. L'agent télécharge le **paquet** depuis `packageUrl` (référencé par le manifeste **signé**) et
   compare son **SHA-256** à `packageSha256` du manifeste. Non concordant → refus + signalement.
5. **Garde anti-downgrade** : la mise à jour n'est appliquée que si `manifest.version` est
   **strictement supérieure** à la version courante (un attaquant ne peut au mieux que rejouer un
   manifeste ancien validement signé — bloqué ici).

Propriété de sécurité : une plateforme compromise contrôle `updateUrl`, `versionManifestSignature`,
et le contenu servi — mais **ne peut pas forger** une signature valide sans la clé privée. Elle ne
peut que rejouer un couple (manifeste, signature) déjà signé → pointe un paquet légitime (hash
vérifié) → aucune exécution de code arbitraire.

**Authenticode** (signature du binaire de l'updater/des binaires) s'ajoute **en complément** dès qu'un
certificat de signature de code est disponible — il ne remplace pas le manifeste signé (qui couvre la
provenance applicative), il renforce la chaîne au niveau OS.

### 2. Updater = exe séparé, jamais lancé depuis le dossier qu'il remplace

Un binaire **chargé** ne peut pas être remplacé sous Windows (verrou de fichier). L'updater
(`Liakont.Agent.Updater.exe`) est donc :

- un **exe autonome** (aucune dépendance à `Liakont.Agent.Core` : il ne porte ni SQLite ni le contrat,
  reste « petit » et stable — F12 §2.5) ;
- **copié dans un dossier de travail séparé** (hors du dossier d'installation) avant lancement, puis
  démarré en **processus détaché** (il survit à l'arrêt du service qu'il pilote).

Le contrat agent ↔ updater est volontairement **par ligne de commande** (couplage faible) + un fichier
de **statut JSON** que l'updater écrit en fin de course et que l'agent relit pour le signalement.

### 3. Séquence updater + rollback

`UpdaterEngine` (testable, abstractions `IServiceControl` / `IBinarySwapper` / `IServiceHealthProbe`) :

1. **Arrêt** du service (l'arrêt propre attend la fin d'un run en cours — AGT01).
2. **Sauvegarde** des binaires courants (les anciens sont **conservés** jusqu'au redémarrage sain).
3. **Remplacement** par les binaires de staging (vérifiés, étape 1).
4. **Démarrage** du service.
5. **Attente du redémarrage sain** (SCM `Running` **et** marqueur de heartbeat local frais) avec
   **timeout**. Sain → succès (purge de la sauvegarde) ; non sain / timeout / exception →
   **rollback** : restauration des anciens binaires, redémarrage, **statut « rollback » signalé** au
   heartbeat suivant.

### 4. Garde-fous

- **Jamais de mise à jour pendant un run d'extraction** : le coordinateur sonde le verrou de run
  (mutex `Global\LiakontAgentRun`, AGT01) via `IRunActivityProbe` et **diffère** si un run est en
  cours. Le déclencheur 426 (qui survient *pendant* un run) pose un fanion ; la mise à jour part au
  prochain cycle hors run.
- **Idempotence** : un seul updater à la fois (garde de ré-entrance sur le coordinateur).
- **Version visible** : version de l'agent dans le heartbeat (AGT03), les logs et le CLI `version`
  (AGT05) ; statut de la dernière mise à jour surfacé au heartbeat (`LastError`) en cas d'échec.

## Points durs Windows (tranchés)

| Point | Décision V1 | Note |
|---|---|---|
| Compte d'exécution de l'updater | Le service tourne en **LocalSystem** (AGT01) ; l'updater est lancé par le service, donc hérite des droits d'arrêt/démarrage du service et d'écriture sous le dossier d'installation. | Si l'installation cible `Program Files`, LocalSystem y écrit. Un compte de service dédié non-LocalSystem exigerait `SeServiceLogonRight` + ACL d'écriture sur le dossier d'installation — couvert par l'installeur OPS05 si ce choix est fait. |
| Verrou de fichiers | L'updater **ne tourne jamais** depuis le dossier d'installation : il est copié dans un dossier de travail séparé. | Empêche « binaire chargé, remplacement impossible ». |
| Emplacement des binaires | Dossier d'installation = paramètre (`--install`) fourni au lancement ; sauvegarde sous un sous-dossier de travail. | Pas de chemin en dur. |
| Bitness | L'updater est livré dans la même bitness (x86/x64) que l'agent ; un updater x86 sous WOW64 verrait `Program Files (x86)` par redirection. | L'installeur livre la bitness cohérente avec l'agent installé. |

## Alternatives écartées

- **Hash seul publié par l'API.** Ne protège pas si la plateforme est compromise (l'attaquant publie
  son propre hash). Écarté (motif central de D6).
- **Authenticode seul, sans manifeste signé.** Dépend de la disponibilité immédiate d'un certificat de
  signature de code et ne couvre pas la provenance applicative (quelle **version** est légitime pour
  CETTE flotte). Retenu en **complément**, pas en remplacement.
- **Updater portant `Liakont.Agent.Core`.** Plus simple à coder (réutilise log/horloge), mais l'updater
  cesse d'être « petit » (SQLite natif, contrat) et se couple à la stabilité de Core. Écarté : l'updater
  reste autonome, le contrat avec l'agent est la ligne de commande + un fichier statut.
