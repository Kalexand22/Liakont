# Recette « tous les derniers correctifs » — env Isatech (SuperPDP sandbox RÉEL)

> Objectif : re-tester **ensemble** les ~39 correctifs livrés depuis la dernière recette
> (lots e-reporting B2C #73, bugs recette EncheresV6 BUG-12→28, TVA sur marge L2, trous UX
> RBF07/08) sur l'environnement de démo **Isatech** (branding pilote Enchères SVV).
> **Décision Karl : PA = SuperPDP sandbox, envoi RÉEL** (couvre motif de rejet PA, validation
> Factur-X CTC-FR, état « E-reporté » après retour PA). ⚠️ chaque envoi crée une vraie ligne sandbox.
>
> Karl exécute les gestes console/agent ; je guide commande par commande (les commandes shell = moi si besoin).

---

## 0. Bring-up (commandes à lancer)

**Prérequis** : Docker Desktop **démarré** ✅ · SQL Server local + `sqlcmd` + **ODBC Driver 17 for SQL Server** ·
`encheresv6-demo-sqlserver.sql` présent ✅ (4,5 Mo) · **agent compilé net48 Release** · **tes creds OAuth2 SuperPDP sandbox**.

> **État actuel** : la stack `liakont-isatech` **tourne déjà** (auto-restart Docker), mais son image Host
> peut dater d'un build antérieur. Pour recetter *les derniers correctifs*, on **rebuild + base vierge** via `reset`.

| # | Étape | Commande |
|---|---|---|
| 1 | Plateforme propre + à jour (rebuild depuis main, base + realm à 0) | `powershell -ExecutionPolicy Bypass -File deployments\isatech\demo.ps1 reset` |
| 2 | Base source SQL Server + login lecture seule + SIREN démo | `powershell -ExecutionPolicy Bypass -File deployments\encheres-demo\demo.ps1 source` |
| 3 | Gabarits agent + chaîne ODBC à chiffrer | `powershell -ExecutionPolicy Bypass -File deployments\encheres-demo\demo.ps1 agent-config` |
| 4 | **Compiler l'agent net48** (sinon extraction factures/notes absente du binaire) | `dotnet build agent\Liakont.Agent.sln -c Release` |

Accès après `reset` : Console **http://localhost:8090** · Keycloak http://localhost:8081 (admin/admin) ·
login console **`sysadmin / Test@1234`** (1er login : changement MDP forcé + enrôlement 2FA TOTP).

> ⚠️ **Écart assumé vs README encheres-demo** : le README s'arrête en PA **Fake** ; ici on va jusqu'à
> **SuperPDP sandbox réel**. Donc au §1 : compte PA = **SuperPdp (Staging)** + secrets OAuth2, et **VALIDER la
> table TVA** (sans `validatedDate`, la garde **PIP01** refuse tout envoi réel — c'est l'étape qui débloque les POST).

---

## 1. Provisioning & configuration (console — tenant `volontaire` / dossier 2 d'abord)

Source de vérité des valeurs : `deployments/encheres-demo/tenant-seed/volontaire/`.

1. **Clients › Nouveau client** → tenant `volontaire`. **Saisir l'identité légale À LA MAIN** (jamais seedée) :
   raison sociale + adresse + SIREN. → *au passage, ceci teste **BUG-14** (§A) : ce que tu saisis ne doit PAS être écrasé par le seed.*
2. **Paramétrage › Fiscal** :
   - Nature d'opération = **`Prestation de services`** (pilote le TT-81 des documents ordinaires → **TPS1** ; sinon CHECK bloque tout).
   - **Table TVA** : saisir les **règles du seed** `mapping-tva.json` (10 règles : `5`→S 20 %, `6`→E+VATEX-EU-J,
     `20`→S 20 %, `5.5`→S 5,5 %, exports `EXP_HORSUE`/`EXP_CEE`/`EXP_FR`, + parts **Frais** pour `5` et `6`).
   - **VALIDER la table** (action opérateur) → lève **PIP01**, autorise l'envoi réel.
   - **Mentions de facturation B2B** (sous le formulaire fiscal) : saisir les 4 zones (termes de paiement/BT-20, pénalités,
     indemnité 40 €, escompte). → *teste **BUG-26a** (§B).* Laisser **vide d'abord** si tu veux prouver le blocage BUG-26b.
3. **Paramétrage › Plateforme Agréée** → compte **SuperPdp** env **Staging/Sandbox** + `accountId`/`clientId`/`clientSecret`.
   Vérifier capacités affichées : e-reporting B2C ✅, e-invoicing B2B ✅.
