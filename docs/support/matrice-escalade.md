# Matrice d'escalade du support (N1 / N2 / N3)

> **But.** Définir **qui traite quoi** dans le support du produit Liakont, par quel **canal**,
> dans quel **délai** (SLA), et **quelles informations** fournir à chaque escalade. C'est l'un
> des prérequis de viabilité du modèle « grappe » de l'Offre Éditeur (les autres : tableau de
> bord de statuts, FAQ + formation, monitoring proactif).
>
> **Source.** [Offre-Editeur-Passerelle.md](../market/Offre-Editeur-Passerelle.md) §5 (qui fait
> quoi, volumes, canal ticket), §10 (SLA contractuel) et
> [F12-Architecture-Plateforme-Agent.md](../conception/F12-Architecture-Plateforme-Agent.md) §5
> (catalogue des alertes et gravités). **Aucun engagement n'est inventé** : tout SLA chiffré de
> ce document est soit repris de l'offre, soit explicitement **« à contractualiser »** (voir §4
> et §7).

---

## 1. Les trois niveaux

Repris **tels quels** de l'Offre Éditeur §5 :

| Niveau | Qui | Contenu | Volume attendu |
|---|---|---|---|
| **N1 — Usage** | **Éditeur** (sa hotline habituelle, en marque blanche : le client final ne sait pas que nous existons) | « Comment je fais ? », statuts, blocages courants résolus depuis la console, formation des utilisateurs, redémarrage de l'agent | **~80 %** des tickets |
| **N2 — Technique** | **Nous** (IT Innovations) | Rejets PA récurrents, statuts non remontés, erreurs de mapping, données sources incohérentes, agent qui ne redémarre pas | **~15-20 %** |
| **N3 — PA / réglementaire** | **Nous + escalade Plateforme Agréée** | Pannes d'API PA, évolutions de format, questions fiscales, incidents de plateforme | **~1-5 %** |

**Canal** : le support se traite **par ticket, pas par téléphone** (Offre §5, prérequis 3). Un
ticket laisse une trace, permet de joindre les informations du §5 ci-dessous et alimente la base
de connaissances ([faq-editeur.md](faq-editeur.md)).

> **Principe directeur (Offre §4) :** la robustesse du produit *est* le sujet économique. À
> ~200 €/an net par site, **une seule journée de support consomme la marge de 2-3 sites**. La
> matrice n'existe pas pour « faire payer » le support mais pour que **80 % des cas se règlent
> en N1 sans nous solliciter** — d'où le monitoring proactif (les alertes SUP01) et la FAQ.

---

## 2. Qui traite quoi — par type d'incident

Avant les alertes (§3), voici le routage des situations rencontrées au quotidien depuis la
console. Le détail de chaque résolution est dans le [guide opérateur](../guide-operateur.md) §8
et la [FAQ](faq-editeur.md).

| Situation | 1er traitement | Escalade si non résolu | Référence |
|---|---|---|---|
| Document **Bloqué — Table TVA non validée / TVA non mappée** | **N1** : compléter et **valider** la table TVA, puis « Revérifier » | N2 si le régime fiscal lui-même est incertain (relais expert-comptable du client) | guide opérateur §8/§9 |
| Document **Bloqué — Paramétrage incomplet** (profil tenant absent) | **N1 → opérateur d'instance** : importer le profil légal du tenant | N2 si l'import échoue | guide éditeur §4 |
| Document **Bloqué — garde-fou B2B/B2C** | **N1** : trancher le verdict acheteur pro / particulier | — (geste opérateur) | guide opérateur §7 |
| Document **Rejeté par la PA** | **N1** : lire le motif, corriger la donnée source, renvoyer ou traiter manuellement | **N2** si le motif est un mapping ou un défaut produit ; **N3** si c'est la PA/format | guide opérateur §8 |
| Document **Erreur technique** (réseau/API) | **N1** : aucune action, re-tenté automatiquement | **N2** si cela persiste | guide opérateur §8 |
| **Agent muet** (poste éteint, service arrêté) | **N1** : vérifier le poste allumé + service « Liakont Agent » démarré, redémarrer | **N2** si l'agent ne redémarre pas / clé API invalide | guide éditeur §8 |
| **Connexion / droits** (page masquée, action absente) | **N1** : vérifier le rôle/permission de l'utilisateur | N2 si la projection rôle→permission est en cause | guide opérateur §17 |

