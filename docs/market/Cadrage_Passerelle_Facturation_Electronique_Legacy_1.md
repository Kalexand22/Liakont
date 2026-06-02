# Passerelle de facturation électronique pour logiciels legacy en France
### Document de cadrage produit, marché et stratégie

*Consolidation et correction de trois analyses indépendantes. Sources primaires privilégiées (DGFiP, economie.gouv.fr, AFNOR, impots.gouv.fr). État du droit : loi de finances 2026.*

---

## 1. Synthèse exécutive

**Le marché existe, la fenêtre est ouverte mais bornée dans le temps.** La réforme rend la facturation électronique obligatoire selon un calendrier gravé dans la loi (réception pour toutes les entreprises et émission GE/ETI au 1ᵉʳ septembre 2026 ; émission PME/TPE/micro au 1ᵉʳ septembre 2027). Le recentrage du Portail Public de Facturation (PPF) supprime toute voie publique gratuite de dépôt : chaque entreprise doit désormais contracter avec une **Plateforme Agréée (PA)** privée. C'est le fait générateur du marché.

**L'angle mort réel n'est pas le routage (commoditisé), c'est l'extraction depuis les systèmes non-API.** Les PA et les ERP modernes présupposent un système source capable de produire un flux structuré ou d'exposer une API. Or une fraction substantielle du tissu PME/ETI tourne sur des socles incapables de cela : AS400/IBM i, vieux SQL Server, Access, anciens Navision/Sage, progiciels métiers propriétaires. C'est précisément la pièce que personne ne veut fournir en standard.

**Verdict : oui, marché attaquable pour une petite structure — à condition de viser la niche verticale et le récurrent, pas le généraliste.** Ne pas devenir PA (coûts ISO 27001 / SecNumCloud / audit DGFiP prohibitifs et marché déjà dense). Se positionner en **Solution Compatible (SC)** adossée à une **API de PA en marque blanche**, concentrer son ingénierie sur l'extraction locale, facturer setup + abonnement + volume, et distribuer via ESN et experts-comptables.

---

## 2. Cadre réglementaire (état du droit 2026)

### 2.1 Base légale
- Ordonnance n° 2021-1190 du 15 septembre 2021 (fondement de la généralisation).
- Loi de finances 2024 (n° 2023-1322, art. 91) : fixation du calendrier actuel.
- Loi de finances 2026 (n° 2026-103 du 19 février 2026, art. 123) : confirmation du recentrage du PPF et **durcissement des sanctions**.
- Code général des impôts : art. 289 bis, 290 et 290 A (champ) ; art. 1737-III et 1788 D (sanctions).

