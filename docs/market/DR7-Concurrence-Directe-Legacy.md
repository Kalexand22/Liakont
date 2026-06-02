# DR7 — Concurrence directe sur le créneau « connecteur legacy / Solution Compatible »

> Deep research exécutée le 2026-06-02 (102 agents, 19 sources, 77 claims extraits, 25 vérifiés).
> Plan parent : `Plan-DR-Marche-Commercial.md`.
> ⚠️ Méthode : la vérification adversariale a subi des défaillances techniques (votes 0-0 = abstention, PAS réfutation) — même problème que DR1-DR6. Les sections sont classées par niveau de fiabilité.

---

## Verdict synthétique

**Le créneau « extraction directe depuis bases legacy non-API » est très peu saturé.** En juin 2026, le marché français de la conformité e-invoicing repose presque exclusivement sur un modèle d'intégration par **API, SFTP ou export de fichiers structurés** — tous les acteurs présupposent que le système source sait exporter ou exposer un endpoint. Personne (parmi les acteurs vérifiés) ne propose comme produit l'extraction directe ODBC/JDBC depuis des bases legacy incapables d'exporter.

**C'est exactement l'angle de la Passerelle.** La conclusion conforte le positionnement produit : la valeur n'est pas le routage (commoditisé) ni même le format (les PA s'en chargent), mais bien la capacité à aller chercher la donnée là où aucun export n'existe.

**Nuance importante** : cette conclusion est une inférence par absence de preuve (confidence medium), pas une preuve positive de marché vide. Des micro-acteurs non indexés peuvent exister — et un concurrent potentiel direct a été repéré sans pouvoir être confirmé : **DevVersPA / SEALOG** (voir §3).

---

## 1. Constats CONFIRMÉS (vérification adversariale passée)

| # | Constat | Vote | Sources |
|---|---|---|---|
| 1 | **La DGFiP ne prescrit aucune méthode de connexion des logiciels legacy non-API.** La réglementation exige uniquement l'interopérabilité PA↔PPF. L'intégration legacy→PA est entièrement laissée au marché. | 3-0 | impots.gouv.fr (liste PA) |
| 2 | **Tenor** positionne son offre e-invoicing comme un **add-on API destiné aux éditeurs** d'ERP/logiciels métier (« Vous éditez ou commercialisez un ERP… ») — pas d'extraction de bases legacy. | 2-1 | tenorsolutions.com |
| 3 | **Tenor opère comme PA et propose la marque grise / marque blanche** (« Tenor assure le rôle PA », portail aux couleurs du client). Pratique également documentée chez seqino et docoon. | 3-0 | tenorsolutions.com, comparateur-facturation-electronique.fr |
| 4 | **TX2** connecte sa PA via **API ou SFTP** — sa liste de connectivité (AS2, OFTP, SFTP, FTPS, HTTP/S, SMTP, SAP RFC, X400, API) ne contient AUCUN mécanisme d'extraction directe de base de données. | 2-1 | tx2.fr |
| 5 | **Generix** est le **seul acteur à revendiquer explicitement le mot « legacy »** (« Send and receive purchase orders and invoices within your ERP or legacy system ») — mais via ETL/transformation, sans connecteur nommé AS400/Access/Windev/Delphi. Ses connecteurs nommés ciblent SAP, Microsoft, Oracle, Sage. | 3-0 | generixgroup.com |
| 6 | **Synthèse** : le créneau de l'extraction directe depuis bases legacy non-API reste très peu saturé ; l'angle libre pour une micro-structure est l'extraction des socles incapables d'exporter. | synthèse (medium) | transversal |

## 2. Implication stratégique pour la Passerelle

1. **Le positionnement est validé** : aucun des acteurs EDI/PA établis ne descend jusqu'à la base de données. Ils s'arrêtent au « donnez-nous un fichier ou une API ».
2. **Les acteurs établis sont des partenaires potentiels, pas des concurrents** : Tenor (marque grise PA), TX2, Generix ciblent les éditeurs et les ETI — exactement le modèle « la Passerelle fabrique le fichier/appel API, la PA route ».
3. **La fenêtre concurrentielle est ouverte mais l'inférence est fragile** : refaire un point de veille avant tout investissement lourd (cf. critère d'arrêt du plan : il n'est PAS déclenché — pas de concurrent direct installé identifié).