4. **Publier le SIREN** (onboarding). → *au passage, teste **BUG-13** (§B) : « Type d'opération »/« Taille d'entreprise » NON requis pour SuperPDP.*
5. **Enrôler un agent** → noter la **clé API**.
6. **Agent** : chiffrer ODBC + clé API (`Liakont.Agent.Cli.exe encrypt` sur le poste), coller dans
   `deployments\encheres-demo\agent\volontaire\agent.json` (vérifier `dossier="2"`, `schema="enc"`, période d'extraction LARGE 2002→2024), installer/lancer.
7. **Planifier les jobs B4** (opérateur **plateforme**, sans société courante) via **Administration › Jobs**.
   → *teste **BUG-4b** (§I).* Cron court (`*/2 * * * *`) sur : `AggregateB2cMarginAll`, `AggregateB2cTaxableAll`,
   `AggregateB2cExportAll`, `AggregateB2cPlainTaxableAll`, `SendAll`, `SyncAll`.

Puis répéter §1 pour le tenant **`judiciaire`** / dossier 1 (B2B criée — pour les cas Factur-X §H et BUG-28).

---

## 2. Recette des correctifs — par écran (39 points)

Légende statut : ✅ résolu · 🟡 partiel · ❓ à confirmer. **Repère** = n° de doc source à ouvrir.

### A. Provisioning / création client
| ID | Attendu | Où |
|---|---|---|
| **BUG-14** ✅ | L'identité saisie à la main (SIREN, raison sociale) est conservée ; le paramétrage (fiscal/mapping/seuils) vient bien du seed, mais **jamais** l'identité fictive du seed | `/clients/nouveau` → `/parametrage` (carte Profil) |
| **BUG-15** ✅ | Profil légal **éditable** (raison sociale/adresse/contact) via « Modifier le profil légal » ; **SIREN grisé** (immuable) ; enregistre sans recréer le tenant | `/parametrage/profil` |

### B. Paramétrage fiscal & table TVA
| ID | Attendu | Où |
|---|---|---|
| **BUG-12** ✅ | À la création d'une règle, le select « Composante » ne propose **que 2** choix (« Frais » / « Adjudication et factures ») — plus d'« Adjudication » morte ; libellés + aide clairs ; en modif, composante figée rendue en **texte** | `/parametrage/table-tva` (création + modif de règle) |
| **BUG-3** ✅ | Le contrôle de cohérence ne marque **plus** les règles **Frais** comme « mortes » (B4 les consulte) ; seule une règle « Adjudication » reste signalée, avec message actionnable (où mapper), pas « supprimez » | `/parametrage/table-tva` (bloc Contrôle de cohérence) |
| **BUG-13** ✅ | Sur SuperPDP : « Type d'opération » / « Taille d'entreprise » **non requis** (pas d'astérisque), bouton Publier actif champs vides | `/parametrage/comptes-pa` (Publication SIREN) |
| **BUG-26a** ✅ | Les mentions de facturation vivent **dans** Paramètres fiscaux (bouton Enregistrer propre) ; `/parametrage` = **9 cartes** (pas 10) ; `/parametrage/mentions` n'existe plus | `/parametrage/fiscal` |

### C. Préférences / shell
| ID | Attendu | Où |
|---|---|---|
| **BUG-16** ✅ | Nouvel utilisateur (localStorage vide, fenêtre privée) → densité **Standard** dès le 1er rendu ; ne retombe **plus** en Compact au fil de la nav | shell + `/settings/preferences` |
| **RBF08** ✅ | Régler thème sombre + densité, se reconnecter depuis **un navigateur neuf** → préférences ré-appliquées **dès le login** (lues en base), sans ouvrir le panneau | shell (hydratation login) |

### D. Agent / extraction  *(après un run de l'agent volontaire)*
| ID | Attendu | Où · Repère |
|---|---|---|
| **BUG-1** ✅ | « Dernier contact » **avance** à chaque cycle `heartbeatMinutes`, même **sans run** d'extraction (avant : figé à l'install) | `/flotte` |
| **BUG-2** ✅ | Désinstaller l'agent **purge** `agent-queue.db*` ; la réinstallation **repart vierge** (pas l'ancien watermark) ; base source jamais touchée | agent `uninstall` · `%ProgramData%\Liakont\...` |
| **BUG-10-EXTRACTION** ✅ | Adjudications réellement régime 6 qui vivent sur le PV (`ligne_pv.no_ba=0`) sortent **avec** leur code régime (jointure `no_ligne_tout_pv`) — plus de faux « 0 code régime » (~170 bordereaux) | `/documents/{id}` · **100264** (lots 29/30), **2000026** (lot 22 → régime 6) |
| **BUG-9** ✅ | Acheteur pays « ENG » → normalisé **GB**, plus bloqué au CHECK | `/documents/{id}` · **100264** |
| **BUG-18** ✅ | Acheteur pays « JAP » → normalisé **JP**, plus bloqué ; un code vraiment inconnu (XYZ) reste bloqué (fail-closed) | `/documents/{id}` · **2000020** |

