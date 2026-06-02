# F11 — Ordonnancement & mode automatique (CLI + Service)
### Document de conception — Gateway.Cli & Gateway.Service

> Statut : 🟨 rédigé sans deep research (conception interne, pattern SynchroAxelor éprouvé). À revoir ensemble.
> Dernière mise à jour : 2026-06-02 — ajout du double hébergement (tâche planifiée **et** service Windows).
>
> **⚠️ AMENDEMENT D'ARCHITECTURE (2026-06-02 — décision blueprint.md §3, postérieure à la rédaction)** :
> cette spec décrit deux hôtes nominaux interchangeables (CLI en tâche planifiée OU service Windows)
> partageant le même Tracking. **C'est obsolète.** Le **Service Windows est l'UNIQUE hôte nominal**
> (ordonnanceur + pipeline + API HTTP + SEUL écrivain du Tracking). Le CLI est réduit à un utilitaire
> de mise en service et de secours, utilisable uniquement quand le Service est ARRÊTÉ (protégé par mutex).
> Le contrat du PipelineRunner, les codes de sortie 0/1/2/3 et la planification décrits ici restent
> valables — c'est l'hébergement qui change. Le backlog (orchestration/items/SVC.yaml, CLI.yaml) fait foi.
> Conséquence sur les sous-commandes CLI du §2 : `extract`, `send`, `sync-status` et `report` ne sont
> PAS reprises — elles sont remplacées par les endpoints de l'API du Service (lot API) et le run
> de secours complet. Le CLI ne garde que : check-config, encrypt-secret, run, backup, verify-archive,
> audit-export.

---

## 1. Objectif

Exécuter le pipeline complet sans intervention humaine : extraction → contrôles → envoi → synchronisation des statuts. C'est le mode de fonctionnement nominal en production ; la console WPF (F10) sert à la supervision et aux cas particuliers.

Pattern de référence : `SynchroAxelor` / `SynchroAppliMobile` (EXE + App.config + logs fichiers), déjà en production chez le client fondateur.

## 1 bis. Principe d'architecture : moteur découplé de l'hébergement

Le pipeline ne sait **rien** de qui le déclenche. La logique (`extract → check → send → sync`) vit dans **`Gateway.Core`** sous la forme d'un orchestrateur réutilisable :

```csharp
// Gateway.Core — host-agnostique
public sealed class PipelineRunner
{
    public Task<RunResult> RunAsync(GatewayConfig config, RunOptions options, CancellationToken ct);
}
// RunResult { ExitCode, Counts, BlockedDocs[], Errors[] }
```

Par-dessus, **deux hôtes interchangeables** appellent ce même `PipelineRunner`, partagent le **même mutex** (§4) et le **même Tracking SQLite** (F6) :

| Hôte | Quand le choisir | Déclenchement |
|---|---|---|
| **`Gateway.Cli.exe`** | Site simple, exploitant à l'aise avec le Planificateur, ou recette/exécution à la main | **externe** : Planificateur de tâches Windows lance l'EXE → un `run` → code retour → sortie |
| **`Gateway.Service`** | Site sans accès au Planificateur, besoin d'auto-surveillance (« montre morte »), ou exploitation qui préfère les services | **interne** : service Windows résident, ordonnanceur propre (timer lisant le planning de la config) → appelle `PipelineRunner` aux heures prévues |

