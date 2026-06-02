# Plan des deep researches commerciales / marché — Passerelle e-invoicing legacy

> Créé le 2026-06-02. Pendant commercial du `..\Conception\README-Index-Conception.md` (DR1-DR6 = réglementaire/technique).
> Les DR ci-dessous sont numérotées DR7+ pour continuer la séquence existante.
> Document parent : `Conception-Produit-Passerelle.md`.

## Vue d'ensemble et statuts

| DR | Sujet | Phase | Priorité | Dépend de | Statut |
|---|---|---|---|---|---|
| [DR7](#dr7) | Concurrence directe sur le créneau SC / connecteur legacy | 1 — Prérequis | 🔴 Haute | — | ✅ faite → `DR7-Concurrence-Directe-Legacy.md` — niche peu saturée, positionnement validé ; ⚠️ DevVersPA/SEALOG à vérifier |
| [DR8](#dr8) | Dimensionnement du marché adressable non-API | 1 — Prérequis | 🔴 Haute | — | ✅ faite → `DR8-Dimensionnement-Marche-Adressable.md` — bottom-up impossible ; top-down solide (7,3k GE/ETI + 4,2M PME/TPE, 42% sans PA connue) |
| [DR9](#dr9) | Business model / pricing / scénarios CA | 2 — Économie | 🔴 Haute | DR7, DR8 | ✅ faite → `DR9-Business-Model-Pricing-Scenarios-CA.md` — 16/25 confirmés ; ne pas facturer au document ; 3 scénarios CA 200/400/700 k€ cumulés 2026-2029 |
| [DR10](#dr10) | Secteurs propices + éditeurs nominatifs par secteur | 3 — Cibles | 🔴 Haute | (DR8 utile) | ⚠️ partiel → `DR10-Secteurs-Editeurs-Legacy.md` — synthèse finale plantée ; 3pi/CHEOPS ✅, Agimaree/ASAPE/NSI-SADIMO repérés, BTP/garages manquants |
| [DR13](#dr13) | Canaux prescripteurs (experts-comptables, ESN, syndicats, salons) | 4 — Acquisition | 🟠 Moyenne | — | ✅ faite → `DR13-Canaux-Prescripteurs.md` — ⚠️ abstentions techniques ; SYMEV liste ISATECH comme partenaire ; EC = canal n°1 ; FNFE-MPE à rejoindre |
| [DR14](#dr14) | Acquisition YouTube | 4 — Acquisition | 🟠 Moyenne | (DR13 pour comparer) | ✅ faite → `DR14-Acquisition-YouTube.md` — verdict : PAS prioritaire ; email + SEO d'abord ; angle legacy libre mais ROI non démontré |
| [DR15](#dr15) | SEO / site web longue traîne | 4 — Acquisition | 🟠 Moyenne | (DR10 pour les pages secteur) | ✅ faite → `DR15-SEO-Site-Web-Longue-Traine.md` — arborescence + 20 pages ; niche WinDev occupée (DevVersPA) ; site à lancer avant fin 2026 |
| [DR11](#dr11) | Analyse approfondie des meilleurs éditeurs | 3 — Cibles | 🟠 Moyenne | DR10 | ✅ faite (manuellement) → `DR11-Analyse-Editeurs-Cibles.md` — **3pi/CHEOPS = cible n°1** (4 salariés, partenaire SYMEV, 0 conformité) ; Agimarée n°2 ; garages dépriorisés |
| [DR12](#dr12) | Intelligence ciblée ISATECH | 3 — Cibles | 🔴 Haute | — | ✅ faite → `DR12-Intelligence-ISATECH.md` — ⚠️ **redressement judiciaire présumé à vérifier d'urgence** ; parc ~100 études ; rien d'annoncé pour EncheresV6 |
| [DR13](#dr13) | Canaux prescripteurs (experts-comptables, ESN, syndicats, salons) | 4 — Acquisition | 🟠 Moyenne | — | ⬜ à lancer |
| [DR14](#dr14) | Acquisition YouTube | 4 — Acquisition | 🟠 Moyenne | (DR13 pour comparer) | ⬜ à lancer |
| [DR15](#dr15) | SEO / site web longue traîne | 4 — Acquisition | 🟠 Moyenne | (DR10 pour les pages secteur) | ⬜ à lancer |
| [DR16](#dr16) | Offre « réception » comme produit d'appel | 5 — Offre | 🟡 Basse | DR7 | ✅ faite → `DR16-Offre-Reception-Produit-Appel.md` — sanctions réception faibles (500-1000 €) ; produit d'appel léger (500-1500 €), pas de produit autonome |
| [DR17](#dr17) | Stratégie multi-PA / conditions partenaires | 5 — Offre | 🟡 Basse | — | ✅ faite → `DR17-Strategie-Multi-PA-Partenaires.md` — B2Brouter confirmé ; **Iopole = PA de secours** (seule à documenter les flux 10.2/10.4) |

**Ordre de lancement recommandé** : DR7 + DR8 (parallèle) → DR9 → DR10 + DR12 (parallèle) → DR13 + DR14 + DR15 (parallèle, pour comparer les canaux avant d'investir) → DR11 → DR16 + DR17.

**Règle anti-doublon** : chaque DR indique ce qui est DÉJÀ couvert par les documents existants (`Cadrage_Passerelle...`, `L'opportunité e-invoicing...`, `Stratégie Automatisation Prospection...`). Ne pas re-rechercher ces points : les réutiliser comme acquis et les faire seulement re-vérifier si datés.

---

## Phase 1 — Prérequis (conditionnent tout le reste)

### <a name="dr7"></a>DR7 — Concurrence directe sur le créneau « connecteur legacy / Solution Compatible »

**Objectif** : savoir qui fait exactement la même chose que la Passerelle, et si la niche risque de se refermer. Prérequis absolu du pricing (DR9) : on ne fixe pas un prix sans connaître l'alternative réelle du client.

**Question de recherche (prête à lancer)** :
> Quelles solutions existent en France (juin 2026) pour connecter un logiciel legacy non-API (AS400, vieux SQL Server, Access, Windev, Delphi, ERP propriétaires anciens) à la facturation électronique obligatoire, SANS remplacer le logiciel ? Recenser nominativement : (a) les Solutions Compatibles / OD positionnées sur l'extraction depuis bases legacy, (b) les offres type « connecteur universel » des Plateformes Agréées (les PA développent-elles des connecteurs base de données génériques ?), (c) les ESN/freelances/petites structures positionnés sur ce créneau, (d) les offres des éditeurs legacy eux-mêmes (modules de mise en conformité). Pour chaque acteur : prix, périmètre technique, modèle (produit vs prestation), cibles. Conclure sur le degré de saturation du créneau et les angles encore libres.

**Sous-questions clés** :
- Weproc PA Connect et équivalents : périmètre exact, succès commercial ?
- Les grosses PA (Pennylane, Cegid, Esker, Docaposte, Generix) ont-elles annoncé des connecteurs ODBC/legacy ?
- Y a-t-il des produits « passerelle Factur-X pour AS400 » ou « pour Sage 30/100 i7 » déjà commercialisés ?
- Que proposent les intégrateurs EDI historiques (Tenor, TX2, Generix) pour les socles anciens ?

**Ne pas refaire** : la cartographie concurrentielle macro (ERP modernes, PA généralistes, GED) est dans `Cadrage` §7 — elle conclut que ce ne sont pas des concurrents frontaux. Cette DR cherche les concurrents FRONTAUX uniquement.

**Livrable** : tableau nominatif des concurrents directs + analyse « la niche est-elle ouverte, en train de se fermer, ou déjà prise ».

---

### <a name="dr8"></a>DR8 — Dimensionnement réel du marché adressable non-API

**Objectif** : remplacer l'estimation non sourcée « plusieurs dizaines de milliers d'entreprises sur socle non-API » (`Cadrage` §3.2) par un chiffrage étayé. Conditionne la crédibilité des scénarios CA (DR9).

**Question de recherche (prête à lancer)** :
> Combien d'entreprises françaises utilisent encore en 2026 un logiciel de gestion/facturation incapable de produire nativement une facture électronique conforme (EN 16931 / Factur-X) ou de dialoguer avec une Plateforme Agréée via API ? Croiser : (a) parc AS400/IBM i en France (entreprises, pas machines), (b) parc Sage 30/100/i7 on-premise non migré, (c) parc anciens Navision/Dynamics on-premise, (d) parc applications Windev/Delphi/Access/FileMaker métier, (e) ERP verticaux propriétaires anciens, (f) statistiques d'impréparation (baromètres OpinionWay/CNOEC, Quadient, Tiime) ventilées par taille et secteur. Distinguer : entreprises soumises à émission 2026 (GE/ETI) vs 2027 (PME/TPE). Produire une fourchette basse/haute du marché adressable pour une solution de type passerelle, avec les hypothèses explicites.

**Ne pas refaire** : les chiffres macro (~10 M assujettis, >4 M B2B, 2-3 Md factures) sont dans `Cadrage` §3.1 — ce n'est PAS le marché adressable. Le piège du « 100 000 entreprises AS400 » (chiffre mondial) est déjà identifié dans `Cadrage` §12.

**Livrable** : fourchette chiffrée et sourcée du marché adressable, ventilée par type de socle et par échéance (2026 vs 2027).

**Limite connue** : les parcs installés des éditeurs sont rarement publics → la DR produira des estimations par triangulation (offres d'emploi, annuaires partenaires, communiqués). L'assumer.

---

## Phase 2 — Économie

### <a name="dr9"></a>DR9 — Business model / pricing / scénarios de CA

**Objectif** : répondre à la question centrale — combien peut-on raisonnablement générer avec une solution ciblée, rapide à vendre, peu coûteuse à déployer, dans une fenêtre 2026-2028 ?

**Question de recherche (prête à lancer)** :
> Pour une micro-structure (développeur solo) commercialisant une passerelle de conformité facturation électronique pour logiciels legacy en France : construire des scénarios de chiffre d'affaires 2026-2029 réalistes. Comparer les modèles économiques : (a) setup + abonnement mensuel par client final, (b) pricing au volume de factures, (c) licence/royalties par éditeur partenaire en marque blanche (par client du parc), (d) prestation pure (forfait + TMA), (e) modèles hybrides. Pour chaque modèle : panier moyen constaté sur le marché français, coût d'acquisition client typique B2B de niche, durée de rétention probable (sachant que le marché legacy s'érode par migration), saisonnalité liée aux échéances (sept. 2026 / sept. 2027). Intégrer : la contrainte de capacité d'un solo (jours facturables limités), le scénario d'un report réglementaire (clause de sauvegarde), et le potentiel résiduel après la vague initiale (maintenance, ViDA 2030). Produire 3 scénarios (prudent / médian / optimiste) avec leurs hypothèses.

**Sous-questions clés** :
- Distinguer pricing **client final** vs pricing **éditeur/marque blanche** — deux modèles différents ; le second est la vraie stratégie (grappes).
- Le client paiera-t-il plus que l'amende (plafond 15 000 €/an) ? Psychologie du prix vs sanction.
- Combien de déploiements un solo peut-il absorber par mois si le produit est packagé ?

**Ne pas refaire** : les benchmarks de prix bruts existent déjà — `Cadrage` §8 (setup 1 500-5 000 €, abo 100-300 €/mois, 0,05-0,15 €/doc) et `L'opportunité` §5 (forfaits 5 000-50 000 €, TJM 600-700 €). Les réutiliser comme données d'entrée, les faire seulement re-vérifier/actualiser.

**Dépend de** : DR7 (prix des alternatives = plafond de prix), DR8 (taille du marché = plafond de volume).

**Livrable** : 3 scénarios CA chiffrés + recommandation de modèle de pricing + seuils de décision.

---

## Phase 3 — Cibles

### <a name="dr10"></a>DR10 — Secteurs propices + éditeurs nominatifs par secteur (fusion des niveaux 1 et 2)

**Objectif** : passer de la liste générique de secteurs à un scoring chiffré ET à des listes nominatives d'éditeurs par secteur. Une seule DR pour les deux niveaux : la recherche secteur sans les noms d'éditeurs ne sert à rien.

**Question de recherche (prête à lancer)** :
> Identifier les marchés verticaux français où des logiciels métier legacy (on-premise, anciens, sans API) sont encore dominants et où la réforme de la facturation électronique 2026-2027 crée une contrainte forte. Pour chaque secteur candidat — ventes aux enchères/SVV, ports/criées, BTP, négoce spécialisé (agricole, matériaux, vins), transport/logistique, garages/distribution automobile, imprimeries, coopératives agricoles, santé/médico-social, associations/fédérations, gestion locative/syndics — évaluer : volume d'entreprises, intensité de facturation B2B vs B2C, degré de modernisation IT, fragmentation, présence d'éditeurs spécialisés. PUIS, pour les 5-6 secteurs les mieux notés : lister nominativement les éditeurs de logiciels métier (nom, produit, ancienneté, techno probable, taille estimée du parc, présence ou absence d'une offre facturation électronique annoncée). Prioriser les éditeurs créés avant 2010, on-premise, sans communication visible sur la réforme.

**Ne pas refaire** : la MÉTHODE de détection automatisée (signaux France Travail, Pappers, scoring, RGPD/CNIL) est entièrement traitée dans `Stratégie Automatisation Prospection B2B Niche.md`. Cette DR produit des LISTES NOMINATIVES par recherche documentaire ; l'automatisation prendra le relais ensuite pour l'industrialisation. Les critères ICP (création <2010, parc 50-500 clients, homogénéité du schéma, pas d'offre PDP annoncée) sont déjà définis dans ce même document — les réutiliser tels quels comme grille de scoring.

**Livrable** : tableau de scoring des secteurs + liste nominative de 30-50 éditeurs avec score de priorité.

---

### <a name="dr11"></a>DR11 — Analyse approfondie des meilleurs éditeurs (niveau 3)

**Objectif** : pour les 5-10 éditeurs les mieux scorés en DR10, produire une fiche d'approche commerciale exploitable.

**Question de recherche (à paramétrer avec les noms issus de DR10)** :
> Pour chacun des éditeurs suivants [LISTE ISSUE DE DR10] : que fait exactement leur logiciel, quelle est leur clientèle (taille, typologie, nombre estimé de clients), quelle est leur stack technique probable (indices : offres d'emploi, mentions techniques, captures d'écran produit), ont-ils une API, quelle est leur stratégie visible sur la facturation électronique (partenariat PA annoncé, module en développement, silence), qui sont leurs dirigeants et intégrateurs/revendeurs, et quel serait le meilleur angle d'approche pour leur proposer un partenariat de mise en conformité de leur parc (marque blanche / sous-traitance / co-développement) ?

**Ne pas refaire** : la structure de la fiche et la rhétorique d'approche (cold email, alliance d'ingénierie) sont dans `Stratégie Automatisation Prospection` — réutiliser.

**Dépend de** : DR10 (fournit les noms).

**Livrable** : une fiche par éditeur + priorisation des 3 premiers à contacter.

---

### <a name="dr12"></a>DR12 — Intelligence ciblée ISATECH

**Objectif** : préparer la négociation avec le partenaire fondateur AVANT qu'il ne se réveille. C'est le niveau 3 appliqué au cas le plus important.

**Question de recherche (prête à lancer)** :
> Tout ce qui est publiquement connaissable sur ISATECH (éditeur/intégrateur français, notamment du logiciel EncheresV6 pour les sociétés de ventes volontaires) : taille, santé financière (comptes publiés, Pappers/societe.com), produits et clientèle, positionnement sur Microsoft Dynamics, communication sur la facturation électronique 2026-2027, partenariats PA annoncés, offres d'emploi récentes (signaux sur leurs priorités R&D), clients SVV identifiables, intégrateurs/revendeurs. Objectif : évaluer leur capacité et leur intention de développer eux-mêmes un module de conformité pour leur parc legacy EncheresV6, et identifier le meilleur levier de négociation pour un partenariat (marque blanche, sous-traitance, apport d'affaires).

**Contexte interne à fournir à la DR** : ISATECH ne livrera rien pour EncheresV6 avant 2027 ; la majorité du parc ne peut pas migrer d'ici là ; le serveur azmut-enbase01 héberge une base par étude (grappe à coût marginal nul). Voir `Conception-Produit-Passerelle.md` §2.

**Livrable** : fiche ISATECH + scénarios de partenariat avec rapport de force.

---

## Phase 4 — Acquisition (lancer les trois en parallèle pour COMPARER les canaux avant d'investir)

### <a name="dr13"></a>DR13 — Canaux prescripteurs : experts-comptables, ESN, syndicats professionnels, salons

**Objectif** : évaluer le canal indirect — potentiellement plus adapté à la cible que l'inbound (la cible DSI/gérant legacy n'est pas forcément sur YouTube/Google).

**Question de recherche (prête à lancer)** :
> En France en 2026, comment une micro-structure proposant une passerelle de conformité facturation électronique pour logiciels legacy peut-elle être prescrite/distribuée par des tiers ? Analyser : (a) les experts-comptables — comment choisissent-ils et prescrivent-ils les solutions à leurs clients « techniquement bloqués », rôle du CNOEC et du guide MaFacture-MonExpert.fr, programmes partenaires des cabinets, (b) les ESN/MSP régionales — modèles de partenariat marque blanche, comment les approcher, (c) les syndicats et fédérations professionnelles par secteur (SYMEV pour les enchères, FFB pour le BTP, etc.) — peut-on devenir « solution référencée », conditions, (d) les salons et événements (Journée Facture Électronique, salons sectoriels) — coût, accessibilité pour un solo, (e) le réseau FNFE-MPE. Pour chaque canal : coût d'entrée, délai, potentiel de volume, exemples d'acteurs qui l'utilisent avec succès.

**Ne pas refaire** : la prospection directe automatisée (cold email) est couverte par `Stratégie Automatisation Prospection`. Cette DR couvre les canaux INDIRECTS uniquement.

**Livrable** : comparatif des canaux prescripteurs + plan d'action pour les 2 meilleurs.

---

### <a name="dr14"></a>DR14 — Acquisition YouTube

**Objectif** : valider (ou invalider) l'hypothèse « 100 vues = ~100 prospects qualifiés » et produire un plan éditorial si elle tient.

**Question de recherche (prête à lancer)** :
> Le canal YouTube est-il pertinent pour acquérir des clients B2B de niche sur la facturation électronique française en 2026 ? Analyser : (a) l'offre existante — quelles chaînes francophones traitent de la facturation électronique / Factur-X / PDP-PA, quels volumes de vues, qui est l'audience (experts-comptables ? dirigeants TPE ? DSI ?), quels sujets sont saturés vs absents, (b) la demande — volumes de recherche YouTube sur les requêtes liées (facturation électronique 2026, logiciel non compatible, Factur-X, API PDP), (c) les sujets à forte intention commerciale non couverts, notamment l'angle « logiciel ancien/legacy non compatible » — qui semble absent, (d) des benchmarks de conversion YouTube B2B de niche (vues → leads → clients) pour évaluer l'hypothèse qu'une audience faible mais ultra-qualifiée suffit, (e) une liste de 10-15 vidéos prioritaires avec mots-clés, angle et intention. Conclure : ROI attendu vs effort de production pour un solo, comparé à d'autres canaux.

**Sous-question critique** : la cible réelle (DSI d'éditeur legacy, gérant de PME sur AS400) cherche-t-elle ses solutions sur YouTube, ou est-ce un canal qui touche surtout les experts-comptables et TPE déjà servis par les PA gratuites ?

**Livrable** : verdict sur le canal + plan éditorial (si positif) + mots-clés.

---

### <a name="dr15"></a>DR15 — SEO / site web longue traîne

**Objectif** : structure de site et stratégie de contenu pour ranker vite sur des requêtes à forte intention, sans affronter les gros acteurs.

**Question de recherche (prête à lancer)** :
> Stratégie SEO longue traîne pour une passerelle de conformité facturation électronique ciblant les logiciels legacy en France : (a) cartographier la concurrence SEO — qui occupe les SERP sur les requêtes principales (facturation électronique 2026, plateforme agréée, Factur-X) et confirmer qu'elles sont inaccessibles, (b) identifier les requêtes longue traîne à forte intention et faible concurrence : par problème (« logiciel non compatible facturation électronique », « rendre un vieux logiciel conforme »), par techno (« AS400 facturation électronique », « Sage 100 i7 Factur-X », « Windev facture électronique », « Access facturation 2026 »), par secteur (issues de DR10), par éditeur (« [logiciel X] facturation électronique » — pages à créer pour chaque éditeur identifié en DR10), (c) volumes de recherche et difficulté estimés pour ces requêtes, (d) structure de site recommandée (pages piliers, pages programmatiques secteur/techno/logiciel, démo, cas d'usage CMP), (e) exemples de sites B2B de niche français ayant réussi cette stratégie. Produire l'arborescence du site et la liste des 30 premières pages à créer, priorisées.

**Synergie** : les pages « secteur » et « logiciel X » découlent directement de DR10 → lancer DR15 après ou en parallèle de DR10.

**Livrable** : arborescence du site + liste priorisée des pages + requêtes cibles.

---

## Phase 5 — Offre et partenaires

### <a name="dr16"></a>DR16 — Offre « réception » comme produit d'appel

**Objectif** : combler un gap produit/marché. L'obligation universelle et la plus proche est la RÉCEPTION (1er sept. 2026, toutes entreprises) — or la Passerelle est conçue émission/e-reporting uniquement. Le `Cadrage` §10 recommandait d'en faire le déclencheur d'achat.

**Question de recherche (prête à lancer)** :
> Au 1er septembre 2026, toute entreprise française doit pouvoir RECEVOIR des factures électroniques via une Plateforme Agréée. Pour une entreprise équipée d'un logiciel legacy sans API : (a) que se passe-t-il concrètement si elle ne fait rien (les factures arrivent où ? sanctions ? blocages fournisseurs ?), (b) quel est le besoin réel d'intégration — consulter les factures reçues sur le portail de la PA suffit-il, ou y a-t-il une vraie demande d'injection des factures fournisseurs DANS le SI legacy (compta fournisseurs, rapprochement) ?, (c) quelles offres existent pour ce besoin et à quel prix, (d) est-ce un produit vendable en soi pour une passerelle legacy, ou seulement un produit d'appel/porte d'entrée commerciale vers l'offre émission 2027 ? Évaluer l'opportunité d'une offre « réception » low-cost comme générateur de leads pour la vague d'émission PME/TPE de septembre 2027.

**Dépend de** : DR7 (qui sert déjà ce besoin).

**Livrable** : verdict produit d'appel ou vrai produit + esquisse de l'offre si pertinente.

---

### <a name="dr17"></a>DR17 — Stratégie multi-PA / conditions partenaires

**Objectif** : réduire la dépendance à B2Brouter et connaître les conditions de gros réelles avant de figer le pricing.

**Question de recherche (prête à lancer)** :
> Comparer les Plateformes Agréées françaises proposant une API en marque blanche / un programme partenaire adapté à un intégrateur-éditeur de Solution Compatible (juin 2026) : B2Brouter, Seqino, Iopole, SuperPDP, et autres PA « API-first ». Pour chacune : modèle de partenariat (reseller, affiliation, marque blanche, tarif de gros), conditions publiques, qualité et couverture de l'API (émission, réception, e-reporting transaction ET paiement, statuts cycle de vie), support de la norme AFNOR XP Z12-013, hébergement (SecNumCloud ?), santé/pérennité de l'entreprise, présence française (support, documentation). Identifier : (a) la meilleure alternative de secours à B2Brouter, (b) les critères de bascule, (c) ce que coûterait la portabilité multi-PA dans l'architecture.

**Ne pas refaire** : les programmes d'affiliation grand public (Sellsy 20 %, Axonaut 25 %) sont dans `L'opportunité` §4 — et la conclusion (la commission seule ne fait pas un business) est acquise.

**⚠️ Limite importante** : les taux de commission et tarifs de gros sont largement NON PUBLICS. Cette DR ne donnera que le cadre ; le contact commercial direct avec 2-3 PA reste indispensable → voir Actions directes ci-dessous.

**Livrable** : comparatif des PA partenaires + recommandation de PA de secours.

---

## Actions directes (PAS des deep researches — à faire par contact humain)

Ces points reviennent dans plusieurs DR mais ne sont pas researchables en ligne. Ne pas attendre des DR ce qu'elles ne peuvent pas donner.

| # | Action | Lié à | Qui contacter |
|---|---|---|---|
| AC1 | Obtenir les conditions reseller/marque blanche réelles de B2Brouter (taux, tarif de gros, engagement) | DR9, DR17 | B2Brouter commercial |
| AC2 | Obtenir les conditions d'1-2 PA alternatives (Seqino, Iopole) | DR17 | Commercial des PA |
| AC3 | Sonder 2-3 experts-comptables sur leur processus de prescription réel | DR13 | Réseau perso / CMP |
| AC4 | Valider auprès du CMP le volume d'acheteurs professionnels (donnée interne) | DR9 | CMP (déjà en A3 du README Conception) |
| AC5 | Vérifier la disponibilité du nom de produit (INPI, domaine) une fois choisi | Point ouvert #1 | INPI / registrar |

---

## Méthode de lancement des DR

1. **Une DR à la fois ou par paire parallèle** (selon l'ordre recommandé en tête de document), pour pouvoir injecter les résultats des unes dans les questions des autres.
2. **Fournir le contexte interne** à chaque DR (extraits des documents existants pertinents) pour éviter qu'elle redécouvre ce qu'on sait déjà.
3. **À chaque DR terminée** : mettre à jour le tableau de statuts ci-dessus + noter la solidité de la recherche (leçon du README Conception : distinguer « confirmé », « abstention » et « réfuté » — une abstention n'est pas une réfutation).
4. **Critère d'arrêt** : si DR7 révèle un concurrent direct installé et bien financé, ou si DR8 révèle un marché adressable < 5 000 entreprises, STOP et réévaluation de la stratégie avant de lancer les phases 3-5.