### E. Liste & détail document (observabilité)
| ID | Attendu | Où · Repère |
|---|---|---|
| **BUG-5** ✅ | Le **détail des lignes** (catégorie/VATEX/taux + cohérence totaux↔lignes) s'affiche **dès le CHECK**, y compris sur un doc **Bloqué** (avant : placeholder « une fois transmis ») | `/documents/{id}` onglet Contenu · un doc **Bloqué** |
| **BUG-19** ✅ | Barre **‹ Précédent · n/N · Suivant ›** en détail : ouvre la fiche suivante **de la liste filtrée** sans repasser par la grille ; transverse (idem depuis les émissions) | `/documents/{id}` et `/emissions-marge-b2c/{id}` |
| **BUG-20** ✅ | Colonne + en-tête **« Famille de pièce »** : Bordereau acheteur / vendeur / Facture client / Note d'honoraires (BA ≠ BV distincts) ; préfixe inconnu → « — » | `/documents` (colonne) + détail |
| **BUG-28** ✅ | En-tête **« SIREN émetteur »** affiche le SIREN **réellement transmis** (avant : « non renseigné » à tort) ; « non renseigné » seulement si vraiment non résolu | `/documents/{id}` · **9000004** (judiciaire, transmis) |
| **BUG-10** ✅ | Message de blocage « aucun code régime » nomme la **vraie cause** + action « corrigez la donnée source » (plus de « scission BG-30 / évolution ultérieure ») | `/documents/{id}` · une ligne réellement sans régime |
| **TVA-marge-ligne** ✅ | Ligne d'adjudication marge affiche **« Régime de la marge – <nature> »** (biens d'occasion/œuvres/collection) au lieu du sec « E — Exonéré » | `/documents/{id}` Contenu · **100152** |
| **TVA-marge-recap** ✅ | Carte **« Récapitulatif de marge »** : Marge TTC, Base HT, TVA sur marge (+ taux) + intro art. 297 E ; absente hors marge | `/documents/{id}` · **100152** |
| **TVA-marge-note297E** ✅ | L'explication art. 297 E n'apparaît **qu'une fois** (intro du récap) — plus de doublon sous le tableau | `/documents/{id}` · **100152** |

### F. Aiguillage & couverture des flux e-reporting B2C
| ID | Attendu | Où · Repère |
|---|---|---|
| **BUG-8** ✅ | Menu unifié **« Émissions e-reporting B2C »** (plus « marge B2C ») ; la page liste **marge (régime 6) ET prix total taxable (régime 5)** | menu → `/emissions-marge-b2c` · PV 85 (rég.6) + un rég.5 |
| **BUG-17** ✅ | Acheteur **à SIREN** sur une marge : plus bloqué « marge non classée » → **routé e-invoicing B2B** (adjudication + honoraire acheteur en lignes) ; assujetti UE sans SIREN → fail-close « facture B2B requise » | `/documents/{id}` · **2000026** (SIREN 998877666) ; **100351** (TVA BE) |
| **flux#7** ✅ | Factures clients + notes d'honoraires **ingérées** (clé = taux effectif) et **e-reportées TPS1** (nature PrestationServices) | `/documents` + `/emissions-marge-b2c` |
| **BUG-11** 🟡 | Export routé : hors UE / franchise → **TLB1**, intracom → **TNT1**, e-reporting **unitaire** taux 0 | `/documents/{id}` + `/emissions-marge-b2c` · un bordereau export. ⚠️ **partiel** : l'intracom **au prix total** (262 ter / VIES / 289 B) reste signalé, **non traité** — ne pas cocher « résolu » dessus |

### G. Envoi & émissions — **SuperPDP sandbox réel**
| ID | Attendu | Où |
|---|---|---|
| **RBF07-A** ✅ | Après « Tout envoyer » / « Lancer un traitement » : grille **+ compteurs** se rafraîchissent **seuls** (sans F5) | `/documents` |
| **RBF07-B** ✅ | Run **tout-différé** (contenu pas encore stagé) → bandeau **INFO** « N en cours d'émission » (plus de faux-rouge « aucun document émis ») ; les **HOLD** (SIREN non publié / TVA non validée) restent **signalés** avec action corrective | `/documents` + Journal des traitements |
| **BUG-22** ✅ | Clic sur une ligne d'émission → **fiche détail** : synthèse transmission + **motif PA lisible** (codes+messages, jamais de JSON) + **pièces** de l'agrégat (liens docs) | `/emissions-marge-b2c/{id}` |
| **BUG-24** ✅ | Doc composant un agrégat **Issued** → badge **« E-reporté »** (vert) + lien « Voir la déclaration » ; bouton **« Envoyer » masqué** (avant : « À envoyer » figé + Envoyer sans effet) | `/documents/{id}` · marge Issued (sandbox id 585) |
| **BUG-27** ✅ | Doc **RejectedByPa** : motif PA (codes FR ex. [BR-FR-05], [BR-CO-25], message HTTP) affiché **dans l'onglet Contrôles ET Historique** (avant : renvoyait à un Historique vide) | `/documents/{id}` onglets Contrôles/Historique |

