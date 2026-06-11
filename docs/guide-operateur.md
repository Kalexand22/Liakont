# Guide de l'opérateur — Console web Liakont

**Public :** opérateur comptable (utilisateur de la console web). Aucune compétence
technique n'est requise.
**Objet :** utiliser Liakont au quotidien — suivre les documents, lever les blocages,
paramétrer la table TVA, réconcilier les PDF, exporter la piste d'audit.

> Ce guide décrit la console **telle qu'elle est livrée**. Les libellés entre guillemets
> (« … ») sont ceux affichés à l'écran. Les écrans dépendent de vos **autorisations**
> (voir [§ Qui peut faire quoi](#qui-peut-faire-quoi)) : certaines actions ou pages
> peuvent ne pas vous être visibles.

---

## 1. Ce que Liakont fait — et ne fait PAS (périmètre V1)

Liakont est une **passerelle de conformité** : elle prend les factures **émises** par
votre logiciel métier (factures clients et avoirs), les normalise, les contrôle, puis les
**transmet à votre Plateforme Agréée** (PA) et conserve la **piste d'audit** pendant 10 ans.

> **⚠️ La réception des factures fournisseurs n'est PAS couverte par Liakont en V1.**
> Vos factures **fournisseurs** (factures que vous recevez) continuent d'être reçues et
> consultées via le **portail de votre Plateforme Agréée**, pas via Liakont. C'est une
> décision de périmètre assumée (Offre Éditeur réalignée le 2026-06-02) : la réception
> rejoindra le produit dans une phase ultérieure. Ne vous attendez pas à voir vos achats
> dans cette console.

Concrètement, dans Liakont vous travaillez **uniquement** sur les documents que votre
logiciel a émis et que l'**agent d'extraction** a remontés à la plateforme.

---

## 2. Se connecter

1. Ouvrez l'adresse de votre instance Liakont dans un navigateur.
2. La page « Connexion » vous redirige vers le fournisseur d'identité de l'instance
   (Keycloak) : saisissez vos identifiants professionnels.
3. Après connexion, vous arrivez sur le **Tableau de bord**.

Vos droits dépendent du **rôle** qui vous a été attribué (lecture, opérateur,
paramétrage, supervision). Si une action vous est refusée, le message vous indique
l'autorisation manquante et de vous rapprocher de l'administrateur de votre organisation.

Pour vous déconnecter, utilisez l'action de déconnexion du menu utilisateur.

---

## 3. Le tableau de bord (`/`)

Page d'accueil en **lecture seule** : une vue d'ensemble de l'état de votre dossier.

Vous y trouvez :

- **L'état du paramétrage** du tenant (profil légal, paramètres fiscaux).
- **La répartition des documents** par état (voir le cycle de vie au § 4).
- **L'état des agents d'extraction** (Actif / Révoqué) et leur dernière remontée.
- **L'état de la table TVA** (Validée / NON VALIDÉE / non paramétrée).
- **La fréquence déclarative** (si elle est renseignée).

Le tableau de bord met en avant les **deux blocages majeurs** qui suspendent tout
traitement (voir aussi § 8) :

| Bandeau | Signification | Quoi faire |
|---|---|---|
| **PARAMÉTRAGE INCOMPLET** — « Le profil du tenant n'est pas encore créé — le traitement des documents est suspendu. Importez le paramétrage du tenant (profil légal) pour reprendre le traitement. » | Le profil légal (SIREN, raison sociale) n'a pas été créé. | Le profil doit être importé par l'opérateur d'instance (voir le [guide de l'éditeur](guide-editeur.md)). Tant qu'il manque, rien ne part. |
| **NON VALIDÉE** — « Les envois sont suspendus jusqu'à validation par l'expert-comptable. » | La table TVA existe mais n'a pas été validée. | Faites valider la table TVA (§ 6). |

Messages d'information complémentaires possibles :
« Fréquence déclarative non renseignée — consultez votre expert-comptable. »

---

## 4. Le cycle de vie d'un document

Chaque document remonté par l'agent passe par une suite d'**états**. La console les
affiche avec une pastille de couleur. Voici la liste complète (libellés exacts) :

| Pastille | État | Signification |
|---|---|---|
| ⏳ **À envoyer** | détecté | Lu dans le logiciel source, en attente de traitement. |
| 📤 **Prêt à envoyer** | contrôlé, OK | A passé les contrôles, prêt pour la transmission. |
| ⏫ **En cours** | transmission engagée | L'envoi vers la PA est en cours. |
| 🛑 **Bloqué** | contrôle échoué | Un contrôle qualité l'a refusé. **Action requise** (voir § 8). |
| ⚠️ **Erreur technique** | incident réseau/API | Problème de transmission ; **re-tenté automatiquement** au prochain traitement. |
| ❌ **Rejeté** | refusé par la PA | La Plateforme Agréée a refusé le document ; consultez le motif. |
| ✅ **Émis** | accepté par la PA | Transmis et accepté ; preuve de transmission disponible. |
| ↪ **Remplacé** | terminal | Renvoyé sous un nouveau numéro après rejet (le numéro vient toujours de la source). |
| ✋ **Traité manuellement** | terminal | Résolu hors passerelle par un opérateur, avec motif obligatoire et journalisé. |

> Référence fonctionnelle : `docs/conception/F06-Tracking-Piste-Audit.md` (états et piste
> d'audit), `docs/conception/F07-F08-Avoirs-Frontiere-B2B-B2C.md` (avoirs, frontière B2B/B2C).

---

## 5. La liste des documents (`/documents`)

C'est votre écran de travail principal. Il liste tous les documents du dossier.

### Filtres

- **Période** : borne les documents par date (rechargement côté serveur). Par défaut, la
  période couvre le mois courant.
- **État** : n'affiche que les documents d'un état donné.
- **Type** : Facture, Avoir, etc.

Un **compteur par état** est affiché en permanence pour la période et le type retenus.
Si rien ne correspond : « Aucun document ne correspond à ces critères. »

### Actions (autorisation « actions » requise)

- **« Tout envoyer »** : émet **tous** les documents *prêts à envoyer* du dossier.
- **« Lancer un traitement »** : déclenche un cycle de traitement du pipeline.
- **« Envoyer la sélection »** : action en masse sur les documents cochés. ⚠️ **Attention :**
  l'envoi émet **tous** les documents prêts à l'envoi du tenant, pas seulement votre
  sélection — la console vous le rappelle explicitement avant de confirmer.
- **« Revérifier la sélection »** / **« Revérifier tout »** : relance les contrôles sur les
  documents bloqués (utile après avoir corrigé la table TVA ou les données source). La
  console affiche ensuite un bilan « N débloqués, N restés bloqués ».
- **« Voir »** : ouvre le détail d'un document.

> **L'envoi est IRRÉVERSIBLE.** Avant toute émission, la console affiche le nombre de
> documents concernés et le montant total, et vous demande une confirmation explicite :
> « Vous allez envoyer N document(s) prêt(s) à l'envoi, pour un montant total de M €.
> Cette action est IRRÉVERSIBLE. »

---

## 6. Le détail d'un document (`/documents/{id}`)

Titre : « Document {numéro} ». Vous y voyez le client, le SIREN, les montants (HT, TVA,
TTC), la date, l'état, et **quatre onglets** : **Contenu**, **Contrôles**, **Historique**,
**Archive**.

- **Contenu** : les données normalisées (modèle pivot EN 16931).
- **Contrôles** : le détail des contrôles qualité, et **si le document est bloqué, le motif
  exact** du blocage (« Ce qui a déclenché le blocage : … »).
- **Historique** : la chronologie des événements (la piste d'audit du document).
- **Archive** : le lien vers le paquet d'archive fiscal (export possible, voir § 10).

### Actions disponibles selon l'état (autorisation « actions »)

- **« Revérifier maintenant »** (si Bloqué) : relance les contrôles sur ce document.
- **« Envoyer »** (si Prêt à envoyer) : déclenche le traitement d'envoi du tenant — tous
  les documents prêts sont émis, celui-ci inclus.
- **Garde-fou B2B/B2C** (si Bloqué pour cette raison) : voir § 7.

Messages d'erreur courants : « Impossible de charger ce document pour le moment. Réessayez
plus tard. », « Ce document est introuvable. Il a peut-être été remplacé, ou il n'appartient
pas à ce tenant. »

---

## 7. Le garde-fou B2B / B2C

Certaines opérations exigent de savoir si l'acheteur est un **professionnel (B2B)** ou un
**particulier (B2C)** : le traitement et l'obligation déclarative diffèrent. Quand Liakont
détecte un acheteur qui **semble professionnel** mais que la qualification n'est pas
certaine, il **bloque** le document et vous demande de trancher :

> « L'acheteur semble être un professionnel (garde-fou B2B/B2C). Tranchez : »

Deux boutons (autorisation « actions ») :

- **« Confirmer particulier (B2C) »** : vous certifiez qu'il s'agit d'un particulier ; le
  document repart dans le circuit normal.
- **« Traiter manuellement (B2B) »** : vous prenez en charge le cas professionnel hors
  passerelle ; le document passe en « Traité manuellement ».

> Ce garde-fou est une **règle de conformité** : il évite d'émettre un document mal qualifié.
> Référence : `docs/conception/F07-F08-Avoirs-Frontiere-B2B-B2C.md`. En cas de doute sur la
> qualification d'un acheteur, rapprochez-vous de votre expert-comptable **avant** de trancher.

---

## 8. Lever un blocage — messages courants et résolution

Un document **🛑 Bloqué** n'est jamais transmis tant qu'il n'est pas corrigé. La console
vous dit **toujours pourquoi** (onglet « Contrôles » du détail) et **qui** résout quoi.

| Symptôme / bandeau | Cause | Résolution |
|---|---|---|
| **PARAMÉTRAGE INCOMPLET** (profil tenant absent) | Le profil légal n'a pas été importé. | À traiter par l'opérateur d'instance ([guide éditeur](guide-editeur.md)). |
| **Table TVA NON VALIDÉE** — « Les envois sont suspendus jusqu'à validation par l'expert-comptable. » | La table TVA n'est pas validée. | Compléter puis **valider** la table TVA (§ 9). |
| Document **Bloqué** avec un motif de **TVA non mappée** | Le régime TVA du document n'a pas de règle dans la table. | Créer la règle manquante dans la table TVA (§ 9), **revalider** la table, puis **« Revérifier »** le document. |
| Document **Bloqué** pour **garde-fou B2B/B2C** | Acheteur professionnel non confirmé. | Trancher via le garde-fou (§ 7). |
| Document **❌ Rejeté** par la PA | La Plateforme Agréée a refusé. | Lire le motif (onglet Contrôles), corriger la donnée source, puis renvoyer **ou** traiter manuellement / remplacer. |
| Document **⚠️ Erreur technique** | Incident réseau/API. | Aucune action : **re-tenté automatiquement** au prochain traitement. Si cela persiste, prévenez le support. |

> **Règle d'or de conformité :** Liakont **bloque plutôt que d'envoyer un document faux**.
> Un blocage protège votre responsabilité fiscale — il faut le **résoudre**, jamais le
> contourner. Aucune validation bloquante ne doit être désactivée pour « faire passer » un
> envoi.

Après correction, utilisez **« Revérifier »** (sur le document) ou **« Revérifier tout »**
(sur la liste) pour relancer les contrôles.

---

## 9. Paramétrage comptable : la table TVA (`/parametrage/table-tva`)

La **table de mapping TVA** fait correspondre chaque régime TVA de votre logiciel source à
la catégorie TVA normalisée attendue par la réforme. **C'est vous (ou votre expert-comptable)
qui la complétez** — Liakont n'invente aucune règle fiscale.

### États de la table

- **Aucune table TVA paramétrée** : il faut d'abord la créer.
- **NON VALIDÉE** : la table existe mais les envois sont **suspendus** jusqu'à validation.
- **Validée** : avec le nom du valideur et la date.

### Le workflow de validation

1. **« Créer une table »** (si aucune n'existe).
2. **« Créer une règle »** (ou **« Créer une règle pour {régime} »** depuis un régime non
   couvert) : ajoutez une règle par régime TVA présent dans vos documents.
3. La console affiche un **rapport de couverture** : les régimes non encore couverts sont
   listés.
4. Vous pouvez **« Éditer »** ou **« Supprimer »** une règle. **Toute modification remet la
   table en NON VALIDÉE** : il faudra la revalider.
5. Quand la couverture est complète, **« Valider »** (après confirmation). La table passe en
   **Validée**, les envois sont de nouveau autorisés.

> **Important :** chaque changement de règle suspend de nouveau les envois (NON VALIDÉE) et
> est tracé. C'est volontaire : une table fiscale ne doit jamais être modifiée sans une
> revalidation explicite par la personne responsable.

### Vertical « enchères » (optionnel)

Si votre métier le requiert, vous pouvez **« Activer / Désactiver »** le vertical enchères
(règles spécifiques aux ventes aux enchères). Laissez-le désactivé si vous n'êtes pas concerné.

> Référence fonctionnelle : `docs/conception/F03-Mapping-TVA.md`. Aucune catégorie TVA ni
> aucun seuil ne doit être saisi « au jugé » : tout vient de votre paramétrage fiscal validé.

---

## 10. Réconciliation des PDF (`/reconciliation`)

Quand votre logiciel source dépose les PDF des factures **en vrac** (sans lien fiable
document ↔ fichier), Liakont propose des **rapprochements** que vous confirmez.

- **« Confirmer »** : valide le rapprochement proposé (ce PDF ↔ ce document).
- **« Rejeter »** : refuse la proposition.
- **« Lier manuellement »** : choisissez vous-même le document à associer.
- **« Aperçu »** : prévisualise le PDF.

File vide : « Aucune proposition de réconciliation en attente. »

Si votre dossier n'alimente pas de pool de PDF, la page indique : « La réconciliation des
PDF n'est pas disponible pour ce tenant : aucun pool de PDF non rattachés n'est alimenté par
l'agent. » (et l'entrée de menu n'apparaît pas).

> Référence : `docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md` (gestion des PDF
> en vrac et réconciliation).

---

## 11. Encaissements (`/encaissements`)

Liste **en lecture seule** des agrégats de paiement (par jour × taux de TVA) pour
l'e-reporting des paiements. Filtre par **mois**.

Bandeaux possibles :

- **Décision fiscale en attente** — « Décision fiscale en attente (TVA sur les débits /
  catégorie d'opération) — consultez votre expert-comptable. » : un paramètre fiscal n'est
  pas tranché ; les agrégats ne peuvent pas être finalisés tant qu'il manque.
- **En attente de la PA** — « En attente : la plateforme {PA} ne supporte pas encore la
  transmission des paiements — les agrégats sont prêts et partiront automatiquement dès
  l'activation. » : tout est prêt côté Liakont ; l'envoi se fera automatiquement quand la PA
  le supportera.

Liste vide : « Aucun encaissement agrégé sur cette période. »

> Référence : `docs/conception/F09-E-Reporting-Paiement.md`.

---

## 12. Traitements (`/traitements`)

Journal **en lecture seule** des derniers cycles du pipeline (les 200 plus récents, du plus
récent au plus ancien) : date de démarrage, nature, déclencheur, nombre de documents
traités / en échec, durée.

Journal vide : « Aucun traitement sur la période. Le pipeline n'a pas encore tourné pour ce
tenant. »

---

## 13. Paramétrage du tenant (`/parametrage`)

Page de synthèse (lecture) du dossier, avec des liens vers les écrans dédiés :

- **Profil** : SIREN, raison sociale, adresse, contact d'alerte.
- **Paramètres fiscaux** : TVA sur les débits, catégorie d'opération, fréquence déclarative.
  Un paramètre non tranché affiche « Décision en attente — paramètre non renseigné par
  l'expert-comptable. »
- **Agents** : la liste des agents (renvoi vers `/agents`).
- **Comptes PA** : les comptes Plateforme Agréée (renvoi vers `/parametrage/comptes-pa`).
- **Table TVA** : son état (renvoi vers `/parametrage/table-tva`).
- **Alertes & supervision** : renvoi vers `/parametrage/alertes`.
- **Intégrité de l'archive** : bouton **« Vérifier l'intégrité de l'archive »** (autorisation
  « paramétrage ») — produit un rapport (coffre intègre / anomalie, nombre d'entrées, chaîne
  ancrée ou non).
- **Exports d'archive** : voir § 14.

Si le profil est absent : bandeau **PARAMÉTRAGE INCOMPLET** (voir § 3).

---

## 14. Exports : piste d'audit et réversibilité

Liakont conserve une **piste d'audit immuable** et un **coffre d'archive WORM** (10 ans).
Plusieurs exports sont disponibles :

- **Export d'audit par période** (autorisation « lecture ») : depuis « Paramétrage »,
  télécharge un ZIP de l'export de contrôle fiscal pour une période donnée
  (`GET /api/v1/audit-export?from=&to=`).
- **Export du dossier d'un document** : depuis le détail d'un document, télécharge le dossier
  d'export de contrôle fiscal de **ce** document
  (`GET /api/v1/documents/{id}/audit-export`).
- **Export complet du tenant (réversibilité)** (autorisation « paramétrage ») : depuis
  « Paramétrage », télécharge un ZIP réunissant tout le paramétrage et la piste d'audit du
  dossier, **sans aucun secret** (`GET /api/v1/tenant-export`). Une confirmation est demandée.

> Ces exports ne modifient **jamais** l'archive : la piste d'audit et le coffre sont en
> écriture seule (append-only / WORM). C'est une garantie de conformité.

---

## 15. Comptes PA, Agents et Alertes (aperçu)

Ces écrans (autorisation « paramétrage ») relèvent surtout de l'administration de l'instance
et sont décrits en détail dans le **[guide de l'éditeur](guide-editeur.md)**. En résumé :

- **Comptes plateforme agréée** (`/parametrage/comptes-pa`) : configurer le compte PA
  (B2Brouter…), ses identifiants et sa clé API (chiffrée, jamais ré-affichée), et publier
  le SIREN auprès de la PA.
- **Gestion des agents** (`/agents`) : enregistrer un agent (et récupérer sa clé, **affichée
  une seule fois**), renouveler une clé, révoquer un agent.
- **Alertes & supervision** (`/parametrage/alertes`) : régler les seuils d'alerte (agent muet,
  documents bloqués, rejets PA), le contact d'alerte et la matrice de routage.

---

## 16. Préférences utilisateur (`/settings/preferences`)

Préférences personnelles (thème, langue…), propres à votre compte et indépendantes du
dossier.

---

## 17. Qui peut faire quoi

Vos écrans et actions dépendent de vos **autorisations** :

| Autorisation | Ce qu'elle permet |
|---|---|
| **lecture** (`liakont.read`) | Consulter le tableau de bord, les documents, les traitements, les encaissements, le paramétrage (lecture) ; exporter l'audit par période. |
| **actions** (`liakont.actions`) | Envoyer / revérifier des documents, trancher le garde-fou B2B/B2C, confirmer/rejeter une réconciliation. |
| **paramétrage** (`liakont.settings`) | Table TVA, comptes PA, gestion des agents, alertes, vérification d'intégrité, export complet du tenant. |
| **supervision** (`liakont.supervision`) | Accéder à la supervision multi-tenants (lecture seule). |

Si une action est refusée : « Accès refusé : … requiert l'autorisation de … . Rapprochez-vous
de l'administrateur de votre organisation. »

---

## 18. En cas de problème

1. **Lisez le message** : Liakont indique toujours le numéro du document, la cause et
   l'action corrective en français.
2. **Un agent muet ?** Si vos documents ne remontent plus, l'agent d'extraction est
   peut-être arrêté : prévenez votre opérateur d'instance (la supervision le détecte).
3. **Un doute fiscal** (catégorie TVA, B2B/B2C, fréquence) : c'est une décision de votre
   **expert-comptable**, jamais une supposition.
4. **Une erreur technique persistante** : remontez-la à votre support (niveau 1 éditeur),
   en précisant le numéro de document.

---

## Annexe — Captures d'écran sur jeu de démonstration

Les captures d'écran de ce guide se prennent sur le **jeu de démonstration fictif** (aucune
donnée client réelle), pour rester reproductibles :

1. Démarrer la plateforme en local et l'authentification (voir [README](../README.md) → *Mode
   démonstration*).
2. Enregistrer un agent dans « Gestion des agents » et récupérer sa clé.
3. Peupler la console avec des factures fictives :
   `powershell -ExecutionPolicy Bypass -File tools/dev-seed-demo-docs.ps1 -AgentKey "prefix.secret"`
   (15 documents fictifs, SIREN d'exemple `123456782`, montants en euros).
4. Capturer chaque écran décrit ci-dessus.

> Ne prenez jamais de captures sur des données client réelles pour la documentation produit.
