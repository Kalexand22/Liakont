# FAQ / base de connaissances — support éditeur

> **But.** Réponses de **niveau 1 (N1, sans escalade)** aux questions récurrentes des opérateurs
> comptables et des clients finaux. C'est la base de connaissances du support de l'éditeur (qui
> assure le N1 en marque blanche). Quand un cas dépasse le N1, voir la
> [matrice d'escalade](matrice-escalade.md).
>
> **Source.** Comportement produit décrit dans le [guide opérateur](../guide-operateur.md) et le
> [guide éditeur](../guide-editeur.md). Engagement de support : Offre Éditeur §5
> ([Offre-Editeur-Passerelle.md](../market/Offre-Editeur-Passerelle.md)). **Aucune règle fiscale
> ni aucun comportement produit n'est inventé ici** : chaque réponse renvoie à la console et aux
> guides.
>
> **Légende :** ✅ résoluble en **N1** · ↗️ **escalader** (voir [matrice](matrice-escalade.md)).

---

## A. Connexion et accès

**Q. Je ne vois pas une page / un bouton attendu (ex. « Paramétrage », « Actions »).** ✅
La console est **gouvernée par les droits** : chaque page ou action exige une **permission**
(consultation, actions, paramétrage, supervision…). Vérifiez le **rôle** de l'utilisateur (guide
opérateur §17). Si le rôle est correct mais l'élément reste masqué sous OIDC, ↗️ N2 (projection
rôle→permission).