### H. Factur-X B2B (SuperPDP réel — tenant `judiciaire` idéal)
| ID | Attendu | Repère |
|---|---|---|
| **BUG-26f / BUG-23** ✅❓ | SIREN de test sandbox **000000001 / 000000002** acceptés **par dérogation gâtée par l'environnement** (Sandbox actif & aucun Production) ; en **Production** (ou sans compte actif) → re-bloqués Luhn strict | **9000004** (Tricatel 000000001). *BUG-23 = statut ❓ à confirmer (couvre acheteur + émetteur 389)* |
| **BUG-26b** ✅ | Mentions **vides** + facture B2B FR → **BLOQUÉE au CHECK** (pas d'envoi voué au rejet) avec message FR listant les mentions manquantes + action corrective | **9000004** / **9000003** |
| **BUG-26c** ✅ | Mentions **renseignées** → 9000004 passe `/convert` SuperPDP **sans** erreur (BR-CO-25, BR-FR-05, PEPPOL R008 levés) → **Émis** ; le rejeu du mapping (`Rebuild`) **conserve** les champs additifs (mentions/delivery) | **9000004** |
| **BUG-26d** ✅ | Onglet Contenu : section **« Mentions de facturation »** (BT-20 + 3 notes FR libellées) ; doc sans mention → message vide (avant : rien / faussement vide à cause du Rebuild) | **9000004** |
| **BUG-26e** ✅ | Doc **RejectedByPa** : bouton **« Re-vérifier et remettre en envoi »** ; cause corrigée → ReadyToSend ; cause **non** corrigée → **Blocked** (motif réévalué, jamais renvoyé aveuglément) ; audit append-only | un doc RejectedByPa |

> Limite sandbox connue (PAS un bug) : **9000003** (SIREN réel 967890120) passe le SIREN mais SuperPDP le rejette
> « receiver address does not exist in peppol directory » (le sandbox ne connaît que 000000001). À confirmer en PDP/PPF réel.

### I. Jobs / planifications
| ID | Attendu | Où |
|---|---|---|
| **BUG-4b** ✅ | Opérateur **plateforme** (sans société courante) **voit et planifie** les jobs **système** (fan-out tous tenants) — plus de liste vide / « Aucune société sélectionnée » ; opérateur tenant garde son périmètre (non-régression) | `/admin/jobs`, `/admin/jobs/new` |
| **BUG-25** ✅ | « Prochaine exécution » au **fuseau du navigateur** comme « Dernière exécution » (plus de suffixe UTC incohérent ; prochaine jamais < dernière pour un job qui vient de tourner) | `/admin/jobs` |

### J. TVA / Déclaration
| ID | Attendu | Où |
|---|---|---|
| **TVA-declaration** ✅ | Menu **« TVA / Déclaration »** + page : TVA sur marge **agrégée mois × devise × taux** (base HT + TVA sur marge) + récap total, alimentée au CHECK ; un doc repassé non-marge disparaît du registre | `/tva-declaration` |

---

## 3. Points d'attention (à ne pas tester comme « résolu »)
- **BUG-11** 🟡 : intracom **au prix total** (262 ter / VIES / état 289 B) reste **non traité** — signalé seulement.
- **BUG-23** ❓ : dérogation SIREN sandbox gâtée par l'env — à confirmer sur les DEUX règles (acheteur `BuyerIdentityRule` + émetteur 389 `PartyRoleConsistencyRule`).
- **Anti-doublon** : SuperPDP ne dédoublonne pas côté serveur (2 POST = 2 lignes) ; l'anti-doublon est **produit** (Pending écrit AVANT le POST). Relancer un job ne doit produire **aucun** nouveau POST (idempotence).
- **Bruit sandbox** : commencer **volontaire, période courte** ciblant 100152 / 100022 / 9000004, observer, puis élargir.

## 4. Ordre de déroulé conseillé (session à deux)
1. **Bring-up** §0 (1→4).  2. **Provisioning volontaire** §1 (teste A, B, C au fil).  3. **Run agent** → §D + §E.
4. **Planifier + lancer B4** → §F + §G + §J.  5. **Tenant judiciaire** → §H (Factur-X B2B) + BUG-28.  6. **§I** (jobs) en transverse.