---

## 3. La matrice des alertes SUP01

Le **monitoring proactif** (module Supervision) lève des alertes **avant** l'appel du client.
Chaque alerte du catalogue SUP01 a une **gravité** (🔴 Critique / 🟠 Avertissement) ; cette
matrice lui associe un **niveau de premier traitement**, une **escalade** et un **SLA par
gravité** (§4). Le catalogue et les gravités sont repris **à l'identique** de
[F12 §5.2](../conception/F12-Architecture-Plateforme-Agent.md) (implémenté dans
`src/Modules/Supervision/Application/AlertRuleCatalog.cs`).

| Règle (id) | Gravité | Seuil par défaut | 1er traitement | Escalade | SLA |
|---|---|---|---|---|---|
| **Agent muet** (`agent.mute`) | 🔴 Critique | > 24 h sans heartbeat | **N1** : poste allumé ? service agent démarré ? redémarrer | **N2** si l'agent ne repart pas (clé API, réseau, install) | SLA Critique (§4) |
| **Run d'extraction manqué** (`agent.missed_run`) | 🔴 Critique | > 36 h | **N1** : vérifier la planification de l'agent | **N2** si la planification est correcte mais aucun run | SLA Critique (§4) |
| **File de push qui grossit** (`push.queue_backlog`) | 🟠 Avertissement | > 50 éléments ou > 6 h | **N2** : erreurs de push répétées (réseau, API PA, payload) | **N3** si l'API PA est en cause | SLA Avertissement (§4) |
| **Documents bloqués non traités** (`documents.blocked`) | 🟠 Avertissement | > 5 jours | **N1** : lever le blocage (table TVA, garde-fou, donnée source) | N2 si la cause est un défaut produit | SLA Avertissement (§4) |
| **Rejets PA non traités** (`documents.pa_rejected`) | 🔴 Critique | > 2 jours | **N1** : corriger et renvoyer / traiter manuellement | **N2** (mapping/produit) puis **N3** (PA/format) | SLA Critique (§4) |
| **Échéance déclarative proche** (`period.deadline_near`) | 🔴 Critique | J-3, documents non transmis | **N1** : débloquer et transmettre les documents en retard | **N2** en cas de blocage technique sous échéance | SLA Critique (§4) |
| **Version d'agent obsolète** (`agent.version_obsolete`) | 🟠 Avertissement | < N-1 | **N2** : déclencher / vérifier l'auto-update de l'agent | — | SLA Avertissement (§4) |

> **État d'implémentation (à connaître pour ne pas s'y reposer à tort).** Au moment de la
> rédaction, **3 règles sont actives** dans le produit (`agent.mute`, `documents.blocked`,
> `documents.pa_rejected`) ; les **4 autres** (`agent.missed_run`, `push.queue_backlog`,
> `period.deadline_near`, `agent.version_obsolete`) sont **déclarées au catalogue F12 §5.2 mais
> pas encore évaluées** (voir [guide éditeur](../guide-editeur.md) §8). La matrice couvre le
> catalogue complet ; le support N1 ne doit pas encore présumer une alerte automatique pour les
> 4 règles inactives.

> **Acquitter ≠ résoudre.** Une alerte reste active tant que sa cause n'a pas disparu ; au plus
> une alerte active par (tenant, règle). L'acquittement note la **prise en charge** (départ du
> chrono SLA), pas la résolution.

---

## 4. SLA par gravité

L'Offre Éditeur **mentionne** un « SLA contractuel » (§10) et la nécessité d'une « matrice
d'escalade contractuelle : périmètre N1/N2, SLA » (§5) **mais ne chiffre aucun délai**. En
conséquence, et conformément à la règle « aucun engagement inventé », **les valeurs ci-dessous
sont une proposition de base, NON CONTRACTUELLE, à contractualiser** (action **OE-4** de l'offre :
validation des clauses par un avocat avant signature éditeur).

| Gravité | Délai de **prise en charge** cible | Délai de **contournement / point d'étape** cible | Statut |
|---|---|---|---|
| 🔴 **Critique** | jour ouvré suivant la détection (proposition) | sous 2 jours ouvrés (proposition) | **à contractualiser** |
| 🟠 **Avertissement** | sous 3 jours ouvrés (proposition) | best-effort (proposition) | **à contractualiser** |