On **livre les deux** ; on **active l'un OU l'autre par déploiement** (jamais les deux en parallèle sur la même config — le mutex l'interdirait de toute façon). Le service n'est qu'une coquille `ServiceBase` (intégrée à .NET Framework 4.8, aucune dépendance) + un timer ; toute la valeur reste dans `Gateway.Core`, donc le coût du second hôte est marginal.

> **Pourquoi prévoir les deux (décision utilisateur, 2026-06-02)** : selon le site legacy, l'un est plus naturel que l'autre. La tâche planifiée est triviale à expliquer/déboguer ; le service résident résout nativement la « montre morte » (§5) et convient aux exploitations qui supervisent des services Windows. Découpler le moteur rend ce choix gratuit et réversible.

## 2. Commandes et arguments

```
Gateway.Cli.exe <commande> --config <chemin> [options]

Commandes :
  run           Pipeline complet : extract + check + send + sync-status (le mode planifié)
  extract       Extraction seule : lit la source, alimente le Tracking, n'envoie rien
  send          Envoi des documents en état "À envoyer" (après contrôles)
  sync-status   Synchronise les statuts/tax reports depuis la PA
  check-config  Valide la configuration (connexion source, clé API, table TVA) sans rien traiter
  report        Affiche un récapitulatif (compteurs par état, période)

Options communes :
  --config <chemin>     Fichier de configuration (obligatoire)
  --from <yyyy-MM-dd>   Début de période (défaut : selon config / dernière exécution)
  --to <yyyy-MM-dd>     Fin de période (défaut : aujourd'hui)
  --dry-run             Tout faire SAUF les appels d'écriture vers la PA (extraction + contrôles + simulation)
  --verbose             Log détaillé console + fichier
  --max <n>             Limite le nombre de documents traités (tests / montée en charge progressive)
```

### Exemples
```
:: Tâche planifiée quotidienne (production)
Gateway.Cli.exe run --config C:\Gateway\config\cmp.json

:: Recette : traiter seulement 10 bordereaux d'une journée donnée, sans envoyer
Gateway.Cli.exe run --config cmp.json --from 2026-06-01 --to 2026-06-01 --max 10 --dry-run

:: Vérifier qu'une nouvelle config est valide avant la mise en service
Gateway.Cli.exe check-config --config nouvelle-etude.json
```

## 3. Codes de retour

| Code | Signification | Effet sur la tâche planifiée |
|---|---|---|
| 0 | Succès complet — tous les documents traités sont émis ou légitimement ignorés | RAS |
| 1 | Succès partiel — au moins un document bloqué/rejeté, le reste est passé | Notification (cf. §5), mais pas d'alerte critique |
| 2 | Échec du run — erreur fatale (config invalide, source inaccessible, clé API refusée) | Alerte : rien n'a été traité |
| 3 | Run non exécuté — verrou détenu par un autre processus (console ou autre run en cours) | Réessayer au prochain créneau |

> Le Planificateur Windows peut déclencher une action sur code ≠ 0 ; en pratique c'est la notification (§5) qui prévient l'opérateur.

## 4. Verrouillage (cohabitation CLI ↔ Console)

- **Mutex nommé système** : `Global\Gateway_<hash-du-chemin-config>` — un seul processus en écriture par configuration.
- La console WPF prend le même mutex pour les actions d'envoi ; en lecture seule (consultation) elle ne le prend pas.
- Si le mutex est occupé : code retour 3, log explicite, aucune attente bloquante (le run suivant rattrapera).
- SQLite en mode WAL pour que la console puisse lire pendant qu'un run écrit.

## 5. Notifications d'erreur

Mécanisme V1 : **e-mail SMTP simple** (serveur SMTP du client, paramétré dans la config).

| Événement | Notification |
|---|---|
| Code retour 2 (échec du run) | Mail immédiat : "La transmission du <date> n'a pas pu s'exécuter : <raison>" |
| Code retour 1 avec nouveaux blocages/rejets | Mail récapitulatif : liste des documents concernés + motifs + lien "ouvrir la console" |
| Aucun run depuis > 48 h (montre morte) | ⚠️ En mode **tâche planifiée** : non détectable par la passerelle elle-même → fichier `last-run-status.json` + consigne, ou tâche « sentinelle » (`report --check-last-run`). En mode **service** : détecté nativement (le service est résident → battement de cœur + auto-alerte si le run prévu n'a pas eu lieu). C'est l'avantage majeur du service. |
| Run OK | Pas de mail (éviter le bruit) — option `NotifyOnSuccess: true` pour les premières semaines |

## 6. Journalisation

- Un fichier de log par run : `logs\2026-06-02_0300_run.log` (horodaté).
- Rotation : conservation 90 jours par défaut (paramétrable) — au-delà, suppression automatique des logs ; le **Tracking SQLite, lui, n'est jamais purgé automatiquement** (piste d'audit, cf. F6/DR6).
- Niveaux : INFO (par document : détecté/envoyé/état), WARN (alertes de contrôle), ERROR (rejets, erreurs techniques).
- Format : texte lisible, horodaté, préfixé par le n° de document → grep-able et lisible par un humain.

## 7. Planification recommandée

| Contexte | Fréquence | Justification |
|---|---|---|
| SVV (ventes aux enchères) | **1×/jour, la nuit (ex. 03:00)** | Les bordereaux sont créés en journée après les ventes ; B2Brouter génère ses ledgers DGFiP à ~02:00 ; un passage à 03:00 envoie les documents du jour J et synchronise les tax reports de J-1 |
| Volume élevé / multi-ventes par jour | 2×/jour (12:00 + 03:00) | Limiter le décalage |
| Recette / démarrage | Manuel uniquement (console ou CLI à la main) | Contrôle humain avant chaque envoi |

> Rappel réglementaire (cf. DR5) : les délais de transmission e-reporting dépendent du régime de TVA du client (décadaire pour le réel normal mensuel). Un passage quotidien couvre tous les régimes avec marge.

### 7 bis. Ordonnanceur interne (mode service)

Quand c'est le **service** qui héberge le pipeline, le planning ne vit plus dans le Planificateur Windows mais dans la config :

```json
"schedule": {
  "times": ["03:00"],        // une ou plusieurs heures dans la journée
  "catchUpOnStart": true,    // au démarrage du service, rattraper si l'heure prévue est passée et non exécutée
  "heartbeatMinutes": 15     // écriture périodique de last-run-status.json (battement de cœur)
}
```

- Le service réveille `PipelineRunner.RunAsync(...)` aux heures prévues, **exactement le même appel** que `Gateway.Cli.exe run`.
- `catchUpOnStart` couvre le cas « la machine était éteinte à 03:00 » (équivalent de l'option « exécuter dès que possible si manqué » du Planificateur).
- Le **battement de cœur** alimente `last-run-status.json` même hors run → une supervision externe (ou le service lui-même) détecte un service figé ou un run manqué. C'est ce qui ferme le trou de la « montre morte ».
- Le service respecte le **même mutex** (§4) : si la console WPF tient un envoi en cours, le run du service rend code 3 et réessaiera au créneau suivant.

## 8. Séquence du `run` (pseudo-code)

```
1. Charger + valider la config                      → échec : code 2
2. Acquérir le mutex                                → occupé : code 3
3. Ouvrir le Tracking (SQLite), ouvrir la source (ODBC)
4. EXTRACT : lire les documents de la période non encore connus → Tracking état "Detected"
5. CHECK   : pour chaque document Detected/Blocked → contrôles F4
             → OK : état "ReadyToSend" ; KO : état "Blocked" (motifs)
6. SEND    : pour chaque ReadyToSend → transformer (pivot → payload) → POST PA
             → succès : "Issued" ; erreurs[] : "RejectedByPa" ; réseau : "TechnicalError" (retenté au prochain run)
7. SYNC    : pour chaque document Issued sans tax report archivé → GET tax_reports → archiver xml_base64
8. Bilan   : compteurs, notifications, code retour
9. Libérer le mutex
```

## 9. Décisions à valider ensemble

| # | Décision | Options | Recommandation |
|---|---|---|---|
| 1 | Notification : SMTP suffit-il ? | SMTP / fichier de statut lisible par un outil de supervision (Centreon, etc.) / les deux | SMTP en V1 + fichier `last-run-status.json` (gratuit à produire, exploitable par n'importe quelle supervision) |
| 2 | La "montre morte" (run qui ne s'exécute plus) | tâche sentinelle / consigne d'exploitation / rien | fichier `last-run-status.json` + consigne ; sentinelle si le client a une supervision |
| 3 | `--dry-run` doit-il créer les documents en `send_after_import: false` chez B2Brouter (validation réelle du payload) ou ne rien envoyer du tout ? | rien envoyer / créer sans envoyer | **rien envoyer** par défaut ; un mode `--validate-remote` séparé pour la création sans envoi (utile en recette, mais crée des objets côté PA) |
| 4 | Service Windows **ou** tâche planifiée ? | tâche planifiée / service / **les deux** | ✅ **livrer les deux** (décision 2026-06-02) : `Gateway.Core.PipelineRunner` commun, deux hôtes minces (`Gateway.Cli` + `Gateway.Service`), activés au choix par déploiement. Tâche planifiée = défaut simple ; service = sites sans Planificateur ou exigeant l'auto-surveillance. Coût marginal car le moteur est partagé. |
| 4b | Le planning du service est-il par config (heures) ou par installation paramétrée ? | config JSON / paramètre service | config JSON (`schedule`) — cohérent avec le reste, modifiable sans réinstaller le service |
| 4c | Installation du service | `sc create` manuel / installeur (F12) / `InstallUtil` | à cadrer en **F12** (déploiement) — prévoir `Gateway.Service install/uninstall` en self-install pour éviter `InstallUtil` |