**Q. Le client appelle : il « ne nous connaît pas ».** ✅ C'est **normal** : le produit est en
**marque blanche**. Le client final ne sait pas qu'IT Innovations existe ; il appelle **sa
hotline habituelle** (l'éditeur). Ne révélez pas l'infrastructure sous-jacente.

---

## B. Documents bloqués et messages de contrôle

> **Règle d'or à rappeler au client :** Liakont **bloque plutôt que d'envoyer un document faux**.
> Un blocage **protège la responsabilité fiscale** du client — il se **résout**, il ne se
> contourne jamais. Aucune validation bloquante n'est désactivée pour « faire passer » un envoi
> (guide opérateur §8).

**Q. Un document est 🛑 Bloqué — comment savoir pourquoi ?** ✅ La console **dit toujours
pourquoi** : onglet **« Contrôles »** du détail du document. Le motif est en français et
actionnable. Après correction, cliquez **« Revérifier »** (sur le document) ou **« Revérifier
tout »** (sur la liste).

**Q. Bandeau « PARAMÉTRAGE INCOMPLET » (profil tenant absent).** ✅→ relais opérateur d'instance :
le **profil légal du tenant** n'a pas été importé. C'est un geste de l'**opérateur d'instance**
(éditeur ou IT Innovations), voir guide éditeur §4. Si l'import échoue, ↗️ N2.

**Q. « Table TVA NON VALIDÉE — les envois sont suspendus jusqu'à validation. »** ✅ La table de
mapping TVA existe mais n'est pas validée : **tous les envois du tenant sont suspendus** tant
qu'elle ne l'est pas. Faites compléter puis **valider** la table par la personne responsable
(expert-comptable du client). Voir question F.

**Q. Document bloqué pour « TVA non mappée ».** ✅ Le régime TVA du document n'a **pas de règle**
dans la table. Créez la règle manquante (« Créer une règle pour {régime} »), **revalidez** la
table, puis **« Revérifier »** le document (guide opérateur §9). Si le régime fiscal lui-même est
incertain, c'est une question pour l'**expert-comptable du client** (pas pour le support : nous
n'inventons aucune règle fiscale).

**Q. Document bloqué par le « garde-fou B2B/B2C ».** ✅ Un acheteur **professionnel** n'a pas été
confirmé. Tranchez le verdict depuis le garde-fou (guide opérateur §7). C'est un geste opérateur,
pas une escalade.

---

## C. Rejets PA et erreurs techniques

**Q. Document ❌ Rejeté par la PA — que faire ?** ✅ puis ↗️ si récurrent : lisez le **motif de
rejet** (onglet « Contrôles »), corrigez la **donnée source**, puis **renvoyez** ou traitez
manuellement / remplacez (guide opérateur §8). Si le **même motif revient** sur plusieurs
documents (mapping, défaut produit) ou si la PA elle-même refuse un flux valide, ↗️ **N2** (puis
N3 côté PA).

**Q. Document ⚠️ Erreur technique.** ✅ **Aucune action** : un incident réseau/API est
**re-tenté automatiquement** au prochain traitement. **Si cela persiste**, ↗️ N2.

**Q. La file de push « grossit » / une alerte le signale.** ↗️ **N2** : des erreurs de push
répétées (réseau, API PA, payload) ne se règlent pas en N1. Joignez le tenant et l'horodatage de
l'alerte (voir [matrice §5](matrice-escalade.md)).

---

## D. L'agent (poste client)

**Q. Alerte « Agent muet » — le tableau de bord ne reçoit plus rien d'un poste.** ✅ d'abord :
1. le **poste est-il allumé** et connecté ?
2. le **service Windows « Liakont Agent »** est-il **démarré** ? Le redémarrer (guide éditeur §8).

Si l'agent **ne repart pas**, ou si la cause est une **clé API invalide / révoquée** ou un
problème d'installation, ↗️ **N2**.

**Q. Au bout de combien de temps l'agent est-il déclaré « muet » ?** ✅ Seuil **par défaut 24 h**
sans heartbeat (paramétrable par tenant dans `/parametrage/alertes`). Référence : F12 §5.2.

**Q. L'agent est dans une version ancienne.** ✅→↗️ N2 : les agents se mettent à jour par
**auto-update** (manifeste signé + vérification de hash), piloté par la **politique de flotte**
côté plateforme (guide éditeur §9). Si une version reste obsolète (< N-1), ↗️ N2 pour vérifier la
politique d'auto-update.

---

## E. Échéances déclaratives

**Q. Une échéance approche et des documents ne sont **pas transmis**.** ✅ Traitez en priorité :
**débloquez** (sections B/C) et laissez le pipeline transmettre. Le produit prévoit une alerte
🔴 **« Échéance déclarative proche »** (J-3 par défaut, F12 §5.2) — **note** : son évaluation
automatique fait partie des règles **non encore activées** au catalogue (état d'implémentation à
jour : [matrice §3](matrice-escalade.md), qui fait foi) ; ne vous reposez pas sur une alerte
automatique, **surveillez activement** les documents bloqués à l'approche d'une échéance.
En cas de **blocage technique sous échéance**, ↗️ N2 sans attendre.

**Q. Le produit gère-t-il la **réception** des factures fournisseurs ?** ✅ **Non en V1.** La
réception est assurée par le **portail de la Plateforme Agréée** du client (inclus dans son
compte) ; l'intégration native est en **phase 2**. Ne jamais laisser croire que le produit couvre
la réception (guide opérateur §1, guide éditeur §2, Offre §6).

---

## F. Table TVA et paramétrage comptable

**Q. Qui complète la table de mapping TVA ?** ✅ **Le client (ou son expert-comptable)** —
**Liakont n'invente aucune règle fiscale**. La table fait correspondre chaque régime TVA du
logiciel source à la catégorie TVA normalisée de la réforme (guide opérateur §9, réf.
`docs/conception/F03-Mapping-TVA.md`).

**Q. Pourquoi toute modification de la table re-suspend les envois ?** ✅ C'est **volontaire** :
toute édition d'une règle remet la table en **NON VALIDÉE** (envois suspendus) et est **tracée**.
Une table fiscale ne se modifie jamais sans **revalidation explicite** par la personne
responsable. Revalidez quand la couverture est complète (« Valider »).

**Q. Comment voir quels régimes ne sont pas encore couverts ?** ✅ La console affiche un **rapport
de couverture** : les régimes non couverts sont listés (guide opérateur §9).

**Q. Mon client fait des ventes aux enchères.** ✅ Le **vertical « enchères »** s'active/désactive
par tenant (« Activer / Désactiver »). Laissez-le **désactivé** si le client n'est pas concerné
(guide opérateur §9).

---

## G. Quand escalader (récapitulatif)

| Vous êtes en N1 et… | Action |
|---|---|
| Le motif de blocage est un **paramétrage console** (table TVA, garde-fou, profil, droits) | ✅ Résoudre en N1 (sections A/B/F) |
| L'agent est **muet** mais redémarre | ✅ N1 (section D) |
| L'agent **ne redémarre pas**, clé API invalide, install KO | ↗️ **N2** |
| **Rejets PA récurrents** ou même motif sur plusieurs documents | ↗️ **N2** (→ N3 si PA/format) |
| **Erreur technique qui persiste**, file de push qui grossit | ↗️ **N2** |
| **Question fiscale** (quelle catégorie TVA pour un régime ?) | → **expert-comptable du client** (jamais inventer une règle) |
| **Évolution réglementaire / de format** | ↗️ **N3** (voir [veille](veille-reglementaire.md)) |

> À chaque escalade, joignez les **informations minimales** du ticket (tenant, version d'agent,
> documents, alerte, motif) — [matrice §5](matrice-escalade.md). **Jamais de secret en clair**
> (clé API, ODBC) dans un ticket.

---

## Voir aussi

- [Matrice d'escalade](matrice-escalade.md) — qui traite quoi, SLA, canal
- [Formation du support N1](formation-n1-editeur.md)
- [Guide opérateur](../guide-operateur.md) · [Guide éditeur](../guide-editeur.md)
- [Veille réglementaire](veille-reglementaire.md)