> **Aucune des durées ci-dessus n'est contractuelle tant qu'elle n'est pas reprise dans le
> contrat type éditeur (OE-4).** Elles donnent un **ordre de grandeur cohérent avec les
> gravités** (une alerte 🔴 est traitée plus vite qu'une 🟠 — « une alerte critique a un SLA,
> pas seulement une couleur ») ; les chiffres définitifs sont arrêtés à la signature.

Ce que l'offre **fixe déjà** (donc non « à contractualiser ») et qui encadre tout SLA :

- **Périmètre N1/N2/N3** et **volumes** : Offre §5 (repris au §1).
- **Canal = ticket**, pas téléphone : Offre §5, prérequis 3.
- **Droit de re-facturation** du support hors périmètre (N1 défaillant chez l'éditeur) :
  Offre §10. Le SLA s'applique au **périmètre N2/N3** que nous portons ; un N1 défaillant côté
  éditeur n'est pas couvert par notre SLA et peut être re-facturé.

---

## 5. Informations à fournir à chaque escalade

Pour éviter les allers-retours (coûteux à ~200 €/an net par site, Offre §4), **un ticket
escaladé vers N2/N3 contient au minimum** :

| Information | Où la trouver | Pourquoi |
|---|---|---|
| **Tenant** (raison sociale / identifiant) | Console, en-tête ; supervision | Cibler le bon client sans fuite cross-tenant |
| **Version de l'agent** et **dernier heartbeat** | `/supervision/{tenantId}` (liste des agents) | Distinguer un bug corrigé d'un poste obsolète |
| **Version de l'instance** | Pied de console / note de déploiement | Reproduire sur la bonne version |
| **Identifiant du / des document(s)** concerné(s) et leur **état** | Liste/détail des documents | Tracer l'incident dans la piste d'audit |
| **Règle d'alerte** déclenchée (id `xxx.yyy`) et son **horodatage** | Détail de supervision | Relier au catalogue SUP01 (§3) |
| **Motif de blocage / rejet** (onglet « Contrôles ») | Détail du document | Le motif est en français, actionnable |
| **Logs de l'agent** (si incident agent) | Poste client (dossier de logs de l'agent) | Diagnostic technique N2 |

> **Données sensibles.** Ne jamais joindre de **secret** (clé API d'un PA, clé d'agent, chaîne
> ODBC) à un ticket. Un incident d'authentification se décrit par son **symptôme**, pas par le
> secret en clair (CLAUDE.md règle n°10). Les logs joints doivent être expurgés des secrets.

---

## 6. Schéma d'escalade

```
Client final
   │  (appelle sa hotline habituelle — marque blanche)
   ▼
N1 — Éditeur (usage)  ──résolu (≈80 %)──► fin
   │  non résolu / hors périmètre usage
   ▼
N2 — IT Innovations (technique)  ──résolu (≈15-20 %)──► fin
   │  cause = plateforme agréée / format / fiscal
   ▼
N3 — IT Innovations + escalade PA (≈1-5 %)
```

---

## 7. Traçabilité (aucun engagement inventé)

| Élément de ce document | Source |
|---|---|
| Niveaux N1/N2/N3, qui fait quoi, volumes | Offre §5 (table « Le support : qui fait quoi ») |
| Canal = ticket (pas téléphone) | Offre §5, prérequis 3 |
| Existence d'un SLA contractuel + re-facturation hors périmètre | Offre §10 |
| Prérequis du modèle (tableau de bord, FAQ, matrice, monitoring) | Offre §5 |
| Catalogue des 7 alertes, ids, seuils par défaut, gravités | F12 §5.2 + `AlertRuleCatalog.cs` |
| État d'implémentation (3 actives / 4 déclarées) | guide éditeur §8 |
| **Chiffres de SLA (§4)** | **AUCUNE source — proposition explicitement « à contractualiser » (OE-4)** |

---

## Voir aussi

- [FAQ éditeur](faq-editeur.md) — réponses de niveau 1 aux questions récurrentes
- [Formation du support N1](formation-n1-editeur.md) — ce que le N1 doit savoir résoudre seul
- [Guide opérateur](../guide-operateur.md) — la console web, écran par écran
- [Guide éditeur / opérateur d'instance](../guide-editeur.md) — supervision, alertes, flotte
- [Veille réglementaire](veille-reglementaire.md) — le processus N3 réglementaire
- [Offre Éditeur](../market/Offre-Editeur-Passerelle.md) — le modèle « grappe » (source)
