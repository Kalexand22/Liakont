# Scénario de démo ISATECH — déploiement CMP (Crédit Municipal de Paris)

> Lot **CMP03**. Scénario pas à pas, minuté (15-20 min), exécutable par une personne qui n'a **pas**
> développé le produit. Il s'appuie **exclusivement** sur le paramétrage versionné de ce dossier
> (`deployments/cmp/`, lots CMP01/CMP02) et sur des pages de la console qui existent réellement
> (chemins vérifiés ci-dessous). Aucune donnée client réelle n'est manipulée : la démo tourne en
> **mode fixtures** (acheteurs fictifs), table TVA **NON VALIDÉE**, donc **aucun envoi réel** à
> l'administration (garde-fou PIP01b).
>
> **Public** : ISATECH (éditeur d'EncheresV6) et/ou le CMP. La preuve à délivrer : *le produit se
> déploie par paramétrage, sans toucher au code source d'EncheresV6, et l'extraction se fait sans
> rien installer dans le logiciel métier de l'étude*.
>
> ⚠️ **Usage interne uniquement, à NE PAS montrer ni évoquer en démo** : l'analyse de situation
> d'ISATECH (`docs/market/DR12-Intelligence-ISATECH.md`, redressement judiciaire) est de
> l'intelligence commerciale interne. Le discours de démo reste sur le produit et l'offre
> (`docs/market/Offre-Editeur-Passerelle.md`). Ne jamais laisser entendre qu'on connaît la situation
> financière de l'interlocuteur.

---

## 0. Prérequis et préparation (à faire AVANT la démo — hors des 20 minutes)

| # | Prérequis | Détail / source |
|---|---|---|
| 1 | **Instance de démo provisionnée** | Appliance Docker OU instance mutualisée IT Innovations, console accessible, base vierge (aucun tenant). |
| 2 | **Dossier de seed CMP disponible** | Une copie de `deployments/cmp/` accessible depuis la machine où tourne la console (pour l'import au pas n°2). |
| 3 | **Agent installé sur une machine de démo** | Agent Liakont installé (installateur OPS08) sur un poste **propre**, **non encore démarré**, configuré avec `deployments/cmp/agent-cmp.json.example` en **mode fixtures** (`extraction.fixturesPath` → `fixtures-demo/`, `extraction.pdfPoolPath` → `fixtures-demo/pdf-pool/`). Clé API et URL plateforme renseignées (placeholders du `.example` remplacés par les valeurs de l'instance de démo). |
| 4 | **Branding appliqué à l'instance** | Logo + couleurs de la marque grise configurés au niveau de l'instance (`BrandingOptions` — `appsettings`/variables d'environnement, rendu par `BrandingHead.razor`). La marque grise se **constate** à l'écran ; elle ne se règle pas depuis une page opérateur (configuration de déploiement). |
| 5 | **Comptes de démonstration** | Un compte opérateur avec la permission `liakont.actions` (sinon réconciliation PDF et actions document masquées) et un compte super-admin (accès `/supervision` et `/admin/audit`). |
| 6 | **Répétition complète effectuée** | La démo DOIT avoir été déroulée de bout en bout sur cette instance propre **avant** la séance (voir §4). Un blocage technique en direct grille la crédibilité. |

> **Mode fixtures, pas de base Pervasive** : la machine d'orchestration et la machine de démo ne
> portent **jamais** la base EncheresV6 réelle. La première extraction ODBC réelle est validée
> séparément, sur le **serveur dédié**, et c'est la gate humaine `GATE_DEMO_ISATECH` qui la porte
> (voir `orchestration/items/CMP.yaml`). La démo prouve la chaîne fonctionnelle ; la preuve du schéma
> réel est un jalon distinct.

---

## 1. Scénario pas à pas (15-20 min)

Minutage indicatif (séance de 18 min). Les chemins entre crochets sont les routes réelles de la
console.

### Acte 1 — « On crée le client sans écrire une ligne de code » (≈ 3 min)

1. **[0:00] Ouvrir la console** sur l'instance de démo, base vierge. Faire remarquer le **branding** :
   logo et couleurs ne sont **pas** ceux de Liakont — c'est la **marque grise**. *Message : « ce que
   vos études verront, c'est VOTRE marque, pas la nôtre. »*
2. **[0:30] Créer le tenant CMP** via l'assistant d'onboarding — page **`/clients/nouveau`**
   (`ClientNouveau.razor`, assistant OPS03, 5 étapes : Profil → Seed → Création → Utilisateurs →
   Agent). À l'étape **Seed**, importer le dossier `deployments/cmp/` : profil (SIREN public
   267500007, raison sociale « Crédit Municipal de Paris »), table de mapping TVA, comptes PA
   (B2Brouter **staging**, clé en placeholder — **saisie manuelle en console**, jamais importée), et
   planification d'extraction à 03:00.
3. **[2:00] Montrer l'idempotence** : l'étape Création liste ses sous-opérations ; en cas d'échec
   partiel, on **reprend** sans tout rejouer (pas de rollback destructeur). *Message : « un
   déploiement, c'est de la configuration, pas un chantier. »*

> **Point de valeur** : *extraction et déploiement sans toucher au logiciel source*. Le tenant est
> né d'un fichier de paramétrage versionné — la preuve que la grappe ISATECH se déploie en série.

### Acte 2 — « Les données arrivent toutes seules » (≈ 3 min)

4. **[3:00] Démarrer l'agent** sur la machine de démo (mode fixtures). Il lit les bordereaux fictifs
   (`fixtures-demo/encheresv6-demo.json`) et les **pousse** à la plateforme. *Message : « l'agent lit
   la base EncheresV6 en lecture seule — aucune modification du logiciel de l'étude, aucun plug-in à
   installer dedans. »*
5. **[3:45] Ouvrir la liste des bordereaux** — page **`/documents`** (`Documents.razor`). Les 4
   bordereaux de démo apparaissent : vente normale `7001`, régime de la marge `7002`, acheteur
   professionnel `7003`, avoir `7050`. Montrer les **filtres** (période, type, recherche libre) et le
   bandeau de **compteurs par état**.

> **Point de valeur** : *le push arrive, le pipeline tourne*. Rien n'a été saisi à la main.

### Acte 3 — « Les blocages sont expliqués en français » (≈ 5 min)

6. **[5:00] Ouvrir le détail d'une vente normale** `7001` — page **`/documents/{id}`**
   (`DocumentDetail.razor`). Onglet **Contenu** : décomposition ligne à ligne
   *régime source → catégorie TVA → VATEX → taux*, montants à 2 décimales. *Message : « chaque
   facture est traçable, ligne par ligne, sans jargon. »*
7. **[6:30] Ouvrir le régime de la marge** `7002`. Expliquer la subtilité métier en clair :
   adjudication en **régime de la marge** (TVA jamais distincte, art. 297 E CGI), mais **frais
   acheteur taxables** même quand l'adjudication est exonérée (`docs/conception/F03-Mapping-TVA.md`
   §2.3). *Message : « le produit connaît VOS règles métier d'enchères — c'est ça, un adaptateur
   spécialisé. »*
8. **[8:00] Ouvrir l'acheteur professionnel** `7003` (`acheteur_siren` fictif `800000002`, non
   attribué dans SIRENE). Onglet **Contrôles** : le **verdict B2B** signale un acheteur professionnel.
   Depuis la **barre d'actions du document** (`DocumentActionBar.razor`), montrer l'action **« Traiter
   manuellement (B2B) »** : le produit **détecte** le cas et propose le traitement adéquat plutôt que
   d'émettre à l'aveugle. *Message honnête : « le B2B est sous garde-fou en V1 — détecté et tracé,
   traité au cas par cas ; on ne fait pas semblant de couvrir le B2B international. »*
9. **[9:30] Montrer le garde-fou d'envoi** : la table de mapping TVA du CMP est **NON VALIDÉE**
   (en attente expert-comptable). Le garde-fou PIP01b **suspend tout envoi réel**. *Message : « tant
   que votre expert-comptable n'a pas validé la table, RIEN ne part à l'administration. Bloquer plutôt
   qu'envoyer faux. »*

> **Point de valeur** : *blocages expliqués en français + sécurité fiscale*. On ne transmet jamais une
> donnée fiscale douteuse.

### Acte 4 — « Le paramétrage TVA, c'est l'opérateur qui le tient » (≈ 2 min)

10. **[10:30] Ouvrir la table TVA** — page **`/parametrage/table-tva`** (`TableTva.razor`). Montrer le
    statut **NON VALIDÉE**, les régimes 5 (assujetti 20 %) et 6 (marge) mappés, et qu'un régime non
    couvert apparaîtrait **« à compléter »**. Ouvrir l'éditeur de règle (`TvaRuleEditor`) : la
    catégorie TVA est une **liste fermée** (UNCL5305) — pas de saisie libre sur un champ fiscal. Le
    produit avertit si une règle est **morte** (ne s'appliquera jamais). *Message : « compléter un
    régime, c'est trois champs guidés, pas une migration. »*
11. **[12:00] Expliquer la validation** : la table passe « validée » via un workflow explicite (l'opérateur
    re-saisit son nom). *En démo on NE valide PAS* — la validation est l'acte fiscal de l'expert-comptable
    du CMP (voir `DECISIONS-FISCALES.md`). *Message : « la responsabilité fiscale reste chez qui la
    porte. »*

### Acte 5 — « Réconciliation, audit, archivage » (≈ 3 min)

12. **[12:30] Réconciliation PDF** — page **`/reconciliation`** (`Reconciliation.razor`, permission
    `liakont.actions`). Montrer les trois files : propositions à confirmer, PDF orphelins, documents
    sans PDF. Aperçu PDF natif (les PDF factices de `fixtures-demo/pdf-pool/`, rapprochés par numéro
    de bordereau). *Message : « le justificatif d'origine est rattaché à chaque facture. »*
13. **[14:00] Export d'audit** — page **`/admin/audit`** (`AdminAudit.razor`). Montrer la piste
    d'audit filtrable (acteur, entité, action, période) et l'**export CSV**. *Message : « tout est
    horodaté, immuable, exportable — la piste d'audit est append-only. »* Mentionner l'**archivage
    10 ans** (coffre WORM) sans nécessairement l'ouvrir.

### Acte 6 — « On voit la panne avant le client » (≈ 2 min)

14. **[15:00] Dashboard de supervision** — page **`/supervision`** (`Supervision.razor`). Vue
    multi-tenants : alertes, état de l'agent, files de documents (bloqués / rejetés PA / en attente),
    tenants critiques en tête. Le **bandeau de vivacité** (`SupervisionLivenessBanner.razor`) montre
    que la supervision est **active** (heartbeat de l'agent visible).
15. **[16:00] Simuler l'agent muet** : **arrêter l'agent** sur la machine de démo. Au prochain cycle,
    le bandeau bascule en **« SUPERVISION EN RETARD »** (dead-man's-switch) et le tenant remonte en
    alerte. *Message : « on détecte le silence de l'agent — la panne se voit côté supervision avant
    que votre étude n'appelle. »*

> **Point de valeur** : *supervision proactive*. C'est le pilier économique du modèle (support N2
> maîtrisé — `docs/market/Offre-Editeur-Passerelle.md` §4-5).

### Clôture (≈ 1 min)

16. **[17:00] Réversibilité** : depuis la liste des clients (**`/clients`**), montrer l'action
    **« Exporter ce client »** (`ClientExportDialog.razor`) — un ZIP avec le coffre d'archive (WORM +
    preuves d'ancrage), les documents, la configuration (clés masquées) et le journal d'audit.
    *Message : « si vous voulez partir, vous repartez avec tout. Pas de prise d'otage des données. »*
17. **[17:30] Généricité multi-PA** : rappeler que le comportement est piloté par les **capacités
    déclarées** du plug-in PA, comparées **entre les plug-ins DISPONIBLES à la date de la démo**
    (B2Brouter vs plug-in *Fake* au minimum ; vs Super PDP si `GATE_PA_SUPERPDP` est passée — voir
    l'état du backlog). *La démo ne dépend JAMAIS de la disponibilité d'un PA tiers.* *Message : « le
    produit ne dépend pas d'UN prestataire — vous n'êtes captif de personne. »*

---

## 2. Points de valeur différenciants (récapitulatif)

| Valeur | Où c'est montré | Pourquoi ça compte pour ISATECH/le CMP |
|---|---|---|
| **Extraction sans toucher au logiciel source** | Actes 1-2 (création tenant, agent en lecture seule) | EncheresV6 n'est pas modifié, aucun plug-in dans le logiciel de l'étude. |
| **Blocages expliqués en français** | Acte 3 (détail document, contrôles) | Le N1 de l'éditeur comprend, escalade moins (`Offre` §5). |
| **Sécurité fiscale (bloquer plutôt qu'envoyer faux)** | Acte 3 pas 9, Acte 4 | Aucune donnée fiscale douteuse transmise ; responsabilité protégée. |
| **Supervision proactive (agent muet)** | Acte 6 | « On voit la panne avant le client » — protège la marge de support. |
| **Piste d'audit + archivage 10 ans** | Acte 5 | Conformité légale + preuve immuable. |
| **Marque grise** | Acte 1 (branding instance) | L'étude voit la marque de SON éditeur — l'éditeur garde la relation client. |
| **Réversibilité** | Clôture pas 16 | Répond frontalement à « et si vous disparaissez ? ». |
| **Généricité multi-PA** | Clôture pas 17 | Pas de dépendance à un prestataire unique. |

---

## 3. Objections probables et réponses

Réponses tirées de `docs/market/Offre-Editeur-Passerelle.md` (§8, §10) et
`docs/market/DR12-Intelligence-ISATECH.md`. **Rester sur le produit et l'offre.**

| Objection | Réponse |
|---|---|
| **« Où sont hébergées les données ? »** | Selon la topologie retenue (à trancher pour le CMP — `tasks/todo.md`) : tenant sur l'instance mutualisée IT Innovations, instance dédiée, ou appliance on-premise chez le CMP. Données fiscales chiffrées, archivage WORM 10 ans. Un DPA / contrat d'hébergement est signé si l'hébergement est mutualisé. |
| **« Que se passe-t-il si vous disparaissez ? »** | **Réversibilité** : export complet d'un tenant en un clic (coffre d'archive, documents, configuration, audit) — démontré en clôture. Côté contrat : **séquestre du code / licence de survie** au profit de l'éditeur (`Offre` §10). L'archivage des clients est restituable, vérifiable avec un outil externe (`tools/verifier-integrite-archive.ps1`, `docs/guide-restitution-reversibilite.md`). |
| **« Et la réception des factures fournisseurs ? »** | **Réponse honnête : NON couverte par le produit en V1.** L'obligation de réception (1ᵉʳ sept. 2026) est assurée par le **portail de la Plateforme Agréée** du client (inclus dans son compte). L'intégration native de la réception est en **phase 2** (backlog — `DR16`). **Ne jamais laisser croire que le produit couvre la réception.** |
| **« Et le B2B international ? »** | **Phase 2.** En V1, le **garde-fou B2B** (détection de l'acheteur professionnel + traitement manuel, démontré à l'Acte 3) couvre le besoin. On ne survend pas le Flux 10.1. |
| **« Vous générez le Factur-X ? »** | Non — c'est la **Plateforme Agréée** qui génère la facture électronique légale à partir des données structurées qu'on lui transmet ; on récupère le Factur-X généré pour l'archivage quand la PA le permet (`Offre` §2). La promesse exacte : « vos données partent conformes, la facture légale est générée par la PA et archivée 10 ans chez vous. » |
| **« Nos études ne paieront jamais ça »** | À partir de 29 €/mois (micro) à 49-59 €/mois (standard) — le prix de 2-3 heures de saisie évitées par mois. L'alternative : ressaisir chaque bordereau dans un portail, ou l'amende par facture non conforme (`Offre` §6, §8). |
| **« Pourquoi vous laisser accéder à notre schéma ? »** | NDA + l'adaptateur ne fonctionne **qu'avec** notre moteur ; le schéma ne sort jamais de la mission ; l'éditeur garde la relation client (`Offre` §8). |

---

## 4. Déroulé de répétition (avant la gate `GATE_DEMO_ISATECH`)

La démo doit avoir été **exécutée de bout en bout** sur une machine/instance propre avant la séance.

1. **Instance propre** : base vierge, console accessible, branding appliqué.
2. **Agent propre** : installé sur un poste neuf (installateur OPS08), configuré en mode fixtures avec
   `agent-cmp.json.example`, **non démarré**.
3. **Dérouler les 6 actes** (§1) dans l'ordre, montre en main, en notant tout écart de minutage ou
   tout pas qui « ne tombe pas juste ».
4. **Tester la simulation d'agent muet** (pas 15) : vérifier que le bandeau bascule bien en
   « SUPERVISION EN RETARD » après l'arrêt de l'agent, dans un délai compatible avec la séance
   (ajuster la cadence d'évaluation de la supervision si besoin sur l'instance de démo).
5. **Vérifier les permissions** : le compte de démo voit bien `/reconciliation` (permission
   `liakont.actions`) et le compte super-admin voit `/supervision` et `/admin/audit`.
6. **Checklist de fin de répétition** :
   - [ ] Création du tenant depuis le seed : OK, idempotente
   - [ ] Push agent → bordereaux visibles dans `/documents`
   - [ ] Détail document : régime de la marge et verdict B2B lisibles en français
   - [ ] Garde-fou d'envoi actif (table NON VALIDÉE → aucun envoi)
   - [ ] Table TVA : éditeur de règle (liste fermée) fonctionnel
   - [ ] Réconciliation PDF : aperçu natif OK
   - [ ] Export d'audit CSV : OK
   - [ ] Supervision : heartbeat visible, bascule « en retard » à l'arrêt de l'agent
   - [ ] Export de réversibilité (ZIP) : OK
   - [ ] Branding marque grise visible partout

> **Rappel gate** : `GATE_DEMO_ISATECH` (humaine) valide en plus la **première extraction ODBC réelle**
> sur le **serveur dédié** (test-odbc → extraction → push → pipeline). Tout écart de schéma constaté
> est consigné et renvoyé au lot **ADP** — jamais contourné en dur (`orchestration/items/CMP.yaml`).