## 3. Concurrents/acteurs repérés mais NON CONFIRMÉS (abstentions techniques — à vérifier manuellement)

> ⚠️ Ces informations proviennent des sources citées mais n'ont pas pu être re-vérifiées (vote 0-0 = abstention technique). Elles sont plausibles et importantes — **à vérifier manuellement avant toute décision**.

| Acteur | Ce qui a été repéré | Pourquoi c'est important | Source à vérifier |
|---|---|---|---|
| **DevVersPA (SEALOG, Chasseneuil-du-Poitou)** | API/connecteur qui relie les logiciels **WinDev** (PC SOFT/WLangage) aux Plateformes Agréées pour Factur-X | **Concurrent direct potentiel le plus proche** : passerelle de conformité spécifique à une techno legacy (WinDev). Modèle à étudier : techno-spécifique vs notre approche générique | devverspa.fr |
| **Weproc PA Connect** | Connecteur ERP existants (y compris ERP « maison ») → facturation électronique via API REST, **SFTP ou fichiers plats**. Prix repéré : ~200 €/mois HT + 0,15 €/doc dégressif (~27 k€/an pour ~20 500 factures) | Référence de pricing la plus proche de notre modèle. MAIS exige toujours que la source produise un fichier | weproc.com/fr/pa-connect |
| **ESALINK (Hubtimize)** ✅ vérifié 2026-06-02 | Intégrateur EDI/B2B avec sa propre PA « Hubtimize e-Invoicing » ; clients grands comptes (Sanofi, Nestlé, L'Oréal, Safran). **Vérifié manuellement** (article « conformité sans tout révolutionner ») : cible explicitement les « environnements legacy » MAIS via **échanges de fichiers/API/EDI** — le logiciel source doit savoir exporter. Pas d'extraction directe en base. | ⚠️ **Concurrent marketing, pas technique** : ils occupent le discours « sans refonte de votre ERP / couche complémentaire » — quasi identique au nôtre. Différenciation à affûter : « même si votre logiciel ne sait pas exporter » (eux s'arrêtent là, nous commençons là). Cible différente (grands comptes vs PME/ETI verticales). | esalink.com/blog/conformite-sans-tout-revolutionner |

### Fiche Esalink complète (analyse approfondie du 2026-06-02, vérifiée manuellement)

| Attribut | Valeur |
|---|---|
| Société | Esalink, Bezannes (51430, près de Reims) — spécialiste EDI/intégration B2B |
| Produit phare | **Hubtimize®** — plateforme SaaS « tout-en-un » : e-invoicing + EDI + WebEDI + data intelligence, architecture modulaire |
| Statut réglementaire | **PA immatriculée définitivement par la DGFiP** (parmi les premières) |
| Certifications | **SecNumCloud (ANSSI) + ISO 27001**, datacenters France et Allemagne — le niveau de certification le plus élevé du panel concurrentiel |
| Autres offres | Solutions on-premise via partenariats : IBM Sterling, Cleo Integration Cloud, **TradeXpress (Generix)** ; TMA, conseil, régie |
| Secteurs | 8+ industries : logistique, auto/aéro, santé, construction, agroalimentaire, manufacturing, banque, retail |
| Partenaires | Yneia, Sendoc, Ayami, Youseeme, **Zeendoc (GED)** ; affiliations GS1, PEPPOL, AFNOR |
| Pricing | **Non public** — modèle : abonnement + consommation par document + prestations d'intégration |
| **Modes d'intégration ERP** (page connexion-erp) | 1. Connecteur natif ERP↔PA (« le plus rapide si disponible ») ; 2. API REST ; 3. Transfert de fichiers SFTP/AS2 |
| **Prérequis exigés du système source** | ⭐ **« Exporter/importer les données aux formats requis (Factur-X, UBL, CII) », supporter la méthode d'intégration, gérer les cycles automatisés et les retours de statuts** |

### ⭐ La citation qui valide tout le positionnement de la Passerelle

La page « connexion ERP » d'Esalink exige que le système source sache **« exporter les données aux formats requis (Factur-X, UBL, CII) »**. Pour les systèmes anciens, Esalink propose seulement un « audit initial » d'évaluation de compatibilité — **aucune solution prescriptive pour les systèmes qui échouent à cet audit**.

> **Autrement dit : un EncheresV6, un CHEOPS ou un Agimarée échouerait à l'audit Esalink — et Esalink n'a rien à leur proposer ensuite. C'est exactement là que la Passerelle commence : on fabrique le Factur-X/UBL à partir de la base de données brute, le logiciel n'a rien à savoir faire.**

Le « legacy » au sens d'Esalink = un ERP ancien qui sait quand même produire des fichiers (EDIFACT, CSV structuré). Le « legacy » au sens de la Passerelle = un logiciel qui ne sait rien produire du tout. Deux marchés différents qui se ressemblent en surface.
| **Clic Concept** | Article « facturation électronique 2026 WinDev » | Indice d'un écosystème WinDev qui s'organise — secteur à surveiller | clicconcept.com |
| **PC SOFT (éditeur de WinDev)** | Page « connecteurs natifs AS400 » | L'éditeur de WinDev outille ses développeurs pour AS400 — pourrait faciliter l'émergence de concurrents WinDev | pcsoft.fr |

## 4. Questions ouvertes (issues de la recherche)

1. **DevVersPA/SEALOG existe-t-il vraiment comme produit commercialisé**, à quel prix, avec quel succès ? → action de vérification manuelle prioritaire (visite du site, appel éventuel).
2. Quels sont les prix réels de Weproc PA Connect et des connecteurs « universels » des PA ?
3. Les éditeurs legacy eux-mêmes (PC SOFT, éditeurs AS400, Delphi) préparent-ils des modules natifs de conformité ?
4. Quels OD/Solutions Compatibles sont positionnés nominativement sur l'extraction depuis socles anciens ?

## 5. Actions découlant de cette DR

| # | Action | Priorité |
|---|---|---|
| DR7-A1 | **Vérifier manuellement DevVersPA/SEALOG** (devverspa.fr) : produit réel ? prix ? clients ? WinDev only ? | **Haute** |
| DR7-A2 | Vérifier le pricing Weproc PA Connect (weproc.com/fr/pa-connect) comme référence de pricing pour DR9 | Haute |
| DR7-A3 | Considérer Tenor (marque grise PA) comme PA alternative dans DR17 | Moyenne |
| DR7-A4 | Point de veille trimestriel sur le créneau (la fenêtre peut se refermer) | Moyenne |
| DR7-A5 | **Différenciation marketing vs Esalink** : ne pas utiliser « sans refonte » (terrain occupé) ; utiliser « même si votre logiciel ne sait pas exporter » / « votre logiciel n'a rien à faire ». Impacte les pages SEO (DR15) et le pitch. | **Haute** (avant rédaction du site) |

## 6. Données techniques de la recherche

- **Stats** : 6 angles, 19 sources, 77 claims extraits, 25 vérifiés → 5 confirmés, 20 tués (dont ~18 par abstention technique, pas par réfutation réelle).
- **Vraies réfutations** (votes avec majorité de réfutation réelle) : « Tenor ne propose pas de connecteur universel DB » (0-3 — donc Tenor pourrait en proposer un, à creuser), « TX2 connecteurs compatibles tous logiciels » (1-0, non confirmé), « Generix 3000 maps B2B » (1-0, non confirmé).
- **Qualité des sources** : majoritairement pages marketing d'éditeurs (primaires mais auto-déclaratives) — elles décrivent le positionnement affiché, pas l'efficacité réelle.
