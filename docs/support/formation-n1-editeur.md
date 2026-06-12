# Formation du support N1 — éditeur

> **But.** Rendre le support **N1 (niveau 1, usage)** de l'éditeur **autonome** sur les cas
> courants : lire un statut, lever un blocage depuis la console, lire les alertes de supervision,
> redémarrer l'agent, et **savoir quand escalader**. C'est l'un des prérequis de viabilité du
> modèle « grappe » de l'Offre Éditeur (les autres : tableau de bord de statuts, FAQ + base de
> connaissances, monitoring proactif, matrice d'escalade).
>
> **Public.** Le support de l'éditeur, qui assure le N1 **en marque blanche** (le client final
> appelle sa hotline habituelle et ne sait pas qu'IT Innovations existe).
>
> **Durée.** **~½ journée**, conformément à l'Offre Éditeur §5 (« FAQ / base de connaissances +
> formation du support éditeur (~½ journée) »). Le déroulé type est au §9.
>
> **Format.** Théorie courte puis **exercices sur l'instance de démonstration** (jeu de
> démonstration des guides — voir [guide opérateur](../guide-operateur.md), Annexe « Captures
> d'écran sur jeu de démonstration », et le mode démo du `README.md`).
>
> **Source.** Comportement produit : [guide opérateur](../guide-operateur.md) et
> [guide éditeur](../guide-editeur.md). Catalogue des alertes :
> [F12 §5.2](../conception/F12-Architecture-Plateforme-Agent.md). Périmètre du support et durée
> de formation : [Offre Éditeur §5](../market/Offre-Editeur-Passerelle.md). **Aucune règle fiscale
> ni aucun engagement n'est inventé ici** : la formation renvoie à la console, aux guides, à la
> [FAQ](faq-editeur.md) et à la [matrice d'escalade](matrice-escalade.md).
>
> **Légende :** ✅ le N1 résout seul · ↗️ le N1 **escalade** (voir [matrice](matrice-escalade.md)).

---

## 1. Objectif et périmètre du N1

Le N1 absorbe **~80 % des tickets** (Offre §5, repris [matrice §1](matrice-escalade.md)). Sa
mission n'est **pas** de tout traiter, mais de **régler vite les cas d'usage** et d'**escalader
proprement** le reste — un aller-retour mal cadré coûte cher au modèle (~200 €/an net par site,
Offre §4 ; voir [matrice §1, principe directeur](matrice-escalade.md)).

| Le N1 résout seul (✅) | Le N1 escalade (↗️) |
|---|---|
| Lire un statut, expliquer le cycle de vie d'un document | Rejets PA **récurrents** / même motif sur plusieurs documents → **N2** |
| Lever un blocage **de paramétrage console** (table TVA, garde-fou B2B/B2C, profil tenant) | Agent qui **ne redémarre pas**, clé API invalide, install KO → **N2** |
| Expliquer un rejet PA isolé, faire corriger la donnée source et renvoyer | **Erreur technique qui persiste**, file de push qui grossit → **N2** |
| Diagnostiquer un **agent muet** simple (poste éteint, service arrêté) et le redémarrer | **Question fiscale** (quelle catégorie TVA ?) → **expert-comptable du client** (jamais inventer) |
| Lire une alerte de supervision et l'acquitter (prise en charge) | **Évolution réglementaire / de format** → **N3** (voir [veille](veille-reglementaire.md)) |

> **Canal = ticket, pas téléphone** (Offre §5, prérequis 3). Un ticket laisse une trace, permet de
> joindre les informations minimales ([matrice §5](matrice-escalade.md)) et alimente la base de
> connaissances ([FAQ](faq-editeur.md)).

---

## 2. Le produit en 5 minutes (le bon modèle mental)

À transmettre **avant** les modules pratiques :

1. **Trois acteurs.** La **console web** (plateforme, ce que voit l'opérateur), l'**agent** (un
   service sur le poste du client final, qui extrait et pousse les documents) et la **Plateforme
   Agréée (PA)** (qui transmet à l'administration). Le N1 agit surtout dans la console ; l'agent se
   diagnostique à distance (heartbeat) et se redémarre sur le poste client (§6).
2. **Règle d'or — Liakont bloque plutôt que d'envoyer un document faux.** Un blocage **protège la
   responsabilité fiscale** du client ; il se **résout**, il ne se **contourne jamais**. Aucune
   validation bloquante n'est désactivée pour « faire passer » un envoi ([guide opérateur §8](../guide-operateur.md)).
3. **Périmètre V1.** Le produit **émet** (e-reporting B2C, garde-fou B2B). Il **ne gère pas la
   réception** des factures fournisseurs en V1 : elle passe par le **portail de la PA** du client
   ([guide opérateur §1](../guide-operateur.md), [guide éditeur §2](../guide-editeur.md), Offre §6).
   Ne jamais laisser croire que le produit couvre la réception.
4. **Aucune règle fiscale inventée.** Le support ne décide **jamais** d'une catégorie TVA, d'un
   seuil ou d'un code : ces choix appartiennent au **client / à son expert-comptable**. Une question
   fiscale n'est pas un ticket support, c'est une question comptable (CLAUDE.md règle n°2).
5. **Marque blanche.** Le client ne connaît que son éditeur. Ne révélez jamais l'infrastructure
   sous-jacente ([FAQ §A](faq-editeur.md)).

---

## 3. Module 1 — Lire un statut et le cycle de vie d'un document

**À savoir.** Le tableau de bord (`/`, [guide opérateur §3](../guide-operateur.md)) et la liste des
documents (`/documents`, §5) montrent l'état de chaque document. Les états et leurs transitions sont
décrits au [guide opérateur §4](../guide-operateur.md). Le détail d'un document (§6) porte un onglet
**« Contrôles »** qui **dit toujours pourquoi** un document est bloqué ou rejeté — en français,
actionnable.

> **Exercice 1 (démo).** Sur l'instance de démo, ouvrir `/documents`, identifier un document
> 🛑 **Bloqué**, ouvrir son détail, lire l'onglet « Contrôles » et **reformuler le motif** au client
> en une phrase actionnable.

---

## 4. Module 2 — Lever les blocages courants depuis la console (✅ N1)

Ce sont les cas les plus fréquents — **tous résolubles en N1**. Détail de chaque résolution :
[guide opérateur §8](../guide-operateur.md) et [FAQ §B/§F](faq-editeur.md).

| Blocage | Geste N1 | Référence |
|---|---|---|
| **Table TVA NON VALIDÉE** (envois suspendus) | Faire **compléter puis valider** la table par le responsable (expert-comptable du client), puis « Revérifier » | [guide opérateur §9](../guide-operateur.md) |
| **TVA non mappée** (régime sans règle) | Créer la règle manquante (« Créer une règle pour {régime} »), **revalider** la table, « Revérifier » le document. Si le **régime fiscal** lui-même est incertain → expert-comptable | [guide opérateur §9](../guide-operateur.md), [FAQ §B](faq-editeur.md) |
| **Garde-fou B2B/B2C** | Trancher le verdict acheteur **professionnel / particulier** — geste opérateur, **pas** une escalade | [guide opérateur §7](../guide-operateur.md) |
| **PARAMÉTRAGE INCOMPLET** (profil tenant absent) | **Relais opérateur d'instance** : importer le profil légal du tenant. ↗️ N2 si l'import échoue | [guide éditeur §4](../guide-editeur.md) |

> **Réflexe clé.** Après toute correction, **« Revérifier »** (un document) ou **« Revérifier
> tout »** (la liste). Et rappeler que **toute modification de la table TVA re-suspend les envois**
> jusqu'à **revalidation** — c'est volontaire et tracé ([FAQ §F](faq-editeur.md)).

> **Exercice 2 (démo).** Provoquer puis lever un blocage « TVA non mappée » : créer la règle de
> mapping manquante, revalider la table, revérifier le document et le voir repasser au vert.

---

## 5. Module 3 — Rejets PA et erreurs techniques

| Situation | Geste N1 | Escalade |
|---|---|---|
| Document ❌ **Rejeté par la PA** | Lire le **motif de rejet** (onglet « Contrôles »), corriger la **donnée source**, **renvoyer** ou traiter manuellement | ↗️ **N2** si le **même motif revient** sur plusieurs documents (mapping / défaut produit) ; ↗️ **N3** si la PA/format est en cause |
| Document ⚠️ **Erreur technique** (réseau/API) | **Aucune action** : re-tenté **automatiquement** au prochain traitement | ↗️ **N2** si cela **persiste** |
| **File de push qui grossit** (ou alerte) | — | ↗️ **N2** (erreurs de push répétées : réseau, API PA, payload) |

Détail : [guide opérateur §8](../guide-operateur.md), [FAQ §C](faq-editeur.md).

> **Exercice 3 (table).** Pour trois tickets fictifs (un rejet PA isolé, le même rejet sur cinq
> documents, une erreur technique transitoire), décider **résoudre N1** ou **escalader N2**, et
> justifier.

---

## 6. Module 4 — L'agent (poste client) et son redémarrage (✅ N1)

L'agent est un **service Windows** sur le poste du client final. Quand le tableau de bord ne reçoit
plus rien d'un poste, la supervision lève l'alerte **« Agent muet »**.

**Diagnostic N1 (dans l'ordre) :**
1. Le **poste est-il allumé** et connecté au réseau ?
2. Le **service Windows « Liakont Agent »** est-il **démarré** ? Le **redémarrer**
   ([guide éditeur §8](../guide-editeur.md)).

Si l'agent **ne repart pas**, ou si la cause est une **clé API invalide / révoquée** ou un problème
d'installation → ↗️ **N2** ([FAQ §D](faq-editeur.md)). Le N1 **ne réinstalle pas** l'agent et **ne
manipule pas** la clé API (secret).

> **Seuil par défaut.** Un agent est déclaré « muet » après **> 24 h sans heartbeat** (paramétrable
> par tenant, [F12 §5.2](../conception/F12-Architecture-Plateforme-Agent.md), repris
> [matrice §3](matrice-escalade.md)).

> **Exercice 4 (démo / poste).** Sur le poste de démo, arrêter le service « Liakont Agent »,
> observer l'effet attendu côté supervision, puis le redémarrer et vérifier le retour du heartbeat.

---

## 7. Module 5 — Lire les alertes de supervision

Le **monitoring proactif** (module Supervision) lève des alertes **avant** l'appel du client. Le N1
doit savoir **lire** une alerte et l'**acquitter**, pas configurer le moteur.

- **Deux gravités** : 🔴 **Critique** / 🟠 **Avertissement** (catalogue complet et seuils :
  [F12 §5.2](../conception/F12-Architecture-Plateforme-Agent.md), routage et premier traitement :
  [matrice §3](matrice-escalade.md)).
- **Toutes les règles du catalogue ne sont pas encore évaluées.** L'**état d'implémentation à
  jour** (quelles règles sont effectivement évaluées) est centralisé dans
  [matrice §3](matrice-escalade.md) — **et l'état réel affiché par la console fait foi**, pas un
  compte mémorisé. Le N1 **ne présume pas** d'alerte automatique pour une règle non encore évaluée
  (il **surveille activement** les documents bloqués/rejetés, surtout à l'approche d'une échéance) ;
  inversement, il **traite toute alerte que la console affiche**, sans la négliger au motif qu'il
  « croyait » la règle inactive.
- **Acquitter ≠ résoudre.** L'acquittement note la **prise en charge** (et fait partir le chrono du
  SLA) ; l'alerte reste active tant que sa **cause** n'a pas disparu ([matrice §3](matrice-escalade.md)).

> **Exercice 5 (démo).** Sur le dashboard de supervision, repérer une alerte 🔴 et une 🟠, dire pour
> chacune le **premier traitement** ([matrice §3](matrice-escalade.md)) et **acquitter** l'une d'elles.

---

## 8. Module 6 — Quand et comment escalader

L'escalade N1 → N2 → N3 et **qui traite quoi** sont définis dans la
[matrice d'escalade](matrice-escalade.md) ; le récapitulatif « quand escalader » est en
[FAQ §G](faq-editeur.md). Le N1 doit **mémoriser** :

- **Vers N2** (IT Innovations) : agent qui ne redémarre pas, clé API invalide, rejets PA récurrents,
  erreur technique persistante, file de push qui grossit.
- **Vers N3** (PA / réglementaire) : panne d'API PA, évolution de format, et — via la
  [veille](veille-reglementaire.md) — toute évolution réglementaire.
- **Vers l'expert-comptable du client** (hors support) : toute **question fiscale**.

**Informations minimales d'un ticket escaladé** ([matrice §5](matrice-escalade.md)) : tenant,
version de l'agent + dernier heartbeat, version de l'instance, identifiant(s) de document(s) et
état, règle d'alerte + horodatage, motif de blocage/rejet, logs de l'agent si incident agent.

> **⚠️ Jamais de secret en clair** dans un ticket (clé API d'un PA, clé d'agent, chaîne ODBC) : on
> décrit le **symptôme**, pas le secret (CLAUDE.md règle n°10). Les logs joints sont **expurgés**.

> **Exercice 6 (rédaction).** Rédiger un ticket d'escalade N2 complet pour un « agent muet qui ne
> redémarre pas », en réunissant les informations minimales **sans** aucun secret.

---

## 9. Déroulé type (~½ journée)

| Créneau | Séquence | Support |
|---|---|---|
| 0:00–0:20 | §2 — Le produit en 5 minutes (modèle mental, règle d'or, périmètre V1) | Slides + [guides](../guide-operateur.md) |
| 0:20–0:45 | §1 — Périmètre N1, ce qu'on résout / ce qu'on escalade, canal ticket | [matrice §1](matrice-escalade.md) |
| 0:45–1:30 | §3–§4 — Statuts + lever les blocages console (**exercices 1 & 2**) | Instance de démo |
| 1:30–2:00 | §5 — Rejets PA et erreurs techniques (**exercice 3**) | [FAQ §C](faq-editeur.md) |
| 2:00–2:30 | §6 — L'agent et son redémarrage (**exercice 4**) | Poste de démo |
| 2:30–3:00 | §7 — Lire les alertes de supervision (**exercice 5**) | Dashboard supervision |
| 3:00–3:30 | §8 — Escalader proprement (**exercice 6**) + revue de la [FAQ](faq-editeur.md) | [matrice §5](matrice-escalade.md) |
| 3:30–4:00 | Évaluation (§10) + questions | Checklist |

---

## 10. Évaluation — checklist de compétences N1

À l'issue de la formation, le N1 sait :

- [ ] Expliquer le **périmètre V1** (émission oui, réception via portail PA) sans sur-promettre.
- [ ] Énoncer la **règle d'or** (bloquer plutôt qu'envoyer faux ; un blocage se résout, jamais ne se contourne).
- [ ] Lire un **statut** et trouver le **motif** d'un blocage (onglet « Contrôles »).
- [ ] Lever les **blocages console** : table TVA (valider / mapper), garde-fou B2B/B2C, profil tenant, et **« Revérifier »**.
- [ ] Traiter un **rejet PA isolé** (corriger la source, renvoyer) et **escalader** un rejet récurrent.
- [ ] Diagnostiquer un **agent muet** et **redémarrer le service** « Liakont Agent ».
- [ ] Lire une **alerte** (gravité, premier traitement), distinguer **acquitter** de **résoudre**, et savoir quelles règles sont **actives**.
- [ ] **Escalader** au bon niveau avec les **informations minimales** et **sans secret en clair**.
- [ ] Renvoyer toute **question fiscale** à l'**expert-comptable du client**.

---

## 11. Traçabilité (aucun engagement inventé)

| Élément de cette formation | Source |
|---|---|
| Périmètre N1, volumes (~80 %), canal ticket, durée ~½ journée | Offre §5 (et §4 pour l'économie) |
| Marque blanche, le client appelle l'éditeur | Offre §5/§6, [FAQ §A](faq-editeur.md) |
| Périmètre V1 (émission oui / réception via portail PA) | [guide opérateur §1](../guide-operateur.md), [guide éditeur §2](../guide-editeur.md), Offre §6 |
| Règle d'or (bloquer plutôt qu'envoyer faux) et résolution des blocages | [guide opérateur §7/§8/§9](../guide-operateur.md) |
| Cycle de vie, statuts, onglet « Contrôles » | [guide opérateur §3/§4/§6](../guide-operateur.md) |
| Rejets PA, erreurs techniques, file de push | [guide opérateur §8](../guide-operateur.md), [FAQ §C](faq-editeur.md) |
| Agent muet, service Windows, redémarrage, seuil 24 h | [guide éditeur §8](../guide-editeur.md), [F12 §5.2](../conception/F12-Architecture-Plateforme-Agent.md) |
| Catalogue d'alertes, gravités ; état d'implémentation (centralisé) | [F12 §5.2](../conception/F12-Architecture-Plateforme-Agent.md), [matrice §3](matrice-escalade.md), [guide éditeur §8](../guide-editeur.md) |
| Niveaux N1/N2/N3, escalade, informations minimales du ticket | [matrice §1/§2/§5](matrice-escalade.md), [FAQ §G](faq-editeur.md) |
| Aucune règle fiscale inventée ; question fiscale → expert-comptable ; pas de secret en clair | CLAUDE.md règles n°2 et n°10 |

> **Aucun chiffre de SLA n'apparaît dans cette formation.** Les délais sont « à contractualiser »
> ([matrice §4](matrice-escalade.md), action OE-4 de l'offre) ; le N1 retient les **gravités** et le
> **premier traitement**, pas un délai contractuel non encore arrêté.

---

## Voir aussi

- [FAQ éditeur](faq-editeur.md) — réponses de niveau 1 aux questions récurrentes (le support de référence du N1)
- [Matrice d'escalade](matrice-escalade.md) — qui traite quoi, SLA, canal, informations à fournir
- [Guide opérateur](../guide-operateur.md) — la console web, écran par écran
- [Guide éditeur / opérateur d'instance](../guide-editeur.md) — supervision, alertes, flotte, agent
- [Veille réglementaire](veille-reglementaire.md) — le processus N3 réglementaire
- [Offre Éditeur §5](../market/Offre-Editeur-Passerelle.md) — le support : qui fait quoi (source)