### 2.2 Deux obligations distinctes
- **e-invoicing** : factures B2B domestiques entre assujettis, en format structuré, transmises **via une PA** (jamais en direct d'entreprise à entreprise).
- **e-reporting** : transmission à l'administration des données de transactions B2C, des opérations internationales/intracommunautaires, **et des données de paiement**.

### 2.3 Calendrier
| Échéance | Obligation |
|---|---|
| **1ᵉʳ sept. 2026** | Réception de factures électroniques — **toutes les entreprises**, quelle que soit leur taille |
| **1ᵉʳ sept. 2026** | Émission + e-reporting — **grandes entreprises et ETI** |
| **1ᵉʳ sept. 2027** | Émission + e-reporting — **PME, TPE, micro-entreprises** |

> Une clause de sauvegarde permet à l'administration de reporter par décret l'échéance technique de quelques mois en cas de défaillance systémique avérée, mais le calendrier de principe est confirmé (l'amendement de report a été rejeté par les députés en avril 2025). À ne pas surpondérer dans le discours commercial : l'obligation de **réception au 1ᵉʳ septembre 2026 est universelle et la plus immédiate**.

### 2.4 Rôle du PPF — point à corriger dans les analyses sources
Le PPF **n'est pas abandonné** : il est **recentré** depuis la décision du 15 octobre 2024, confirmée par la LF 2026. Il conserve deux missions régaliennes :
1. **Annuaire central** associant chaque SIREN/SIRET à sa plateforme de réception (accès API critique pour le routage) ;
2. **Concentrateur** des données fiscales et de paiement remontées par les PA.

Ce qui disparaît, c'est sa fonction de **plateforme de dépôt/routage gratuite**. Conséquence de conception : **la passerelle ne se connecte jamais au PPF** ; elle injecte la donnée dans une PA, qui assume le routage et la déclaration fiscale.

### 2.5 Plateformes Agréées (PA)
- Terminologie officielle : **PA** remplace *PDP* (depuis juillet 2025) ; **SC** (Solution Compatible) remplace *OD*.
- **≈134 PA immatriculées** par la DGFiP (liste officielle impots.gouv.fr, mise à jour du 22 mai 2026 ; première liste de 101 PA au 16 janvier 2026, enrichie chaque semaine).
- Acteurs majeurs : Pennylane, Cegid, Sage, Generix, Esker, Docaposte/SERES, Yooz, Tenor, Docoon, B2Brouter, Seqino, Iopole.

### 2.6 Formats et normes
- Socle sémantique : **EN 16931**.
- Formats acceptés : **Factur-X** (PDF/A-3 + XML CII, hybride), **UBL 2.1**, **UN/CEFACT CII (D22B)**.
- Profils Factur-X : Minimum, Basic WL, Basic, **EN 16931** (profil de référence pour la piste d'audit fiable), **Extended** (gestion des sous-lignes). Pour le marché français, viser **EN16931** et **EXTENDED-CTC-FR**.
- **Normes AFNOR — répartition de la charge entre la PA et la passerelle.** En s'adossant à une PA en marque blanche, on délègue le transport et l'interopérabilité ; mais la justesse du contenu reste chez la passerelle. Concrètement :
  - **XP Z12-012** (formats et profils) et **XP Z12-013** (API d'interface SI ↔ PA) : **absorbées par la PA**. La passerelle code contre l'API propriétaire de la PA, pas contre la norme brute — c'est précisément ce qu'on lui achète.
  - **Statuts du cycle de vie / messages UN/CEFACT CDAR** : **absorbés par la PA**. Elle renvoie les statuts dans son propre format (souvent JSON via webhook) ; la passerelle se contente de les lire et de les afficher, sans fabriquer de CDAR.
  - **XP Z12-014** (cas d'usage B2B — 44 cas, version 1.3 du 26 février 2026) : **reste à la charge de la passerelle**. Ce n'est pas une affaire de transport mais de **données métier** : acomptes, autofacturation, TVA sur marge, avoirs, factures mixtes biens/services changent la structure même de la facture. La PA valide ou rejette la *forme*, mais ne reconstruit pas un cas d'usage correct à partir d'une donnée mal qualifiée en base. Une TVA sur marge mappée comme une TVA classique sera techniquement acceptée par la PA, mais **fiscalement fausse** — et c'est le client qui prend l'amende.
- **Frontière à retenir** : la PA porte le routage, l'interopérabilité, l'archivage et les statuts. La passerelle reste responsable de produire un format conforme **EN 16931** (Factur-X / UBL / CII) en entrée de la PA, et surtout du **mapping correct des cas d'usage métier (XP Z12-014)**. La justesse fiscale des données est la valeur ajoutée — et la responsabilité — de la passerelle.

### 2.7 Mentions obligatoires
La réforme **ajoute** aux mentions historiques : SIREN émetteur et destinataire, n° TVA intracommunautaire, **catégorie de l'opération** (Livraison de biens / Prestation de services / mixte), option de paiement de la TVA sur les débits le cas échéant, adresse de livraison si différente. La qualification LB/PS/mixte n'est pas cosmétique : elle conditionne l'exigibilité de la TVA et le e-reporting de paiement — or beaucoup d'ERP anciens n'ont **aucune taxonomie interne** pour distinguer un bien d'un service, ce qui impose une table de correspondance au paramétrage.

### 2.8 Sanctions — chiffres à jour (LF 2026)
> ⚠️ Correction majeure : l'une des analyses sources cite encore **15 €/facture** (montant périmé de l'art. 1737 CGI). Le bon chiffre est **50 €**.

- **50 € par facture** non émise au format électronique conforme.
- **500 € par transmission e-reporting** manquante.
- **Plafond 15 000 € / an** par entreprise.
- **Droit à l'erreur** pour la première infraction régularisée spontanément ou sous 30 jours.
- Risque commercial ultime : rejet du **droit à déduction de la TVA** de l'acheteur en l'absence de facture conforme.

---

## 3. Marché adressable

### 3.1 Périmètre théorique
- **≈10 millions** d'acteurs assujettis à la TVA (DGFiP, communiqué du 16 janvier 2026).
- **>4 millions** d'entreprises directement concernées par les flux B2B (ministère de l'Économie).
- Volume annuel estimé : **2 à 3 milliards** de factures électroniques attendues, et plus de 100 milliards de données fiscales.

### 3.2 Périmètre réellement adressable (à ne pas confondre)
Le marché de la passerelle **n'est pas** les 4 M d'entreprises B2B, mais la **fraction sur socle non-API** — de l'ordre de **plusieurs dizaines de milliers d'entreprises** en France. C'est largement suffisant pour une micro-structure, mais le discours doit rester honnête : la cible n'est pas « des millions de clients ».

Indicateur clé de tension : selon l'observatoire OpinionWay/Quadient (mars 2026), **86 % des entreprises comptent se mettre en conformité via leur ERP existant, mais seules 19 % utilisent déjà un format conforme** (UBL, CII, Factur-X). Cet écart **est** le marché.

### 3.3 Cartographie des socles legacy
1. **IBM AS400 / IBM i** : parcs importants en France (distribution, santé, transport, logistique). Extraction via fichiers spool ou requêtes DB2 — ingénierie spécialisée, compétences rares.
2. **ERP commerciaux on-premise en fin de vie** : anciens Navision (pré-Business Central), Sage 30/100 I7, bases SQL Express vieillissantes.
3. **Logiciels verticaux « maison »** : développés dans les années 1990-2000 sur SQL Server 2000/2005, Oracle, Access, FileMaker (gestion de garages, pharmacies, coopératives agricoles, imprimeries, menuiseries, etc.). Parfaitement adaptés au métier, jamais mis à jour.

### 3.4 Secteurs prioritaires
Industrie / agroalimentaire, BTP / construction, négoce et distribution (AS400 fréquents), transport, santé / laboratoires. Le BTP et l'agriculture figurent parmi les moins numérisés.

---

## 4. Conformité minimale (ce que le client doit techniquement faire)

1. Extraire les données de facture du système source.
2. Générer un format structuré conforme EN 16931, avec toutes les mentions obligatoires.
3. Transmettre via une PA immatriculée.
4. Gérer les statuts du cycle de vie (déposée / reçue / acceptée / refusée / litige / encaissée).
5. Être inscrit à l'annuaire (démarche opérée par la PA).
6. Assurer le e-reporting des flux B2C / international / paiement si concerné.
7. **Être capable de recevoir** les factures fournisseurs structurées dès le 1ᵉʳ sept. 2026.

---

## 5. Définition produit — MVP

Positionnement réglementaire : **Solution Compatible (SC)**, non agréée, déléguant le routage final à une PA partenaire. Principe d'architecture directeur : **noyau générique (≈90 %) + connecteur spécifique par socle (≈10 %)**. Si chaque client impose de réécrire la logique de transformation ou de routage, le modèle économique s'effondre sous la maintenance.

### 5.1 Fonctionnalités indispensables (must-haves)
1. **Moteur de connexion de données** non intrusif : pilotes ODBC/JDBC vers SQL Server, Oracle, DB2 (AS400), MySQL, PostgreSQL, Access. Lecture **asynchrone planifiée** (scrutation périodique des tables de factures validées).
2. **Modèle pivot interne + outil de mapping** : dictionnaire de données aligné sur EN 16931, interface de correspondance colonnes source → champs canoniques. C'est l'abstraction qui rend le produit réplicable.
3. **Module de validation et de contrôle qualité** : SIREN/SIRET valides et non clos, n° TVA intracommunautaire, codes pays ISO 3166-1 alpha-2, cohérence HT/TVA/TTC, présence des mentions obligatoires. Génération d'alertes pour correction **à la source** (une facture à SIRET invalide est rejetée par la PA).
4. **Générateur de formats structurés** : production Factur-X / UBL / CII (profils EN16931 et EXTENDED-CTC-FR), encapsulation XML dans le PDF/A-3.
5. **Connecteur de dépôt vers UNE PA** : pousser le fichier conforme via l'API de la PA partenaire (qui porte le routage, l'interopérabilité et les statuts), puis **rapatrier les statuts** renvoyés par la PA et les réinscrire si possible dans le SI source. Le connecteur ne fabrique ni le routage ni les messages de cycle de vie.
6. **Interface de supervision minimale** : volumes traités, journaux, documents bloqués, état des e-reportings.

### 5.2 Fonctionnalités à exclure au départ (anti-scope-creep)
- Devenir PA (ISO 27001 / SecNumCloud / audit DGFiP).
- OCR / IA d'extraction de factures papier.
- Workflows d'approbation Procure-to-Pay.
- Initiation de paiements SEPA (contraintes DSP2).
- Archivage à valeur probante 10 ans (NF Z42-013) — **à déléguer à la PA**.
- Multi-PA dès le départ (un connecteur PA d'abord).
- Les 44 cas d'usage AFNOR (5 à 10 couvrent une PME standard).
- Couverture multi-pays / PEPPOL international.

---

## 6. Architecture technique recommandée

- **Extraction strictement non invasive** : aucune écriture dans la base source. Pour éviter les *table locks* sur bases anciennes non indexées (AS400, SQL Server 2005) en heures de pointe, privilégier : vues SQL en lecture seule, base tampon répliquée (mirroring), exécution nocturne planifiée.
- **Pipeline canonique** : Extraction → Pivot (mapping) → Contrôle → Génération → Routage PA → Statuts. La variabilité client est confinée à la requête d'extraction et au fichier de mapping.
- **e-reporting de paiement — le point dur** : pour les prestations de services, il faut transmettre date et montant d'encaissement par taux de TVA. Or l'ADV (génération de facture) est souvent **cloisonnée** de la comptabilité / trésorerie (lettrage) dans les progiciels anciens. Lier un flux bancaire à une facture historisée exige une interrogation multi-bases ou une heuristique de rapprochement. **C'est là que la valeur ajoutée du connecteur est la plus différenciante.**
- **Piste d'audit fiable** : journaux horodatés démontrant l'absence d'altération du sens fiscal entre la donnée brute et le XML produit.

---

## 7. Concurrence et positionnement

| Famille | Exemples | Pourquoi ce n'est pas un concurrent frontal |
|---|---|---|
| ERP modernes SaaS | Odoo, Cegid XRP, SAP S/4HANA, Sage | Résolvent le problème en **remplaçant** le SI — rejeté par la cible legacy (coût/risque) |
| PA généralistes | Pennylane, Esker, Cegid, Docaposte | Présupposent un flux **déjà formaté** ; n'iront pas écrire des requêtes dans une base de 2005 |
| OD / GED complets | DocuWare, Zeendoc, Open Bee | Lourds à déployer (dizaines de k€), changent les process — l'opposé de la furtivité visée |
| **Intégrateurs EDI avec PA, discours « legacy »** | **Esalink (Hubtimize)**, Tenor (eDemat), TX2, Generix | ⚠️ **Concurrents marketing, pas techniques** : Esalink revendique « la conformité sans tout révolutionner » et cible les « environnements legacy » — MAIS exige que le système source sache **exporter du Factur-X/UBL/CII ou des fichiers structurés** (prérequis publiés sur leur page connexion ERP). Le logiciel qui ne sait rien exporter échoue à leur audit et reste sans solution. Cible grands comptes industriels (Sanofi, Nestlé, Safran). Voir `DR7-Concurrence-Directe-Legacy.md` |
| **API PA marque blanche** | **Seqino, Iopole, B2Brouter, SuperPDP** | **Alliés stratégiques**, pas concurrents : fournissent l'immatriculation et le routage à sous-traiter |

**Stratégie « océan bleu »** : cibler les segments délaissés — logiciels verticaux sectoriels non maintenus, développements bureautiques Access/Excel. Un connecteur « sur étagère » pour 4-5 logiciels verticaux dominants s'amortit en grappe via le bouche-à-oreille au sein d'un même syndicat professionnel.

---

## 8. Modèle économique

Structure **tridimensionnelle** (rémunérer la complexité initiale + sécuriser le récurrent) :

1. **Setup / connecteur (one-shot)** : audit du schéma, scripts d'extraction sur mesure, configuration du mapping, tests d'intégration. Repère marché : **1 500 – 5 000 € HT** (davantage pour AS400 / Oracle ancien). Barrière justifiée par la valeur de la prestation.
2. **Abonnement SaaS récurrent** : accès au dashboard, maintenance évolutive, **veille réglementaire** (mise à jour codes pays, mentions, normes AFNOR). Repère marché : **100 – 300 € / mois HT** (référence Weproc PA Connect ≈ 200 €/mois), engagement minimal 12 mois.
3. **Volume transactionnel (pay-per-use)** : répercussion du coût marginal de la PA. Repère marché dégressif : **≈0,15 € HT/doc** (<10 000/mois) → **0,10 €** → **0,05 €** (>100 000/mois).

**Distribution indirecte (levier d'acquisition) :**
- **ESN / intégrateurs régionaux / MSP** : en marque blanche, l'ESN facture l'audit et l'intégration au tarif fort, l'éditeur perçoit l'abonnement et le volume.
- **Experts-comptables** : prescripteurs naturels, confrontés aux clients « techniquement bloqués » que la passerelle débloque sans remplacer leur outil.

---

## 9. Risques

**Techniques**
- *Table locks* / instabilité sur requêtes lourdes en base ancienne → parade par extraction non invasive (cf. §6).
- Hétérogénéité extrême des sources → discipline du 90/10 sous peine d'explosion de la maintenance.
- Qualité des données source (référentiels tiers incomplets) → contrôles en amont obligatoires.
- Mappings TVA complexes (TVA sur marge, acomptes, autofacturation) → profil EXTENDED-CTC-FR + cas d'usage AFNOR XP Z12-014.
- Dépendance à l'API de la PA partenaire.

**Commerciaux**
- Cycle de vente PME long, faible maturité IT côté client.
- Concurrence des offres PA gratuites/low-cost qui banalisent la perception du prix.
- **Obsolescence programmée du marché** : la passerelle prolonge la vie de socles condamnés à terme (5-10 ans) par le vieillissement matériel et le départ des compétences AS400. Concevoir le plan d'affaires pour un **ROI amorti sur 3-5 ans** et anticiper le churn (migration cliente = résiliation).

**Réglementaires / juridiques**
- La responsabilité fiscale incombe à l'entreprise émettrice, pas à l'éditeur — mais en cas de donnée erronée transmise, l'imputabilité doit être contractuellement bordée : **clauses limitatives de responsabilité** transférant la charge de la qualité de la donnée source au client.
- La passerelle **n'est pas une PA** : elle ne peut ni router en direct ni déclarer ; obligation d'adossement à une PA immatriculée.
- RGPD (données personnelles dans les factures), piste d'audit fiable, statut clair de SC.

---

## 10. Recommandation stratégique et plan d'action

**Le faisceau des trois analyses est convergent et la thèse tient.** Pour une petite structure de développement, l'opportunité est réelle à condition d'attaquer par la niche et de bâtir le récurrent.

### Angle d'attaque
Démarrer par **un couple « secteur vertical + type de base » maîtrisé** (par ex. un applicatif Magic XPA ou un ERP métier sur SQL Server ancien), livrer un premier connecteur de référence, puis répliquer en grappe. Les compétences en systèmes legacy (Magic XPA, .NET) et en normes comptables françaises (FEC, régimes TVA, Axelor) sont directement transposables — notamment pour les contrôles de données et les mappings TVA, qui sont le cœur de valeur.

### Atout différenciant à exploiter
S'adosser dès le départ à une **API de PA en marque blanche** plutôt que de tout construire. Un partenariat avec un acteur comme **B2Brouter** (déjà manipulé dans un contexte client réel) raccourcit le time-to-market et porte le risque réglementaire — c'est exactement le partenaire que ces analyses recommandent de constituer.

### Plan
| Horizon | Action |
|---|---|
| **Immédiat (juin–sept. 2026)** | Choisir une PA partenaire marque blanche (critères : support XP Z12-013, hébergement SecNumCloud, tarif de gros, solidité). Identifier 1-2 verticaux. Construire un POC extraction → pivot → Factur-X → dépôt PA sur un cas réel. |
| **Court terme** | Packager une offre « setup + abonnement » centrée sur la **réception** (échéance universelle de sept. 2026) — déclencheur d'achat le plus immédiat. |
| **2027** | Étendre à l'**émission + e-reporting** PME/TPE (échéance sept. 2027) en réutilisant les connecteurs validés. |
| **Pérennité** | Capitaliser sur le récurrent et la veille réglementaire (ViDA, évolutions AFNOR) ; développer la revente via éditeurs verticaux et experts-comptables. |

### Seuils de décision
- Réutilisabilité d'un connecteur > 10 clients par socle → bascule en posture éditeur ; sinon, rester en prestation/conseil.
- Si les PA intègrent nativement l'extraction legacy → la niche se referme : surveiller.
- Surveiller chaque loi de finances (report ou durcissement).

---

## 11. Sources prioritaires (à vérifier avant tout engagement)

**Officielles**
- Ministère de l'Économie — *Tout savoir sur la facturation électronique* : economie.gouv.fr
- DGFiP / impots.gouv.fr — *Facturation électronique et plateformes agréées* (liste officielle des PA) ; fiches e-reporting de transaction et de paiement (art. 290 A CGI)
- Service-Public Entreprendre — actualités réforme et sanctions
- AFNOR — normes XP Z12-012, XP Z12-013, XP Z12-014 (cas d'usage B2B)
- Loi de finances 2026 (n° 2026-103 du 19 février 2026)

**Marché / pricing (secondaires, indicatives)**
- Weproc PA Connect (étalonnage tarifaire connecteur)
- Comparateurs PA (Qonto, comparateur-facturation-electronique.fr)
- Observatoire OpinionWay / Quadient (mars 2026) — taux de conformité

**Concurrence (analyses vérifiées le 2026-06-02, cf. `DR7-Concurrence-Directe-Legacy.md`)**
- Esalink — « Rester conforme sans tout révolutionner » : https://www.esalink.com/blog/conformite-sans-tout-revolutionner/ — concurrent marketing le plus proche ; exige l'export Factur-X/UBL/CII côté source
- Esalink — connexion ERP (prérequis techniques du système source) : https://www.esalink.com/blog/connexion-erp/
- DevVersPA / SEALOG (passerelle WinDev→PA) : https://devverspa.fr/ — concurrent technique le plus proche, limité à WinDev

---

## 12. Points à vérifier / réserves

- **Nombre de PA** : évolue chaque semaine (≈134 au 22 mai 2026). Vérifier impots.gouv.fr.
- **Périmètres de marché** (≈10 M / >4 M) : ne pas les confondre avec le marché *adressable* (dizaines de milliers sur socle non-API).
- **Nombre total de mentions obligatoires** : la réforme ajoute plusieurs mentions ; le chiffre « 24 » avancé par une des analyses sources n'a pas été confirmé sur source primaire — se référer au cahier des charges DGFiP plutôt qu'à un total chiffré.
- **« 100 000 entreprises AS400 »** : chiffre **mondial**, non français — ne pas l'utiliser pour dimensionner le marché national.
- Les tarifs cités sont des repères de marché et ne préjugent pas du prix soutenable pour une offre de niche à forte valeur d'extraction (potentiellement supérieur).

---

## Annexe — Glossaire

### A. Cadre réglementaire et acteurs

**DGFiP** — Direction Générale des Finances Publiques. L'administration fiscale française, pilote de la réforme.

**Réforme de la facturation électronique** — Obligation faite à toutes les entreprises assujetties à la TVA d'émettre et recevoir leurs factures B2B sous forme de données structurées, via des plateformes agréées, et de transmettre certaines données à l'administration.

**e-invoicing** — Volet « facturation » de la réforme : l'échange de factures B2B domestiques entre assujettis, au format structuré, via une PA.

**e-reporting** — Volet « déclaration » : transmission à l'administration de données de transactions **non** couvertes par l'e-invoicing. Deux sous-catégories :
- *e-reporting de transaction* : ventes B2C, opérations internationales et intracommunautaires.
- *e-reporting de paiement* : pour les prestations de services, date et montant d'encaissement (sert à déterminer l'exigibilité de la TVA).

**B2B / B2C / B2G** — Business-to-Business (entre entreprises), Business-to-Consumer (vers le particulier), Business-to-Government (vers le secteur public, géré via Chorus Pro).

**PPF — Portail Public de Facturation** — Infrastructure publique. Initialement prévue comme plateforme de dépôt gratuite, **recentrée** depuis octobre 2024 sur deux rôles : l'annuaire central et le concentrateur de données. Ne route plus les factures.

**Annuaire central** — Base hébergée par le PPF qui associe chaque SIREN/SIRET à la plateforme de réception choisie par l'entreprise. Indispensable au routage : elle indique « où livrer » une facture.

**Concentrateur** — Fonction du PPF qui collecte auprès des PA les données fiscales et de paiement pour les transmettre à l'administration.

**Routage** — Acheminement d'une facture de la PA de l'émetteur vers la PA du destinataire (via l'annuaire), puis remontée des statuts en sens inverse. Fonction **réservée aux PA**.

**PA — Plateforme Agréée** — Opérateur privé immatriculé par l'État (sécurité, interopérabilité, conformité fiscale auditées) autorisé à émettre, recevoir, router les factures et déclarer à l'administration. Nouvelle appellation officielle de l'ex-**PDP** (Plateforme de Dématérialisation Partenaire).

**SC — Solution Compatible** — Logiciel qui prépare/transforme des factures mais **n'est pas agréé** : il doit s'adosser à une PA pour le routage. Nouvelle appellation de l'ex-**OD** (Opérateur de Dématérialisation). *C'est le statut visé par la passerelle.*

**Cycle de vie de la facture** — Suite de statuts qu'une facture traverse : déposée, reçue, acceptée, refusée, en litige, encaissée. Doivent être tracés et remontés.

**Mentions obligatoires** — Données légales devant figurer dans la facture. La réforme en ajoute : SIREN émetteur/destinataire, n° TVA intracommunautaire, catégorie d'opération, adresse de livraison si différente, etc.

**Catégorie d'opération (LB / PS / mixte)** — Qualification obligatoire de chaque facture : Livraison de Biens, Prestation de Services, ou opération mixte. Détermine les règles d'exigibilité de la TVA.

**Exigibilité de la TVA** — Moment où la TVA devient due à l'État : à la livraison pour les biens, à l'encaissement pour les services (sauf option « TVA sur les débits »).

**Droit à déduction de la TVA** — Possibilité pour l'acheteur de récupérer la TVA payée. Une facture non conforme peut faire perdre ce droit — d'où l'enjeu commercial fort.

**TVA sur marge** — Régime particulier (biens d'occasion, agences de voyage…) où la TVA porte sur la marge et non le prix total. Mapping délicat en facturation électronique (cas d'usage spécifique).

**Assujetti à la TVA** — Critère d'entrée dans la réforme : toute entreprise dans le champ de la TVA, **y compris** micro-entreprises et entreprises en franchise en base.

**GE / ETI / PME / TPE** — Tailles d'entreprise (Grande Entreprise, Entreprise de Taille Intermédiaire, Petite et Moyenne Entreprise, Très Petite Entreprise). Conditionnent le calendrier d'obligation.

**CGI** — Code général des impôts. Articles clés : 289 bis, 290, 290 A (champ) ; 1737-III, 1788 D (sanctions).

**Loi de finances** — Loi budgétaire annuelle. La LF 2024 a fixé le calendrier ; la LF 2026 a durci les sanctions (50 €/facture).

**PAF — Piste d'Audit Fiable** — Obligation de pouvoir retracer, sans rupture, le lien entre une facture et l'opération réelle. Impose des journaux d'événements démontrant qu'aucune donnée fiscale n'a été altérée.

**ViDA — VAT in the Digital Age** — Paquet européen de modernisation de la TVA qui standardisera la facturation électronique au niveau de l'UE dans les prochaines années. À surveiller pour la pérennité du produit.

**Chorus Pro** — Plateforme publique existante pour les factures vers le secteur public (B2G), depuis 2017.

### B. Formats et normes techniques

**EN 16931** — Norme européenne définissant le **modèle sémantique** d'une facture électronique (quelles données, quel sens). Socle commun à tous les formats acceptés.

**Format structuré** — Facture sous forme de données lisibles par machine (et non un simple PDF visuel). Permet l'intégration automatique.

**Factur-X** — Format **hybride** : un PDF/A-3 (lisible par l'humain) contenant un fichier XML de données structurées (lisible par machine). Standard de fait pour les PME.

**UBL — Universal Business Language** — Format XML structuré, répandu à l'international et sur le réseau PEPPOL.

**CII — Cross Industry Invoice** — Format XML structuré du standard UN/CEFACT. C'est le XML embarqué dans Factur-X.

**UN/CEFACT** — Organisme des Nations Unies définissant des standards d'échange de données commerciales.

**PDF/A-3** — Variante du PDF normalisée pour l'archivage, autorisant l'intégration de fichiers en pièce jointe (le XML, dans Factur-X).

**XML** — Langage de balisage structurant des données de façon arborescente. Format sous-jacent d'UBL et CII.

**Profils Factur-X** — Niveaux de richesse croissante : Minimum, Basic WL, Basic, **EN 16931** (profil de référence, complet), **Extended** (gère les sous-lignes). 

**EXTENDED-CTC-FR** — Profil étendu spécifique aux exigences françaises (CTC = Continuous Transaction Controls), nécessaire pour les cas complexes.

**CDAR** — Type de message UN/CEFACT utilisé pour transmettre les **statuts du cycle de vie** d'une facture.

**AFNOR** — Organisme français de normalisation. Publie les spécifications techniques de la réforme.

**XP Z12-012 / XP Z12-013 / XP Z12-014** — Normes AFNOR de la réforme : respectivement les **formats et profils**, l'**API d'interface SI ↔ PA**, et les **cas d'usage B2B** (44 cas — acomptes, autofacturation, TVA sur marge, etc.). À consulter avant de figer le modèle pivot.

**PEPPOL** — Réseau européen d'échange de documents commerciaux électroniques. Pertinent pour les flux internationaux (hors périmètre du MVP).

**SIREN / SIRET** — Identifiants d'entreprise (SIREN = entité légale à 9 chiffres ; SIRET = établissement à 14 chiffres). Doivent être valides et actifs.

**N° TVA intracommunautaire** — Identifiant fiscal européen de l'entreprise (ex. FR + clé + SIREN).

**ISO 3166-1 alpha-2** — Norme des codes pays sur 2 lettres (FR, DE, BE…). Les bases anciennes les stockent souvent dans un format non conforme.

**ISO 27001** — Norme de sécurité de l'information. Exigée pour devenir PA (et donc évitée par la passerelle, qui reste SC).

**SecNumCloud** — Qualification de sécurité (ANSSI) requise pour l'hébergement d'une PA. Critère de choix d'un partenaire PA.

**NF Z42-013** — Norme d'archivage électronique à valeur probante. Relève de la PA, pas de la passerelle.

### C. Technique et intégration

**Legacy** — Système informatique ancien (ERP, base, logiciel métier) toujours en production mais incapable de dialoguer nativement avec les standards modernes. Cœur de cible du produit.

**ERP** — Progiciel de Gestion Intégré (Enterprise Resource Planning) : logiciel central gérant ventes, achats, stocks, compta…

**Middleware / passerelle / connecteur** — Couche logicielle intermédiaire qui relie deux systèmes hétérogènes. Ici : entre la base legacy et la PA.

**API / REST / Webhook** — Interface de programmation permettant à deux logiciels de communiquer. REST est un style d'API courant ; un Webhook est une notification automatique envoyée lors d'un événement (ex. changement de statut d'une facture).

**ODBC / JDBC** — Pilotes standardisés d'accès aux bases de données (ODBC côté Windows/.NET, JDBC côté Java). Permettent de lire une base sans connaître son moteur spécifique.

**SGBD / base relationnelle** — Système de Gestion de Base de Données organisant les données en tables liées (SQL Server, Oracle, MySQL, PostgreSQL, DB2…).

**Vue SQL (view)** — Requête enregistrée présentée comme une table virtuelle, en lecture seule. Moyen recommandé d'extraire sans perturber la base source.

**DB2** — SGBD d'IBM, moteur natif des environnements AS400 / IBM i.

**AS400 / IBM i** — Plateforme matérielle et système IBM (fin des années 1980), réputée robuste, très présente en distribution, santé, logistique. Données souvent extraites via requêtes DB2 ou fichiers spool.

**Spool** — Fichier d'impression généré par les systèmes AS400. Source de données fréquente quand aucune API n'existe.

**Access / FileMaker** — Outils bureautiques de base de données, souvent détournés pour gérer devis et factures dans les TPE.

**Navision / Business Central** — Ancien et nouveau nom de l'ERP Microsoft Dynamics. Les versions Navision on-premise anciennes sont une cible typique.

**Table lock (verrouillage de table)** — Blocage temporaire d'une table pendant une requête lourde, qui peut paralyser les utilisateurs. Risque à éviter par une extraction non invasive.

**Mirroring / réplication** — Copie automatique des données vers une base secondaire, sur laquelle on peut extraire sans toucher à la production.

**Base tampon** — Base intermédiaire dédiée à l'extraction, alimentée par réplication.

**Extraction asynchrone / CRON** — Lecture des données à intervalles planifiés (ex. la nuit via une tâche CRON), plutôt qu'en temps réel, pour limiter l'impact sur la production.

**Modèle pivot** — Modèle de données interne et neutre, aligné sur EN 16931, vers lequel on convertit toutes les données source avant de générer les formats finaux. Cœur de la réplicabilité du produit.

**Mapping** — Mise en correspondance des champs de la base client (« CLI_NAME ») avec les champs du modèle pivot (« nom_acheteur »). Paramétrage spécifique à chaque socle.

**ADV — Administration des Ventes** — Module/processus gérant les commandes et la facturation client. Souvent cloisonné de la comptabilité dans les vieux progiciels.

**Lettrage** — Rapprochement comptable entre une facture et son paiement. Nécessaire au e-reporting de paiement, et techniquement difficile à reconstituer en legacy.

**SaaS / on-premise / cloud-natif** — Modes de déploiement : SaaS = logiciel hébergé en ligne par abonnement ; on-premise = installé sur les serveurs du client ; cloud-natif = conçu d'emblée pour le cloud.

**DSP2** — Directive européenne sur les services de paiement, imposant des contraintes fortes. Raison pour laquelle l'initiation de virements est exclue du MVP.

### D. Économie et stratégie commerciale

**MVP — Minimum Viable Product** — Version minimale du produit couvrant juste ce qu'il faut pour être utile et conforme, sans fonctionnalités superflues.

**Setup fees (frais d'implémentation)** — Facturation initiale unique rémunérant l'audit de la base, l'écriture des scripts d'extraction et le paramétrage du mapping.

**ARR / récurrent** — Annual Recurring Revenue : revenu d'abonnement répété, socle de pérennité d'un éditeur logiciel.

**Pay-per-use** — Facturation à l'usage, ici au volume de factures traitées.

**Marque blanche / marque grise** — Revente d'une technologie tierce sous sa propre marque. En marque blanche, le partenaire (ESN) présente la solution comme la sienne ; en marque grise, la marque de l'éditeur reste partiellement visible. Ici : s'adosser à l'API d'une PA pour le routage.

**Churn** — Taux d'attrition : proportion de clients qui résilient. Risque clé ici, car la migration d'un client vers un ERP moderne supprime le besoin.

**TCO — Total Cost of Ownership** — Coût total de possession d'un système sur sa durée de vie (licences, matériel, intégration, maintenance). Argument du « pourquoi ne pas remplacer l'ERP ».

**Go-to-market** — Stratégie de mise sur le marché : qui on vise, par quel canal, avec quel discours.

**ESN** — Entreprise de Services du Numérique (ex-SSII). Prestataires d'infogérance et d'intégration locaux : canal de distribution privilégié.

**MSP — Managed Service Provider** — Prestataire de services informatiques gérés (infogérance). Même rôle de distributeur que l'ESN.

**Océan bleu** — Stratégie consistant à viser un segment peu disputé plutôt qu'à affronter la concurrence sur un marché saturé.

**Scope creep** — Dérive du périmètre : ajout progressif de fonctionnalités non prévues qui alourdit le produit. À proscrire pour un MVP.

**Procure-to-Pay** — Chaîne complète achat → paiement, incluant les workflows d'approbation. Hors périmètre d'une passerelle.
